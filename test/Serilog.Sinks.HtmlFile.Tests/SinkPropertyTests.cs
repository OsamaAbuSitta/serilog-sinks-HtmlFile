using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FsCheck;
using FsCheck.Xunit;
using Serilog.Events;
using Serilog.Formatting;

namespace Serilog.Sinks.HtmlFile.Tests;

/// <summary>
/// Feature: production-readiness, Property 3: Thread-safe concurrent writes
/// Feature: production-readiness, Property 4: Append preserves HTML structure
/// Validates: Requirements 11.2, 11.3
/// </summary>
public class SinkPropertyTests : IDisposable
{
    private readonly string _tempDir;

    public SinkPropertyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SinkPropertyTests_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>
    /// Property 3: Thread-safe concurrent writes.
    /// For any set of LogEvent instances emitted concurrently from multiple threads
    /// to a single HtmlFileSink, every event SHALL appear exactly once in the output file,
    /// and the file SHALL not contain corrupted or interleaved partial entries.
    ///
    /// **Validates: Requirements 11.2**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(LogEventArbitraries) })]
    public bool ConcurrentWritesPreserveAllEvents(LogEvent[] events)
    {
        if (events.Length == 0)
            return true;

        var filePath = Path.Combine(_tempDir, Guid.NewGuid().ToString("N") + ".html");
        var template = new HtmlTemplate();
        var formatter = new HtmlLogEventFormatter();
        var encoding = Encoding.UTF8;

        // Format each event individually to know what to expect in the file
        var expectedEntries = new List<string>();
        foreach (var evt in events)
        {
            var sw = new StringWriter();
            formatter.Format(evt, sw);
            expectedEntries.Add(sw.ToString().TrimEnd('\n', '\r', ','));
        }

        // Write all events concurrently to a single sink
        using (var sink = new HtmlFileSink(filePath, formatter, template, null, encoding))
        {
            Parallel.ForEach(events, evt => sink.Emit(evt));
        }

        // Read the file content
        var fileContent = File.ReadAllText(filePath, encoding);

        // Verify every event's formatted output appears in the file
        foreach (var entry in expectedEntries)
        {
            if (!fileContent.Contains(entry))
                return false;
        }

        // Verify the file is not corrupted: it should contain the template header and tail
        var headerWriter = new StringWriter();
        template.WriteHeader(headerWriter);
        var headerContent = headerWriter.ToString();

        var tailWriter = new StringWriter();
        template.WriteTail(tailWriter);
        var tailContent = tailWriter.ToString();

        // The file should start with the header and end with the tail
        if (!fileContent.StartsWith(headerContent))
            return false;

        if (!fileContent.EndsWith(tailContent))
            return false;

        return true;
    }

    /// <summary>
    /// Feature: production-readiness, Property 4: Append preserves HTML structure
    ///
    /// Property 4: Append preserves HTML structure.
    /// For any sequence of LogEvent instances appended to an HtmlFileSink, the resulting
    /// file SHALL contain the template header, followed by all formatted log entries in
    /// emission order, followed by the template tail — with no content missing or reordered.
    ///
    /// **Validates: Requirements 11.3**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(LogEventArbitraries) })]
    public bool AppendPreservesHtmlStructure(LogEvent[] events)
    {
        if (events.Length == 0)
            return true;

        var filePath = Path.Combine(_tempDir, Guid.NewGuid().ToString("N") + ".html");
        var template = new HtmlTemplate();
        var formatter = new HtmlLogEventFormatter();
        var encoding = Encoding.UTF8;

        // Format each event individually to know the expected entries in order
        var expectedEntries = new List<string>();
        foreach (var evt in events)
        {
            var sw = new StringWriter();
            formatter.Format(evt, sw);
            expectedEntries.Add(sw.ToString());
        }

        // Append events sequentially (NOT concurrently — this tests ordering)
        using (var sink = new HtmlFileSink(filePath, formatter, template, null, encoding))
        {
            foreach (var evt in events)
                sink.Emit(evt);
        }

        // Read the file content
        var fileContent = File.ReadAllText(filePath, encoding);

        // Get expected header and tail
        var headerWriter = new StringWriter();
        template.WriteHeader(headerWriter);
        var headerContent = headerWriter.ToString();

        var tailWriter = new StringWriter();
        template.WriteTail(tailWriter);
        var tailContent = tailWriter.ToString();

        // Verify file starts with the template header
        if (!fileContent.StartsWith(headerContent))
            return false;

        // Verify file ends with the template tail
        if (!fileContent.EndsWith(tailContent))
            return false;

        // Extract the entries region between header and tail
        var entriesRegion = fileContent.Substring(
            headerContent.Length,
            fileContent.Length - headerContent.Length - tailContent.Length);

        // Verify all formatted entries appear in order (entry N before entry N+1)
        var searchStart = 0;
        foreach (var entry in expectedEntries)
        {
            var idx = entriesRegion.IndexOf(entry, searchStart, StringComparison.Ordinal);
            if (idx < 0)
                return false;
            searchStart = idx + entry.Length;
        }

        return true;
    }
}
