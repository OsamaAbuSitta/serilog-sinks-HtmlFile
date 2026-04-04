using System;
using System.IO;
using System.Text;
using System.Threading;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Formatting;

namespace Serilog.Sinks.HtmlFile
{
    /// <summary>
    /// A Serilog sink that writes log events to a self-contained HTML file
    /// with incremental append via seek-based insertion.
    /// </summary>
    internal sealed class HtmlFileSink : ILogEventSink, IDisposable
    {
        static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(5);

        readonly object _syncRoot = new object();
        readonly ITextFormatter _formatter;
        readonly IHtmlTemplate _template;
        readonly long? _fileSizeLimitBytes;
        readonly Encoding _encoding;
        readonly string _tailContent;

        FileStream? _fileStream;
        StreamWriter? _output;
        long _insertionOffset;
        bool _disposed;
        bool _sizeLimitReached;

        const int TailBufferSize = 4096;

        /// <summary>
        /// Gets the current size of the underlying file in bytes,
        /// or zero if the file is not open.
        /// </summary>
        public long FileSize
        {
            get
            {
                lock (_syncRoot)
                {
                    return _fileStream?.Length ?? 0;
                }
            }
        }

        /// <summary>
        /// Constructs a new <see cref="HtmlFileSink"/>.
        /// </summary>
        /// <param name="path">The file path for the HTML log file.</param>
        /// <param name="formatter">The formatter used to convert log events to JS object literals.</param>
        /// <param name="template">The HTML template providing header and tail sections.</param>
        /// <param name="fileSizeLimitBytes">Optional maximum file size in bytes before the sink stops writing.</param>
        /// <param name="encoding">The character encoding to use. Defaults to UTF-8 without BOM.</param>
        public HtmlFileSink(
            string path,
            ITextFormatter formatter,
            IHtmlTemplate template,
            long? fileSizeLimitBytes,
            Encoding encoding)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
            _template = template ?? throw new ArgumentNullException(nameof(template));
            _fileSizeLimitBytes = fileSizeLimitBytes;
            _encoding = encoding ?? new UTF8Encoding(false);

            // Pre-compute the tail content so we can rewrite it after each entry
            var tailWriter = new StringWriter();
            _template.WriteTail(tailWriter);
            _tailContent = tailWriter.ToString();

            try
            {
                var directory = Path.GetDirectoryName(path);
                
                var fileName = FileNamingHelper.EvaluatePattern(Path.GetFileName(path));
                var filePath = Path.Combine(directory, fileName);
                
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                var fileExists = File.Exists(filePath);

                _fileStream = new FileStream(
                    filePath,
                    fileExists ? FileMode.Open : FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.Read);

                _output = new StreamWriter(_fileStream, _encoding);

                if (fileExists)
                {
                    RecoverInsertionOffset();
                }
                else
                {
                    InitializeNewFile();
                }
            }
            catch (Exception ex)
            {
                SelfLog.WriteLine("Unable to open HTML log file {0}: {1}", path, ex);
                DisposeStreams();
            }
        }

        /// <inheritdoc />
        public void Emit(LogEvent logEvent)
        {
            if (logEvent == null) throw new ArgumentNullException(nameof(logEvent));

            if (_fileStream == null)
                return; // sink is no-op if file could not be opened

            if (!Monitor.TryEnter(_syncRoot, LockTimeout))
            {
                SelfLog.WriteLine("Unable to acquire lock to write log event within timeout; dropping event");
                return;
            }

            try
            {
                if (_disposed || _fileStream == null)
                    return;

                // Check file size limit — if exceeded, stop writing
                if (_fileSizeLimitBytes.HasValue && _fileStream.Length >= _fileSizeLimitBytes.Value)
                {
                    if (!_sizeLimitReached)
                    {
                        _sizeLimitReached = true;
                        SelfLog.WriteLine(
                            "Serilog.Sinks.HtmlFile: size limit of {0} bytes reached for file '{1}'. Further writes suppressed.",
                            _fileSizeLimitBytes.Value, _fileStream.Name);
                    }
                    return;
                }

                // Format the log event into a string
                var buffer = new StringWriter();
                _formatter.Format(logEvent, buffer);
                var entry = buffer.ToString();

                // Seek to the insertion offset
                _output!.Flush();
                _fileStream!.Seek(_insertionOffset, SeekOrigin.Begin);

                // Write the entry bytes directly to the file stream
                var entryBytes = _encoding.GetBytes(entry);
                var tailBytes = _encoding.GetBytes(_tailContent);

                _fileStream.Write(entryBytes, 0, entryBytes.Length);

                // Update insertion offset to after the entry
                _insertionOffset = _fileStream.Position;

                // Write the closing tags
                _fileStream.Write(tailBytes, 0, tailBytes.Length);

                // Truncate any leftover content and flush
                _fileStream.SetLength(_fileStream.Position);
                _fileStream.Flush();
            }
            catch (Exception ex)
            {
                SelfLog.WriteLine("Failed to write log event to HTML file: {0}", ex);
            }
            finally
            {
                Monitor.Exit(_syncRoot);
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            lock (_syncRoot)
            {
                if (_disposed)
                    return;
                _disposed = true;
                DisposeStreams();
            }
        }

        void InitializeNewFile()
        {
            // Write the header (everything before the insertion point)
            _template.WriteHeader(_output!);
            _output!.Flush();

            // Record the insertion offset (right after the header)
            _insertionOffset = _fileStream!.Position;

            // Write the tail (closing tags)
            _template.WriteTail(_output);
            _output.Flush();
        }

        void RecoverInsertionOffset()
        {
            var marker = _template.InsertionMarker;
            var fileSize = _fileStream!.Length;
            var readSize = (int)Math.Min(fileSize, TailBufferSize);

            // Read only the tail of the file — the marker is always near the end
            _fileStream.Seek(-readSize, SeekOrigin.End);
            var buffer = new byte[readSize];
            _ = _fileStream.Read(buffer, 0, readSize);
            var tail = _encoding.GetString(buffer);

            var markerIndexInTail = tail.LastIndexOf(marker, StringComparison.Ordinal);
            if (markerIndexInTail >= 0)
            {
                var bytesBeforeMarkerInTail = _encoding.GetByteCount(tail.Substring(0, markerIndexInTail));
                _insertionOffset = (fileSize - readSize) + bytesBeforeMarkerInTail;
                return;
            }

            // Fallback: marker was not in the tail buffer — do a full-file scan
            SelfLog.WriteLine(
                "Serilog.Sinks.HtmlFile: insertion marker not found in tail buffer for file, falling back to full scan.");
            _fileStream.Seek(0, SeekOrigin.Begin);
            var reader = new StreamReader(_fileStream, _encoding, false, 4096, leaveOpen: true);
            var content = reader.ReadToEnd();
            var markerIndex = content.IndexOf(marker, StringComparison.Ordinal);

            if (markerIndex >= 0)
            {
                _insertionOffset = _encoding.GetByteCount(content.Substring(0, markerIndex));
            }
            else
            {
                // Marker not found even in full scan — append to end
                SelfLog.WriteLine("Serilog.Sinks.HtmlFile: insertion marker not found; appending to end of file.");
                _fileStream.Seek(0, SeekOrigin.End);
                _insertionOffset = _fileStream.Position;
            }
        }

        void DisposeStreams()
        {
            try
            {
                _output?.Dispose();
            }
            catch
            {
                // Swallow
            }

            try
            {
                _fileStream?.Dispose();
            }
            catch
            {
                // Swallow
            }

            _output = null;
            _fileStream = null;
        }
    }
}
