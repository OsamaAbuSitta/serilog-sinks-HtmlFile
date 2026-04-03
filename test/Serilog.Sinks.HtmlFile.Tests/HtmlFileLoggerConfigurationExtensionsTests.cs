using System;
using System.IO;
using System.Text;
using Serilog.Events;

namespace Serilog.Sinks.HtmlFile.Tests;

/// <summary>
/// Tests for the updated HtmlFileLoggerConfigurationExtensions.HtmlFile() method,
/// verifying new parameters are accepted, defaults are backward-compatible,
/// and fileNamingPattern is evaluated correctly.
/// Requirements: 7.1, 7.7, 11.1, 11.5, 11.9
/// </summary>
public class HtmlFileLoggerConfigurationExtensionsTests : IDisposable
{
    private readonly string _tempDir;

    public HtmlFileLoggerConfigurationExtensionsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ConfigExtTests_" + Guid.NewGuid().ToString("N"));
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

    // Validates: Requirements 7.1, 11.1
    [Fact]
    public void NewParameters_AreAccepted_AndDoNotThrow()
    {
        var path = Path.Combine(_tempDir, "accepted.html");

        using var logger = new LoggerConfiguration()
            .WriteTo.HtmlFile(
                path,
                archiveNamingPattern: "{BaseName}-{Timestamp}{Extension}",
                archiveTimestampFormat: "yyyy-MM-dd",
                fileNamingPattern: null,
                dateFormat: "yyyyMMdd")
            .CreateLogger();

        logger.Information("Test message");

        Assert.True(File.Exists(path));
    }

    // Validates: Requirements 7.7, 11.9
    [Fact]
    public void DefaultValues_ProduceBackwardCompatibleBehavior()
    {
        var path = Path.Combine(_tempDir, "defaults.html");

        // Call with no new parameters — all default to null
        using var logger = new LoggerConfiguration()
            .WriteTo.HtmlFile(path)
            .CreateLogger();

        logger.Information("Backward compatible");

        // File should be created at the exact path specified
        Assert.True(File.Exists(path), $"Expected file at {path}");
        var content = File.ReadAllText(path);
        Assert.Contains("Backward compatible", content);
    }

    // Validates: Requirements 11.1, 11.5
    [Fact]
    public void FileNamingPattern_WithDatePlaceholder_CreatesFileAtEvaluatedPath()
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var pattern = Path.Combine(_tempDir, "app-{Date}.html");

        using var logger = new LoggerConfiguration()
            .WriteTo.HtmlFile(
                path: pattern,
                fileNamingPattern: pattern)
            .CreateLogger();

        logger.Information("Date pattern test");

        var expectedPath = Path.Combine(_tempDir, $"app-{today}.html");
        Assert.True(File.Exists(expectedPath), $"Expected file at {expectedPath}");
    }

    // Validates: Requirements 11.5
    [Fact]
    public void PlainPath_WithoutFileNamingPattern_CreatesFileAtExactPath()
    {
        var path = Path.Combine(_tempDir, "exact-path.html");

        using var logger = new LoggerConfiguration()
            .WriteTo.HtmlFile(path)
            .CreateLogger();

        logger.Information("Plain path test");

        Assert.True(File.Exists(path), $"Expected file at exact path {path}");
        // Ensure no other files were created (no pattern evaluation happened)
        var files = Directory.GetFiles(_tempDir);
        Assert.Single(files);
        Assert.Equal("exact-path.html", Path.GetFileName(files[0]));
    }

    // Validates: Requirements 7.1
    [Fact]
    public void ArchiveNamingParameters_AreForwardedToSink()
    {
        var path = Path.Combine(_tempDir, "archive-fwd.html");

        // Custom archive pattern — should not throw and should create a valid logger
        using var logger = new LoggerConfiguration()
            .WriteTo.HtmlFile(
                path,
                archiveNamingPattern: "{BaseName}_archived_{Timestamp}{Extension}",
                archiveTimestampFormat: "yyyyMMdd")
            .CreateLogger();

        logger.Information("Archive forwarding test");

        Assert.True(File.Exists(path));
    }

    // Validates: Requirements 11.1
    [Fact]
    public void FileNamingPattern_WithCustomDateFormat_UsesSpecifiedFormat()
    {
        var today = DateTime.UtcNow.ToString("yyyyMMdd");
        var pattern = Path.Combine(_tempDir, "log-{Date}.html");

        using var logger = new LoggerConfiguration()
            .WriteTo.HtmlFile(
                path: pattern,
                fileNamingPattern: pattern,
                dateFormat: "yyyyMMdd")
            .CreateLogger();

        logger.Information("Custom date format test");

        var expectedPath = Path.Combine(_tempDir, $"log-{today}.html");
        Assert.True(File.Exists(expectedPath), $"Expected file at {expectedPath}");
    }

    // Validates: Requirements 7.1, 11.1
    [Fact]
    public void AllNewParameters_CanBeCombined()
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var pattern = Path.Combine(_tempDir, "combined-{Date}.html");

        using var logger = new LoggerConfiguration()
            .WriteTo.HtmlFile(
                path: pattern,
                archiveNamingPattern: "{BaseName}-old-{Timestamp}{Extension}",
                archiveTimestampFormat: "yyyy-MM-dd_HHmmss",
                fileNamingPattern: pattern,
                dateFormat: "yyyy-MM-dd")
            .CreateLogger();

        logger.Information("Combined parameters test");

        var expectedPath = Path.Combine(_tempDir, $"combined-{today}.html");
        Assert.True(File.Exists(expectedPath), $"Expected file at {expectedPath}");
    }
}
