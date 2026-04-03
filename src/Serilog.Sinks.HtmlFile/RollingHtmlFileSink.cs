using System;
using System.IO;
using System.Text;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Formatting;

namespace Serilog.Sinks.HtmlFile
{
    /// <summary>
    /// A Serilog sink that delegates to <see cref="HtmlFileSink"/> and performs
    /// size-based file rolling. When the current file exceeds the configured
    /// size limit, it is renamed to an archive and a fresh file is created.
    /// </summary>
    internal sealed class RollingHtmlFileSink : ILogEventSink, IDisposable
    {
        readonly object _syncRoot = new object();
        readonly ITextFormatter _formatter;
        readonly IHtmlTemplate _template;
        readonly long _fileSizeLimitBytes;
        readonly Encoding _encoding;
        readonly string _archiveNamingPattern;
        readonly string _archiveTimestampFormat;
        readonly string? _fileNamingPattern;
        readonly string _dateFormat;

        string _currentPath;
        HtmlFileSink? _currentSink;
        bool _disposed;

        /// <summary>
        /// Constructs a new <see cref="RollingHtmlFileSink"/>.
        /// </summary>
        /// <param name="path">The file path for the HTML log file.</param>
        /// <param name="formatter">The formatter used to convert log events to JS object literals.</param>
        /// <param name="template">The HTML template providing header and tail sections.</param>
        /// <param name="fileSizeLimitBytes">Maximum file size in bytes before rolling occurs.</param>
        /// <param name="encoding">The character encoding to use. Defaults to UTF-8 without BOM.</param>
        /// <param name="archiveNamingPattern">The archive naming pattern. Defaults to "{BaseName}_{Timestamp}{Extension}".</param>
        /// <param name="archiveTimestampFormat">The timestamp format for archive names. Defaults to "yyyyMMddHHmmss".</param>
        /// <param name="fileNamingPattern">Optional active file naming pattern with placeholders like {Date} and {MachineName}.</param>
        /// <param name="dateFormat">The date format for the {Date} placeholder. Defaults to "yyyy-MM-dd".</param>
        public RollingHtmlFileSink(
            string path,
            ITextFormatter formatter,
            IHtmlTemplate template,
            long fileSizeLimitBytes,
            Encoding encoding,
            string archiveNamingPattern = ArchiveNamingHelper.DefaultPattern,
            string archiveTimestampFormat = ArchiveNamingHelper.DefaultTimestampFormat,
            string? fileNamingPattern = null,
            string dateFormat = FileNamingHelper.DefaultDateFormat)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
            _template = template ?? throw new ArgumentNullException(nameof(template));
            _fileSizeLimitBytes = fileSizeLimitBytes;
            _encoding = encoding ?? new UTF8Encoding(false);
            _archiveNamingPattern = archiveNamingPattern;
            _archiveTimestampFormat = archiveTimestampFormat;
            _fileNamingPattern = fileNamingPattern;
            _dateFormat = dateFormat;

            // Validate archive naming configuration (fail-fast)
            ArchiveNamingHelper.Validate(_archiveNamingPattern, _archiveTimestampFormat);

            // Determine the initial active file path
            if (_fileNamingPattern != null)
            {
                FileNamingHelper.Validate(_fileNamingPattern);
                _currentPath = FileNamingHelper.EvaluatePattern(_fileNamingPattern, _dateFormat);
            }
            else
            {
                _currentPath = path;
            }

            _currentSink = new HtmlFileSink(_currentPath, _formatter, _template, null, _encoding);
        }

        /// <inheritdoc />
        public void Emit(LogEvent logEvent)
        {
            if (logEvent == null) throw new ArgumentNullException(nameof(logEvent));

            lock (_syncRoot)
            {
                if (_disposed)
                    return;

                if (_currentSink!.FileSize >= _fileSizeLimitBytes)
                {
                    TryRoll();
                }

                _currentSink!.Emit(logEvent);
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
                _currentSink?.Dispose();
                _currentSink = null;
            }
        }

        void TryRoll()
        {
            try
            {
                _currentSink!.Dispose();

                var directory = Path.GetDirectoryName(_currentPath);
                var baseName = Path.GetFileNameWithoutExtension(_currentPath);
                var ext = Path.GetExtension(_currentPath);
                var utcNow = DateTime.UtcNow;

                var archiveName = ArchiveNamingHelper.FormatArchiveName(
                    _archiveNamingPattern,
                    baseName,
                    utcNow,
                    ext,
                    _archiveTimestampFormat);

                var archivePath = string.IsNullOrEmpty(directory)
                    ? archiveName
                    : Path.Combine(directory, archiveName);

                File.Move(_currentPath, archivePath);
            }
            catch (IOException ex)
            {
                SelfLog.WriteLine("Serilog.Sinks.HtmlFile: failed to roll HTML log file '{0}': {1}", _currentPath, ex);
            }
            catch (Exception ex)
            {
                SelfLog.WriteLine("Serilog.Sinks.HtmlFile: unexpected error rolling HTML log file '{0}': {1}", _currentPath, ex);
            }

            // Re-evaluate the file naming pattern if set, to get a fresh path (e.g., new date)
            if (_fileNamingPattern != null)
                _currentPath = FileNamingHelper.EvaluatePattern(_fileNamingPattern, _dateFormat);

            // Always create a new sink, whether the rename succeeded or not.
            _currentSink = new HtmlFileSink(_currentPath, _formatter, _template, null, _encoding);
        }
    }
}
