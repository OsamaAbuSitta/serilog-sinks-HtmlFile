using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Serilog;
using Serilog.Events;
using Serilog.Parsing;

namespace Serilog.Sinks.HtmlFile.Tests;

/// <summary>
/// Unit tests for the unified HtmlTemplate class.
/// Validates: Requirements 1.2, 1.3, 5.4
/// </summary>
public class HtmlTemplateTests
{
    private const string PlaceholderMarker = "<!-- LOG_ENTRIES_PLACEHOLDER -->";
    private const string ResourceName = "Serilog.Sinks.HtmlFile.DefaultTemplate.html";

    /// <summary>
    /// Verifies that new HtmlTemplate() (default, embedded resource) produces
    /// byte-identical output to the expected transformation of DefaultTemplate.html.
    ///
    /// The expected output is:
    ///   WriteHeader = [content before marker] + "var logEntries = [\n"
    ///   WriteTail   = "// __LOG_ENTRIES_END__\n" + "];\n" + [content after marker]
    ///
    /// Validates: Requirements 1.2, 5.4
    /// </summary>
    [Fact]
    public void DefaultTemplate_WriteHeaderAndWriteTail_AreByteIdenticalToExpectedTransformation()
    {
        // Read the raw embedded resource to compute expected output
        var assembly = typeof(HtmlTemplate).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var rawContent = reader.ReadToEnd();

        var markerIndex = rawContent.IndexOf(PlaceholderMarker, StringComparison.Ordinal);
        Assert.True(markerIndex >= 0, "Embedded resource must contain the placeholder marker");

        var expectedHeaderContent = rawContent.Substring(0, markerIndex);
        var expectedTailContent = rawContent.Substring(markerIndex + PlaceholderMarker.Length);

        var expectedHeader = expectedHeaderContent + "var logEntries = [\n";
        var expectedTail = "// __LOG_ENTRIES_END__\n" + "];\n" + expectedTailContent;

        // Construct default HtmlTemplate (loads from embedded resource)
        var template = new HtmlTemplate();

        var headerWriter = new StringWriter();
        template.WriteHeader(headerWriter);
        var actualHeader = headerWriter.ToString();

        var tailWriter = new StringWriter();
        template.WriteTail(tailWriter);
        var actualTail = tailWriter.ToString();

        // Character-identical comparison
        Assert.Equal(expectedHeader, actualHeader);
        Assert.Equal(expectedTail, actualTail);
    }

    /// <summary>
    /// Verifies that the combined WriteHeader + WriteTail output forms a complete
    /// HTML document with the expected structural elements.
    ///
    /// Validates: Requirements 1.2, 5.4
    /// </summary>
    [Fact]
    public void DefaultTemplate_CombinedOutput_ContainsExpectedStructuralElements()
    {
        var template = new HtmlTemplate();

        var headerWriter = new StringWriter();
        template.WriteHeader(headerWriter);
        var header = headerWriter.ToString();

        var tailWriter = new StringWriter();
        template.WriteTail(tailWriter);
        var tail = tailWriter.ToString();

        var combined = header + tail;

        // Must be a complete HTML document
        Assert.StartsWith("<!DOCTYPE html>", combined);
        Assert.EndsWith("</html>\n", combined);

        // Header structural elements
        Assert.Contains("<head>", header);
        Assert.Contains("<style>", header);
        Assert.Contains("<div class=\"toolbar\">", header);
        Assert.Contains("<div id=\"log-container\"></div>", header);
        Assert.Contains("<script>", header);
        Assert.True(header.EndsWith("var logEntries = [\n"),
            "WriteHeader must end with 'var logEntries = [\\n'");

        // Tail structural elements
        Assert.True(tail.StartsWith("// __LOG_ENTRIES_END__\n"),
            "WriteTail must start with '// __LOG_ENTRIES_END__\\n'");
        Assert.Contains("];\n", tail);
        Assert.Contains("</script>", tail);
        Assert.Contains("</body>", tail);
        Assert.Contains("</html>", tail);
    }

    /// <summary>
    /// Verifies that the embedded resource DefaultTemplate.html contains exactly
    /// one occurrence of the placeholder marker.
    ///
    /// Validates: Requirements 1.3
    /// </summary>
    [Fact]
    public void EmbeddedResource_ContainsExactlyOnePlaceholderMarker()
    {
        var assembly = typeof(HtmlTemplate).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName);
        Assert.NotNull(stream);

        using var reader = new StreamReader(stream, Encoding.UTF8);
        var content = reader.ReadToEnd();

        var count = 0;
        var searchFrom = 0;
        while (true)
        {
            var index = content.IndexOf(PlaceholderMarker, searchFrom, StringComparison.Ordinal);
            if (index < 0) break;
            count++;
            searchFrom = index + PlaceholderMarker.Length;
        }

        Assert.Equal(1, count);
    }

    /// <summary>
    /// Verifies that the embedded resource DefaultTemplate.html is accessible
    /// via Assembly.GetManifestResourceStream and returns a non-null stream.
    ///
    /// Validates: Requirements 1.1, 1.5
    /// </summary>
    [Fact]
    public void EmbeddedResource_IsAccessibleViaManifestResourceStream()
    {
        var assembly = typeof(HtmlTemplate).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName);

        Assert.NotNull(stream);
    }

    /// <summary>
    /// Verifies that the InsertionMarker property returns the expected
    /// JavaScript comment used by the sink to locate the append offset.
    ///
    /// Validates: Requirements 5.3
    /// </summary>
    [Fact]
    public void InsertionMarker_ReturnsExpectedValue()
    {
        var template = new HtmlTemplate();

        Assert.Equal("// __LOG_ENTRIES_END__", template.InsertionMarker);
    }

    /// <summary>
    /// Verifies that configuring the sink via WriteTo.HtmlFile(path) with no
    /// customTemplatePath (defaults to null) uses the embedded default template.
    /// Emits an event and checks the output file for default template structural elements.
    ///
    /// Validates: Requirements 4.1
    /// </summary>
    [Fact]
    public void DefaultTemplate_ViaExtensionMethod_CreatesFileWithDefaultTemplateContent()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "HtmlTemplateTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var logPath = Path.Combine(tempDir, "test.html");

            using (var logger = new LoggerConfiguration()
                       .WriteTo.HtmlFile(logPath)
                       .CreateLogger())
            {
                logger.Information("ExtensionMethodTestEvent");
            }

            Assert.True(File.Exists(logPath), "Log file should be created");

            var content = File.ReadAllText(logPath);

            // Default template structural elements
            Assert.Contains("<!DOCTYPE html>", content);
            Assert.Contains("<style>", content);
            Assert.Contains("<div class=\"toolbar\">", content);
            Assert.Contains("<div id=\"log-container\"></div>", content);
            Assert.Contains("var logEntries = [", content);

            // The emitted event message
            Assert.Contains("ExtensionMethodTestEvent", content);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    /// <summary>
    /// Verifies that configuring the sink via WriteTo.HtmlFile(path, customTemplatePath: ...)
    /// uses the custom template file content instead of the embedded default.
    /// Emits an event and checks the output file for custom template content and the event.
    ///
    /// Validates: Requirements 4.2
    /// </summary>
    [Fact]
    public void CustomTemplate_ViaExtensionMethod_CreatesFileWithCustomTemplateContent()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "HtmlTemplateTest_Custom_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var templatePath = Path.Combine(tempDir, "custom-template.html");
            var logPath = Path.Combine(tempDir, "test.html");

            // Create a custom template with distinctive content and the required placeholder
            File.WriteAllText(templatePath,
                "<html><body><h1>Custom Log</h1><script>" +
                "<!-- LOG_ENTRIES_PLACEHOLDER -->" +
                "</script></body></html>");

            using (var logger = new LoggerConfiguration()
                       .WriteTo.HtmlFile(logPath, customTemplatePath: templatePath)
                       .CreateLogger())
            {
                logger.Information("CustomTemplateExtensionEvent");
            }

            Assert.True(File.Exists(logPath), "Log file should be created");

            var content = File.ReadAllText(logPath);

            // Custom template content must be present
            Assert.Contains("<h1>Custom Log</h1>", content);

            // Framing injected by HtmlTemplate
            Assert.Contains("var logEntries = [", content);
            Assert.Contains("// __LOG_ENTRIES_END__", content);

            // The emitted event message
            Assert.Contains("CustomTemplateExtensionEvent", content);

            // Must NOT contain default template elements (proves custom template was used)
            Assert.DoesNotContain("<div class=\"toolbar\">", content);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    /// <summary>
    /// Verifies backward compatibility: a log file created with HtmlTemplate can be
    /// reopened by a new HtmlFileSink with HtmlTemplate, which locates the insertion
    /// marker and resumes appending correctly.
    ///
    /// Validates: Requirements 5.5
    /// </summary>
    [Fact]
    public void BackwardCompatibility_ReopenExistingLogFile_AppendsCorrectly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "HtmlTemplateTest_Compat_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var logPath = Path.Combine(tempDir, "compat.html");
            var formatter = new HtmlLogEventFormatter();
            var encoding = new UTF8Encoding(false);

            // First session: create the file and emit "first event"
            using (var sink = new HtmlFileSink(logPath, formatter, new HtmlTemplate(), null, encoding))
            {
                sink.Emit(CreateEvent("first event"));
            }

            // Second session: reopen the same file and emit "second event"
            using (var sink = new HtmlFileSink(logPath, formatter, new HtmlTemplate(), null, encoding))
            {
                sink.Emit(CreateEvent("second event"));
            }

            var content = File.ReadAllText(logPath);

            // Both events must be present
            Assert.Contains("first event", content);
            Assert.Contains("second event", content);

            // "first event" must appear before "second event"
            var idxFirst = content.IndexOf("first event", StringComparison.Ordinal);
            var idxSecond = content.IndexOf("second event", StringComparison.Ordinal);
            Assert.True(idxFirst >= 0, "\"first event\" not found in file");
            Assert.True(idxSecond >= 0, "\"second event\" not found in file");
            Assert.True(idxFirst < idxSecond, "\"first event\" must appear before \"second event\"");

            // File must retain valid structure
            Assert.Contains("var logEntries = [", content);
            Assert.Contains("// __LOG_ENTRIES_END__", content);
            Assert.Contains("</html>", content);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    private static LogEvent CreateEvent(string message)
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
}
