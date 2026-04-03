using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Serilog.Events;
using Serilog.Parsing;

namespace Serilog.Sinks.HtmlFile.Tests;

/// <summary>
/// End-to-end tests for HtmlTemplate loaded from an .html file.
/// Validates: Requirements 12.1, 12.2, 12.3
/// </summary>
public class CustomTemplateEndToEndTests : IDisposable
{
    private readonly string _tempDir;
    private readonly HtmlLogEventFormatter _formatter = new();
    private readonly Encoding _encoding = new UTF8Encoding(false);

    public CustomTemplateEndToEndTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CustomTemplateE2E_" + Guid.NewGuid().ToString("N"));
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

    private static LogEvent CreateEvent(string message, LogEventLevel level = LogEventLevel.Information)
    {
        var tokens = new List<MessageTemplateToken> { new TextToken(message) };
        var template = new MessageTemplate(tokens);
        return new LogEvent(
            DateTimeOffset.UtcNow,
            level,
            null,
            template,
            Array.Empty<LogEventProperty>());
    }

    private string CreateTemplateFile(string header, string tail)
    {
        var templatePath = Path.Combine(_tempDir, "custom-template.html");
        File.WriteAllText(templatePath, header + "<!-- LOG_ENTRIES_PLACEHOLDER -->" + tail);
        return templatePath;
    }

    [Fact]
    public void EmittedEvents_AppearInOutputFile()
    {
        // Arrange
        var templatePath = CreateTemplateFile(
            "<html><body><h1>Custom Log</h1>",
            "</body></html>");
        var customTemplate = new HtmlTemplate(templatePath);
        var outputPath = Path.Combine(_tempDir, "output.html");

        // Act
        using (var sink = new HtmlFileSink(outputPath, _formatter, customTemplate, null, _encoding))
        {
            sink.Emit(CreateEvent("First event"));
            sink.Emit(CreateEvent("Second event"));
            sink.Emit(CreateEvent("Third event"));
        }

        // Assert
        var content = File.ReadAllText(outputPath);
        Assert.Contains("First event", content);
        Assert.Contains("Second event", content);
        Assert.Contains("Third event", content);
    }

    [Fact]
    public void CustomHeader_AppearsBefore_LogEntries()
    {
        // Arrange
        var templatePath = CreateTemplateFile(
            "<html><body><h1>Custom Log</h1>",
            "</body></html>");
        var customTemplate = new HtmlTemplate(templatePath);
        var outputPath = Path.Combine(_tempDir, "header-test.html");

        // Act
        using (var sink = new HtmlFileSink(outputPath, _formatter, customTemplate, null, _encoding))
        {
            sink.Emit(CreateEvent("header check event"));
        }

        // Assert
        var content = File.ReadAllText(outputPath);
        var headerIdx = content.IndexOf("<h1>Custom Log</h1>", StringComparison.Ordinal);
        var eventIdx = content.IndexOf("header check event", StringComparison.Ordinal);

        Assert.True(headerIdx >= 0, "Custom header content not found in output");
        Assert.True(eventIdx >= 0, "Log event not found in output");
        Assert.True(headerIdx < eventIdx, "Custom header must appear before log entries");
    }

    [Fact]
    public void CustomTail_AppearsAfter_LogEntries()
    {
        // Arrange
        var templatePath = CreateTemplateFile(
            "<html><body><h1>Custom Log</h1>",
            "</body></html>");
        var customTemplate = new HtmlTemplate(templatePath);
        var outputPath = Path.Combine(_tempDir, "tail-test.html");

        // Act
        using (var sink = new HtmlFileSink(outputPath, _formatter, customTemplate, null, _encoding))
        {
            sink.Emit(CreateEvent("tail check event"));
        }

        // Assert
        var content = File.ReadAllText(outputPath);
        var eventIdx = content.IndexOf("tail check event", StringComparison.Ordinal);
        var tailIdx = content.IndexOf("</body></html>", StringComparison.Ordinal);

        Assert.True(eventIdx >= 0, "Log event not found in output");
        Assert.True(tailIdx >= 0, "Custom tail content not found in output");
        Assert.True(eventIdx < tailIdx, "Custom tail must appear after log entries");
    }

    [Fact]
    public void CustomHeaderAndTail_SurroundEntries()
    {
        // Arrange
        var templatePath = CreateTemplateFile(
            "<html><body><h1>Custom Log</h1>",
            "</body></html>");
        var customTemplate = new HtmlTemplate(templatePath);
        var outputPath = Path.Combine(_tempDir, "surround-test.html");

        // Act
        using (var sink = new HtmlFileSink(outputPath, _formatter, customTemplate, null, _encoding))
        {
            sink.Emit(CreateEvent("event alpha"));
            sink.Emit(CreateEvent("event beta", LogEventLevel.Warning));
        }

        // Assert
        var content = File.ReadAllText(outputPath);

        var headerIdx = content.IndexOf("<h1>Custom Log</h1>", StringComparison.Ordinal);
        var alphaIdx = content.IndexOf("event alpha", StringComparison.Ordinal);
        var betaIdx = content.IndexOf("event beta", StringComparison.Ordinal);
        var tailIdx = content.IndexOf("</body></html>", StringComparison.Ordinal);

        Assert.True(headerIdx >= 0, "Custom header not found");
        Assert.True(alphaIdx >= 0, "First event not found");
        Assert.True(betaIdx >= 0, "Second event not found");
        Assert.True(tailIdx >= 0, "Custom tail not found");

        Assert.True(headerIdx < alphaIdx, "Header must precede first event");
        Assert.True(alphaIdx < betaIdx, "Events must appear in emission order");
        Assert.True(betaIdx < tailIdx, "Tail must follow last event");
    }
}
