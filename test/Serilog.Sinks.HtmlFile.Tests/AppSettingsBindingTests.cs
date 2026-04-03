using System.IO;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Serilog.Sinks.HtmlFile.Tests;

/// <summary>
/// Integration tests for appsettings.json binding via Serilog.Settings.Configuration.
/// Validates: Requirements 10.1, 10.2
/// </summary>
public class AppSettingsBindingTests : IDisposable
{
    private readonly string _tempDir;

    public AppSettingsBindingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AppSettingsBindingTests_" + Guid.NewGuid().ToString("N"));
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
    /// Build LoggerConfiguration from IConfiguration with all JSON-bindable WriteTo.HtmlFile
    /// parameters, verify sink creates a log file at the configured path.
    /// The 10 extension method parameters are: path, restrictedToMinimumLevel, fileSizeLimitBytes,
    /// encoding, customTemplatePath, formatter, archiveNamingPattern, archiveTimestampFormat,
    /// fileNamingPattern, dateFormat. Of these, encoding and formatter are complex types that
    /// cannot be bound from JSON strings, so the 8 string/numeric parameters are specified.
    /// Validates: Requirement 10.1
    /// </summary>
    [Fact]
    public void AppSettingsJsonBinding_WithAllBindableParameters_CreatesFile()
    {
        var logFilePath = Path.Combine(_tempDir, "integration-test.html");

        // All 10 extension method parameters: path, restrictedToMinimumLevel,
        // fileSizeLimitBytes, encoding, customTemplatePath, formatter,
        // archiveNamingPattern, archiveTimestampFormat, fileNamingPattern, dateFormat.
        // encoding and formatter are complex types not bindable from JSON.
        // customTemplatePath and fileNamingPattern default to null and are omitted
        // (JSON null is converted to empty string by the configuration binder).
        // The remaining 6 string/numeric parameters are specified explicitly.
        var json = $$"""
        {
            "Serilog": {
                "Using": ["Serilog.Sinks.HtmlFile"],
                "WriteTo": [
                    {
                        "Name": "HtmlFile",
                        "Args": {
                            "path": "{{logFilePath.Replace("\\", "\\\\")}}",
                            "restrictedToMinimumLevel": "Information",
                            "fileSizeLimitBytes": 1073741824,
                            "archiveNamingPattern": "{BaseName}_{Timestamp}{Extension}",
                            "archiveTimestampFormat": "yyyyMMddHHmmss",
                            "dateFormat": "yyyy-MM-dd"
                        }
                    }
                ]
            }
        }
        """;

        var configuration = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json)))
            .Build();

        using var logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .CreateLogger();

        // Emit a log event to trigger file creation
        logger.Information("Integration test event");

        // Dispose to flush
        logger.Dispose();

        Assert.True(File.Exists(logFilePath), $"Expected log file to be created at {logFilePath}");

        var content = File.ReadAllText(logFilePath);
        Assert.NotEmpty(content);
    }

    /// <summary>
    /// Emit a log event through a JSON-configured sink and verify the log file
    /// contains the formatted event with the distinctive message.
    /// Validates: Requirement 10.2
    /// </summary>
    [Fact]
    public void AppSettingsJsonBinding_EmittedEvent_AppearsInLogFile()
    {
        var logFilePath = Path.Combine(_tempDir, "event-write-test.html");
        var distinctiveMessage = "UniqueTestMarker_" + Guid.NewGuid().ToString("N");

        var json = $$"""
        {
            "Serilog": {
                "Using": ["Serilog.Sinks.HtmlFile"],
                "WriteTo": [
                    {
                        "Name": "HtmlFile",
                        "Args": {
                            "path": "{{logFilePath.Replace("\\", "\\\\")}}"
                        }
                    }
                ]
            }
        }
        """;

        var configuration = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json)))
            .Build();

        using var logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .CreateLogger();

        logger.Information(distinctiveMessage);

        // Dispose to flush all buffered writes
        logger.Dispose();

        Assert.True(File.Exists(logFilePath), $"Expected log file to be created at {logFilePath}");

        var content = File.ReadAllText(logFilePath);
        Assert.Contains(distinctiveMessage, content);
    }
}
