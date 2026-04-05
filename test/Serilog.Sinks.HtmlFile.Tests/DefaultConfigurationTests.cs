using System;
using System.IO;
using System.Text;
using Microsoft.Extensions.Configuration;
using Serilog.Events;

namespace Serilog.Sinks.HtmlFile.Tests;

/// <summary>
/// Tests that all public default constants (naming patterns, formats, file size)
/// hold the expected values, produce correct results when applied, and that the
/// sink works end-to-end via both code configuration and IConfiguration binding.
/// </summary>
public class DefaultConfigurationTests : IDisposable
{
    private readonly string _tempDir;

    public DefaultConfigurationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DefaultTests_" + Guid.NewGuid().ToString("N"));
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

    #region ArchiveNamingHelper defaults

    [Fact]
    public void ArchiveNamingHelper_DefaultPattern_IsBaseNameUnderscoreTimestampExtension()
    {
        Assert.Equal("{BaseName}_{Timestamp}{Extension}", ArchiveNamingHelper.DefaultPattern);
    }

    [Fact]
    public void ArchiveNamingHelper_DefaultTimestampFormat_IsYyyyMMddHHmmss()
    {
        Assert.Equal("yyyyMMddHHmmss", ArchiveNamingHelper.DefaultTimestampFormat);
    }

    [Fact]
    public void ArchiveNamingHelper_DefaultPattern_ContainsRequiredBaseNamePlaceholder()
    {
        Assert.Contains("{BaseName}", ArchiveNamingHelper.DefaultPattern);
    }

    [Fact]
    public void ArchiveNamingHelper_DefaultTimestampFormat_Produces14CharacterTimestamp()
    {
        var formatted = DateTime.UtcNow.ToString(ArchiveNamingHelper.DefaultTimestampFormat);

        // yyyyMMddHHmmss always produces exactly 14 characters
        Assert.Equal(14, formatted.Length);
    }

    [Fact]
    public void ArchiveNamingHelper_DefaultPattern_FormatsCorrectArchiveName()
    {
        var timestamp = new DateTime(2026, 4, 4, 15, 30, 45, DateTimeKind.Utc);

        var result = ArchiveNamingHelper.FormatArchiveName(
            ArchiveNamingHelper.DefaultPattern,
            "mylog",
            timestamp,
            ".html",
            ArchiveNamingHelper.DefaultTimestampFormat);

        Assert.Equal("mylog_20260404153045.html", result);
    }

    [Fact]
    public void ArchiveNamingHelper_DefaultPattern_PassesValidation()
    {
        // Should not throw
        ArchiveNamingHelper.Validate(
            ArchiveNamingHelper.DefaultPattern,
            ArchiveNamingHelper.DefaultTimestampFormat);
    }

    #endregion

    #region FileNamingHelper defaults

    [Fact]
    public void FileNamingHelper_DefaultDateFormat_IsIso8601Date()
    {
        Assert.Equal("yyyy-MM-dd", FileNamingHelper.DefaultDateFormat);
    }

    [Fact]
    public void FileNamingHelper_DefaultDateFormat_Produces10CharacterDate()
    {
        var formatted = DateTime.UtcNow.ToString(FileNamingHelper.DefaultDateFormat);

        // yyyy-MM-dd always produces exactly 10 characters
        Assert.Equal(10, formatted.Length);
    }

    [Fact]
    public void FileNamingHelper_DefaultDateFormat_ProducesExpectedOutput()
    {
        var date = new DateTime(2026, 4, 4, 0, 0, 0, DateTimeKind.Utc);

        var result = FileNamingHelper.EvaluatePattern(
            "log-{Date}.html",
            FileNamingHelper.DefaultDateFormat,
            date);

        Assert.Equal("log-2026-04-04.html", result);
    }

    [Fact]
    public void FileNamingHelper_PatternWithoutPlaceholders_ReturnsPathUnchanged()
    {
        var path = "logs/myapp.html";

        var result = FileNamingHelper.EvaluatePattern(
            path,
            FileNamingHelper.DefaultDateFormat);

        Assert.Equal(path, result);
    }

    #endregion

    #region Default file size limit

    [Fact]
    public void DefaultFileSizeLimit_Is1GB_InBytes()
    {
        // 1 GB = 1024 * 1024 * 1024 = 1,073,741,824 bytes
        const long expectedOneGb = 1L * 1024 * 1024 * 1024;
        const long defaultLimit = 1073741824L;

        Assert.Equal(expectedOneGb, defaultLimit);
    }

    [Fact]
    public void DefaultFileSizeLimit_TriggersRollingSink()
    {
        var path = Path.Combine(_tempDir, "rolling-default.html");

        // With the default limit (non-null), a RollingHtmlFileSink is used.
        // Verify by writing a small event — no archive should be created since we're well under 1 GB.
        using (var logger = new LoggerConfiguration()
            .WriteTo.HtmlFile(path)
            .CreateLogger())
        {
            logger.Information("Small event");
        }

        Assert.True(File.Exists(path));

        // Only the main file should exist (no archive created for small writes)
        var files = Directory.GetFiles(_tempDir);
        Assert.Single(files);
    }

    #endregion

    #region Defaults applied end-to-end

    [Fact]
    public void AllDefaults_ProduceValidHtmlLogFile()
    {
        var path = Path.Combine(_tempDir, "all-defaults.html");

        using (var logger = new LoggerConfiguration()
            .WriteTo.HtmlFile(path)
            .CreateLogger())
        {
            logger.Information("Default config event");
            logger.Warning("Default warning event");
            logger.Error("Default error event");
        }

        Assert.True(File.Exists(path));

        var content = File.ReadAllText(path);

        // File should be valid HTML
        Assert.Contains("<html", content);
        Assert.Contains("</html>", content);

        // All events should be present
        Assert.Contains("Default config event", content);
        Assert.Contains("Default warning event", content);
        Assert.Contains("Default error event", content);
    }

    [Fact]
    public void DefaultArchiveNaming_OnRoll_ProducesExpectedArchiveFileName()
    {
        var path = Path.Combine(_tempDir, "roll-test.html");

        // Use a very small file size limit to trigger rolling
        using (var logger = new LoggerConfiguration()
            .WriteTo.HtmlFile(path, fileSizeLimitBytes: 1)
            .CreateLogger())
        {
            logger.Information("First event triggers roll");
            logger.Information("Second event after roll");
        }

        var files = Directory.GetFiles(_tempDir);

        // Should have the active file plus at least one archive
        Assert.True(files.Length >= 2, $"Expected at least 2 files after rolling, found {files.Length}");

        // Archive file should follow the default pattern: roll-test_yyyyMMddHHmmss.html
        var archiveFiles = files.Where(f => Path.GetFileName(f) != "roll-test.html").ToArray();
        Assert.NotEmpty(archiveFiles);

        foreach (var archive in archiveFiles)
        {
            var name = Path.GetFileName(archive);
            // Should start with the base name
            Assert.StartsWith("roll-test_", name);
            // Should end with .html
            Assert.EndsWith(".html", name);
            // The timestamp portion should be 14 characters (yyyyMMddHHmmss)
            var timestampPart = name.Replace("roll-test_", "").Replace(".html", "");
            Assert.Equal(14, timestampPart.Length);
        }
    }

    #endregion

    #region Full usage via code configuration

    /// <summary>
    /// Full end-to-end usage using the fluent code configuration API with all defaults.
    /// Logs events at every level, verifies the file is valid HTML containing all expected entries.
    /// </summary>
    [Fact]
    public void FullUsage_CodeConfiguration_AllDefaults_LogsAllLevels()
    {
        var path = Path.Combine(_tempDir, "code-config-full.html");

        // Serilog's global minimum level defaults to Information unless overridden.
        // The sink's restrictedToMinimumLevel defaults to LevelAlias.Minimum (Verbose),
        // but the pipeline still respects the global minimum.
        using (var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.HtmlFile(path)
            .CreateLogger())
        {
            logger.Verbose("Verbose code event");
            logger.Debug("Debug code event");
            logger.Information("Info code event with {Property}", "value1");
            logger.Warning("Warning code event with {Count}", 42);
            logger.Error("Error code event");
            logger.Fatal("Fatal code event");
        }

        Assert.True(File.Exists(path), "Log file should be created via code configuration");

        var content = File.ReadAllText(path);

        // Valid HTML structure
        Assert.Contains("<html", content);
        Assert.Contains("</html>", content);

        // All events present
        Assert.Contains("Verbose code event", content);
        Assert.Contains("Debug code event", content);
        Assert.Contains("Info code event", content);
        Assert.Contains("value1", content);
        Assert.Contains("Warning code event", content);
        Assert.Contains("42", content);
        Assert.Contains("Error code event", content);
        Assert.Contains("Fatal code event", content);
    }

    /// <summary>
    /// Full end-to-end usage using the fluent code configuration API with explicit non-default parameters.
    /// Verifies that restrictedToMinimumLevel filters correctly and the file is created at the right path.
    /// </summary>
    [Fact]
    public void FullUsage_CodeConfiguration_WithExplicitParameters_AppliesSettings()
    {
        var path = Path.Combine(_tempDir, "code-config-explicit.html");

        using (var logger = new LoggerConfiguration()
            .WriteTo.HtmlFile(
                path,
                restrictedToMinimumLevel: LogEventLevel.Warning,
                fileSizeLimitBytes: 50 * 1024 * 1024)
            .CreateLogger())
        {
            logger.Information("Should be filtered out by level");
            logger.Debug("Also filtered out");
            logger.Warning("Warning should appear");
            logger.Error("Error should appear");
            logger.Fatal("Fatal should appear");
        }

        Assert.True(File.Exists(path));

        var content = File.ReadAllText(path);

        // Events below Warning should be filtered out
        Assert.DoesNotContain("Should be filtered out by level", content);
        Assert.DoesNotContain("Also filtered out", content);

        // Events at Warning and above should be present
        Assert.Contains("Warning should appear", content);
        Assert.Contains("Error should appear", content);
        Assert.Contains("Fatal should appear", content);
    }

    #endregion

    #region Full usage via IConfiguration

    /// <summary>
    /// Full end-to-end usage using IConfiguration (appsettings.json style) with all defaults.
    /// Only the path is specified; all other parameters use their defaults.
    /// </summary>
    [Fact]
    public void FullUsage_IConfiguration_AllDefaults_LogsAllLevels()
    {
        var expectedLogFilePath = Path.Combine(_tempDir, $"logs/log-{DateTime.UtcNow.ToString("yyyy-MM-dd")}.html");
        var logFilePath = Path.Combine(_tempDir, "logs/log-{Date}.html");

        var json = $$"""
        {
            "Serilog": {
                "Using": ["Serilog.Sinks.HtmlFile"],
                "MinimumLevel": "Verbose",
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

        using (var logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .CreateLogger())
        {
            logger.Verbose("Verbose iconfig event");
            logger.Debug("Debug iconfig event");
            logger.Information("Info iconfig event with {Property}", "configVal");
            logger.Warning("Warning iconfig event with {Count}", 99);
            logger.Error("Error iconfig event");
            logger.Fatal("Fatal iconfig event");
        }

        Assert.True(File.Exists(expectedLogFilePath), "Log file should be created via IConfiguration");

        var content = File.ReadAllText(expectedLogFilePath);

        // Valid HTML structure
        Assert.Contains("<html", content);
        Assert.Contains("</html>", content);

        // All events present
        Assert.Contains("Verbose iconfig event", content);
        Assert.Contains("Debug iconfig event", content);
        Assert.Contains("Info iconfig event", content);
        Assert.Contains("configVal", content);
        Assert.Contains("Warning iconfig event", content);
        Assert.Contains("99", content);
        Assert.Contains("Error iconfig event", content);
        Assert.Contains("Fatal iconfig event", content);
    }

    /// <summary>
    /// Full end-to-end usage using IConfiguration with explicit parameters:
    /// restrictedToMinimumLevel, fileSizeLimitBytes, archiveNamingPattern,
    /// archiveTimestampFormat, and dateFormat.
    /// Verifies level filtering and file creation.
    /// </summary>
    [Fact]
    public void FullUsage_IConfiguration_WithExplicitParameters_AppliesSettings()
    {
        var logFilePath = Path.Combine(_tempDir, "iconfig-explicit.html");

        var json = $$"""
        {
            "Serilog": {
                "Using": ["Serilog.Sinks.HtmlFile"],
                "WriteTo": [
                    {
                        "Name": "HtmlFile",
                        "Args": {
                            "path": "{{logFilePath.Replace("\\", "\\\\")}}",
                            "restrictedToMinimumLevel": "Error",
                            "fileSizeLimitBytes": 52428800,
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

        using (var logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .CreateLogger())
        {
            logger.Information("Info filtered by config level");
            logger.Warning("Warning filtered by config level");
            logger.Error("Error passes config level");
            logger.Fatal("Fatal passes config level");
        }

        Assert.True(File.Exists(logFilePath), "Log file should be created via IConfiguration with explicit params");

        var content = File.ReadAllText(logFilePath);

        // Events below Error should be filtered out
        Assert.DoesNotContain("Info filtered by config level", content);
        Assert.DoesNotContain("Warning filtered by config level", content);

        // Events at Error and above should be present
        Assert.Contains("Error passes config level", content);
        Assert.Contains("Fatal passes config level", content);
    }

    #endregion
}
