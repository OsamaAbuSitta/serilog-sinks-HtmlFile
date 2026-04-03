using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Serilog.Events;
using Serilog.Parsing;

namespace Serilog.Sinks.HtmlFile.Tests;

public class RollingHtmlFileSinkTests : IDisposable
{
    private readonly string _tempDir;
    private readonly HtmlLogEventFormatter _formatter = new();
    private readonly HtmlTemplate _template = new();
    private readonly Encoding _encoding = new UTF8Encoding(false);

    public RollingHtmlFileSinkTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "RollingHtmlFileSinkTests_" + Guid.NewGuid().ToString("N"));
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

    private static string PadMessage(int approximateBytes)
    {
        return new string('X', approximateBytes);
    }

    // Validates: Requirements 7.2
    [Fact]
    public void DefaultConstruction_ArchiveUsesDefaultPattern()
    {
        var path = Path.Combine(_tempDir, "app.html");

        using (var sink = new RollingHtmlFileSink(path, _formatter, _template, 500, _encoding))
        {
            // Write enough data to trigger rolling
            var bigEvent = CreateEvent(PadMessage(600));
            sink.Emit(bigEvent);
            // First emit fills the file past 500 bytes, next emit triggers roll
            sink.Emit(CreateEvent("after roll"));
        }

        var files = Directory.GetFiles(_tempDir);
        var archiveFile = Array.Find(files, f => !Path.GetFileName(f).Equals("app.html", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(archiveFile);
        var archiveName = Path.GetFileName(archiveFile);
        // Default pattern: {BaseName}_{yyyyMMddHHmmss}{Extension} => app_YYYYMMDDHHMMSS.html
        Assert.Matches(@"^app_\d{14}\.html$", archiveName);
    }

    // Validates: Requirements 7.3
    [Fact]
    public void CustomArchivePattern_IsUsedDuringRolling()
    {
        var path = Path.Combine(_tempDir, "app.html");

        using (var sink = new RollingHtmlFileSink(
            path, _formatter, _template, 500, _encoding,
            archiveNamingPattern: "{BaseName}-archive-{Timestamp}{Extension}",
            archiveTimestampFormat: "yyyy-MM-dd"))
        {
            var bigEvent = CreateEvent(PadMessage(600));
            sink.Emit(bigEvent);
            sink.Emit(CreateEvent("after roll"));
        }

        var files = Directory.GetFiles(_tempDir);
        var archiveFile = Array.Find(files, f => !Path.GetFileName(f).Equals("app.html", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(archiveFile);
        var archiveName = Path.GetFileName(archiveFile);
        // Custom pattern: {BaseName}-archive-{Timestamp}{Extension} => app-archive-YYYY-MM-DD.html
        Assert.Matches(@"^app-archive-\d{4}-\d{2}-\d{2}\.html$", archiveName);
    }

    // Validates: Requirements 8.1
    [Fact]
    public void Constructor_InvalidArchivePattern_ThrowsArgumentException()
    {
        var path = Path.Combine(_tempDir, "app.html");

        var ex = Assert.Throws<ArgumentException>(() =>
            new RollingHtmlFileSink(path, _formatter, _template, 500, _encoding,
                archiveNamingPattern: "{Timestamp}{Extension}"));

        Assert.Contains("{BaseName}", ex.Message);
    }

    // Validates: Requirements 8.1
    [Fact]
    public void Constructor_InvalidTimestampFormat_ThrowsFormatException()
    {
        var path = Path.Combine(_tempDir, "app.html");

        Assert.Throws<FormatException>(() =>
            new RollingHtmlFileSink(path, _formatter, _template, 500, _encoding,
                archiveTimestampFormat: "%"));
    }

    // Validates: Requirements 11.2
    [Fact]
    public void FileNamingPattern_EvaluatedAtConstruction_ProducesActiveFilePath()
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var pattern = Path.Combine(_tempDir, "app-{Date}.html");

        using (var sink = new RollingHtmlFileSink(pattern, _formatter, _template, 10_000_000, _encoding,
            fileNamingPattern: pattern))
        {
            sink.Emit(CreateEvent("test"));
        }

        var expectedPath = Path.Combine(_tempDir, $"app-{today}.html");
        Assert.True(File.Exists(expectedPath), $"Expected file at {expectedPath}");
    }

    // Validates: Requirements 11.6
    [Fact]
    public void FileNamingPattern_ReEvaluatedOnRoll_ProducesFreshActiveFilePath()
    {
        // Use a pattern with {MachineName} so we can verify re-evaluation happens
        var pattern = Path.Combine(_tempDir, "{MachineName}-{Date}.html");
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var machineName = Environment.MachineName;
        var expectedFileName = $"{machineName}-{today}.html";

        using (var sink = new RollingHtmlFileSink(pattern, _formatter, _template, 500, _encoding,
            fileNamingPattern: pattern))
        {
            // Write enough to trigger rolling
            var bigEvent = CreateEvent(PadMessage(600));
            sink.Emit(bigEvent);
            sink.Emit(CreateEvent("after roll"));
        }

        // After rolling, the new active file should exist with the evaluated name
        var expectedPath = Path.Combine(_tempDir, expectedFileName);
        Assert.True(File.Exists(expectedPath), $"Expected re-evaluated file at {expectedPath}");
    }

    // Validates: Requirements 11.5
    [Fact]
    public void PlainPath_WithoutFileNamingPattern_WorksIdenticallyToOriginal()
    {
        var path = Path.Combine(_tempDir, "plain.html");

        using (var sink = new RollingHtmlFileSink(path, _formatter, _template, 10_000_000, _encoding))
        {
            sink.Emit(CreateEvent("test message"));
        }

        Assert.True(File.Exists(path), $"Expected file at {path}");
        var content = File.ReadAllText(path);
        Assert.Contains("test message", content);
    }
}
