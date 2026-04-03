using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Parsing;

namespace Serilog.Sinks.HtmlFile.Tests;

[Collection("SelfLog")]
public class HtmlFileSinkTests : IDisposable
{
    private readonly string _tempDir;
    private readonly HtmlLogEventFormatter _formatter = new();
    private readonly HtmlTemplate _template = new();
    private readonly Encoding _encoding = new UTF8Encoding(false);

    public HtmlFileSinkTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "HtmlFileSinkTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch
        {
            // Best-effort cleanup
        }
    }

    private static LogEvent CreateEvent(string message = "Hello")
    {
        var tokens = new List<MessageTemplateToken> { new TextToken(message) };
        var template = new MessageTemplate(tokens);
        return new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            null,
            template,
            Array.Empty<LogEventProperty>());
    }

    // -------------------------------------------------------------------------
    // Tail-read recovery tests (Requirement 1)
    // -------------------------------------------------------------------------

    // Validates: Requirements 1.1, 1.4 — reopen and append; new entry must follow existing ones
    [Fact]
    public void ReopenedFile_AppendedEntryAppearsAfterExistingEntries()
    {
        var path = Path.Combine(_tempDir, "reopen.html");

        using (var sink = new HtmlFileSink(path, _formatter, _template, null, _encoding))
        {
            sink.Emit(CreateEvent("first"));
            sink.Emit(CreateEvent("second"));
        }

        using (var sink = new HtmlFileSink(path, _formatter, _template, null, _encoding))
        {
            sink.Emit(CreateEvent("third"));
        }

        var content = File.ReadAllText(path);

        var idxFirst  = content.IndexOf("first",  StringComparison.Ordinal);
        var idxSecond = content.IndexOf("second", StringComparison.Ordinal);
        var idxThird  = content.IndexOf("third",  StringComparison.Ordinal);

        Assert.True(idxFirst  >= 0, "\"first\" not found");
        Assert.True(idxSecond >= 0, "\"second\" not found");
        Assert.True(idxThird  >= 0, "\"third\" not found");
        Assert.True(idxFirst  < idxSecond, "\"first\" must precede \"second\"");
        Assert.True(idxSecond < idxThird,  "\"second\" must precede \"third\"");
    }

    // Validates: Requirements 1.1, 1.4 — file stays structurally valid after reopen + append
    [Fact]
    public void ReopenedFile_RemainsStructurallyValid()
    {
        var path = Path.Combine(_tempDir, "valid.html");

        using (var sink = new HtmlFileSink(path, _formatter, _template, null, _encoding))
            sink.Emit(CreateEvent("initial"));

        using (var sink = new HtmlFileSink(path, _formatter, _template, null, _encoding))
            sink.Emit(CreateEvent("appended"));

        var content = File.ReadAllText(path);

        Assert.Contains("var logEntries = [", content);
        Assert.Contains(_template.InsertionMarker, content);
        Assert.Contains("];", content);
        Assert.Contains("</body>", content);
        Assert.Contains("</html>", content);
    }

    // Validates: Requirement 1.5 — new file must not go through the recovery path
    [Fact]
    public void NewFile_EntryIsWrittenCorrectly()
    {
        var path = Path.Combine(_tempDir, "newfile.html");

        using (var sink = new HtmlFileSink(path, _formatter, _template, null, _encoding))
            sink.Emit(CreateEvent("new file entry"));

        var content = File.ReadAllText(path);
        Assert.Contains("new file entry", content);
        Assert.Contains("</html>", content);
    }

    // Validates: Requirement 1.3 — fallback triggers when marker sits beyond the tail buffer
    [Fact]
    public void RecoverInsertionOffset_FallsBack_WhenMarkerBeyondTailBuffer()
    {
        var path = Path.Combine(_tempDir, "large.html");

        // PaddedTailTemplate appends 5 KB of padding AFTER the insertion marker,
        // pushing the marker more than 4 KB from the end of the file.
        var customTemplate = new PaddedSuffixTemplate(5000);

        using (var sink = new HtmlFileSink(path, _formatter, customTemplate, null, _encoding))
            sink.Emit(CreateEvent("first in large file"));

        var warnings = new List<string>();
        SelfLog.Enable(msg => warnings.Add(msg));
        try
        {
            using (var sink = new HtmlFileSink(path, _formatter, customTemplate, null, _encoding))
                sink.Emit(CreateEvent("second in large file"));
        }
        finally
        {
            SelfLog.Disable();
        }

        var content = File.ReadAllText(path);
        Assert.Contains("first in large file",  content);
        Assert.Contains("second in large file", content);
        // Fallback warning must have been emitted
        Assert.Contains(warnings, w => w.Contains("tail buffer"));
    }

    // -------------------------------------------------------------------------
    // Concurrent roll tests (Requirement 2)
    // -------------------------------------------------------------------------

    // Validates: Requirements 2.1, 2.4 — concurrent emits spanning multiple rolls lose no events
    [Fact]
    public void ConcurrentEmitsDuringRoll_NoEventsLost()
    {
        var path = Path.Combine(_tempDir, "concurrent.html");
        const int threadCount = 4;
        const int eventsPerThread = 20;
        // Large enough limit that rolling happens a few times but not on every event
        const long sizeLimit = 5000;

        var messages = new System.Collections.Concurrent.ConcurrentBag<string>();

        using (var sink = new RollingHtmlFileSink(path, _formatter, _template, sizeLimit, _encoding))
        {
            var threads = new Thread[threadCount];
            for (var i = 0; i < threadCount; i++)
            {
                var threadId = i;
                threads[i] = new Thread(() =>
                {
                    for (var j = 0; j < eventsPerThread; j++)
                    {
                        var msg = $"t{threadId}-e{j}";
                        messages.Add(msg);
                        sink.Emit(CreateEvent(msg));
                    }
                });
            }

            foreach (var t in threads) t.Start();
            foreach (var t in threads) t.Join();
        }

        // Collect content from all produced files (active + all archives)
        var allContent = new StringBuilder();
        foreach (var file in Directory.GetFiles(_tempDir, "*.html"))
            allContent.Append(File.ReadAllText(file));

        var combined = allContent.ToString();
        foreach (var msg in messages)
            Assert.Contains(msg, combined);
    }

    // Validates: Requirements 2.2, 2.3 — File.Move failure is caught; new sink keeps writing
    [Fact]
    public void RollWithMoveFailure_NewSinkContinuesWriting()
    {
        var path = Path.Combine(_tempDir, "movefail.html");
        const long sizeLimit = 500;

        var selfLogMessages = new List<string>();
        SelfLog.Enable(msg => selfLogMessages.Add(msg));
        try
        {
            using (var sink = new RollingHtmlFileSink(path, _formatter, _template, sizeLimit, _encoding,
                // Use a custom archive pattern that conflicts — same archive name every time
                archiveNamingPattern: "{BaseName}-archive{Extension}",
                archiveTimestampFormat: "yyyyMMddHHmmss"))
            {
                // First big event fills the file past limit
                sink.Emit(CreateEvent(new string('X', 600)));
                // Triggers roll #1 → archive created as "movefail-archive.html"
                sink.Emit(CreateEvent("triggers roll 1"));

                // Fill again
                sink.Emit(CreateEvent(new string('Y', 600)));
                // Triggers roll #2 → tries to move to "movefail-archive.html" again → IOException
                sink.Emit(CreateEvent("triggers roll 2"));

                // New sink must still be functional
                sink.Emit(CreateEvent("still writing after move failure"));
            }
        }
        finally
        {
            SelfLog.Disable();
        }

        // The active file must contain the post-failure event
        var content = File.ReadAllText(path);
        Assert.Contains("still writing after move failure", content);

        // A SelfLog error about the failed roll must have been recorded
        Assert.Contains(selfLogMessages, m => m.Contains("roll") || m.Contains("archive") || m.Contains("failed"));
    }

    // -------------------------------------------------------------------------
    // Size-limit SelfLog warning tests (Requirement 3)
    // -------------------------------------------------------------------------

    // Validates: Requirements 3.1, 3.2, 3.3 — exactly one warning on first suppressed event
    [Fact]
    public void SizeLimitReached_EmitsExactlyOneWarning()
    {
        var path = Path.Combine(_tempDir, "sizelimit.html");
        var warnings = new List<string>();

        SelfLog.Enable(msg => warnings.Add(msg));
        try
        {
            using var sink = new HtmlFileSink(path, _formatter, _template, 500, _encoding);

            // First event fills the file past 500 bytes
            sink.Emit(CreateEvent(new string('Z', 600)));

            // These events should all be suppressed
            sink.Emit(CreateEvent("suppressed 1"));
            sink.Emit(CreateEvent("suppressed 2"));
            sink.Emit(CreateEvent("suppressed 3"));
        }
        finally
        {
            SelfLog.Disable();
        }

        var sizeLimitWarnings = warnings.FindAll(w =>
            w.Contains("size limit", StringComparison.OrdinalIgnoreCase) &&
            w.Contains("suppressed", StringComparison.OrdinalIgnoreCase));

        Assert.Single(sizeLimitWarnings);
        Assert.Contains("500", sizeLimitWarnings[0]);
    }

    // Validates: Requirement 3.4 — no warning when no size limit is configured
    [Fact]
    public void NoSizeLimit_NoSizeLimitWarningEmitted()
    {
        var path = Path.Combine(_tempDir, "nolimit.html");
        var warnings = new List<string>();

        SelfLog.Enable(msg => warnings.Add(msg));
        try
        {
            using var sink = new HtmlFileSink(path, _formatter, _template, null, _encoding);

            for (var i = 0; i < 10; i++)
                sink.Emit(CreateEvent($"event {i}"));
        }
        finally
        {
            SelfLog.Disable();
        }

        var sizeLimitWarnings = warnings.FindAll(w =>
            w.Contains("size limit", StringComparison.OrdinalIgnoreCase));

        Assert.Empty(sizeLimitWarnings);
    }

    // -------------------------------------------------------------------------
    // Helper: template whose tail has a large suffix after the insertion marker,
    // forcing the tail-read fallback code path.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Writes the standard HtmlTemplate tail followed by <paramref name="suffixLength"/>
    /// padding bytes. This pushes the insertion marker more than <c>TailBufferSize</c> (4 KB)
    /// bytes from the end of the file, triggering the full-file fallback in RecoverInsertionOffset.
    /// </summary>
    private sealed class PaddedSuffixTemplate : IHtmlTemplate
    {
        private readonly HtmlTemplate _inner = new();
        private readonly string _suffix;

        public PaddedSuffixTemplate(int suffixLength) =>
            _suffix = new string('A', suffixLength);

        public string InsertionMarker => _inner.InsertionMarker;

        public void WriteHeader(TextWriter output) => _inner.WriteHeader(output);

        public void WriteTail(TextWriter output)
        {
            _inner.WriteTail(output);     // marker is written here
            output.Write(_suffix);         // padding pushes marker away from end of file
        }
    }
}
