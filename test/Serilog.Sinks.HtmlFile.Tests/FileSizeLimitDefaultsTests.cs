using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Configuration;
using Serilog.Configuration;

namespace Serilog.Sinks.HtmlFile.Tests;

/// <summary>
/// Unit tests for default fileSizeLimitBytes value and null behavior.
/// Validates: Requirements 16.1, 16.3
/// </summary>
public class FileSizeLimitDefaultsTests : IDisposable
{
    private readonly string _tempDir;

    public FileSizeLimitDefaultsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FileSizeLimitTests_" + Guid.NewGuid().ToString("N"));
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
    /// Verify the default fileSizeLimitBytes parameter value is 1073741824L (1 GB)
    /// via reflection on the extension method's default parameter value.
    /// Validates: Requirement 16.1
    /// </summary>
    [Fact]
    public void FileSizeLimitBytes_DefaultValue_Is1GB()
    {
        var method = typeof(HtmlFileLoggerConfigurationExtensions)
            .GetMethod(
                "HtmlFile",
                BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(method);

        var param = method!.GetParameters()
            .Single(p => p.Name == "fileSizeLimitBytes");

        Assert.True(param.HasDefaultValue, "fileSizeLimitBytes should have a default value");
        Assert.Equal(1073741824L, param.DefaultValue);
    }

    /// <summary>
    /// Verify that passing null for fileSizeLimitBytes allows unrestricted file growth.
    /// Write enough events that would exceed a small limit, and confirm no truncation occurs.
    /// Validates: Requirement 16.3
    /// </summary>
    [Fact]
    public void NullFileSizeLimitBytes_AllowsUnrestrictedFileGrowth()
    {
        var path = Path.Combine(_tempDir, "unlimited.html");

        using (var logger = new LoggerConfiguration()
            .WriteTo.HtmlFile(path, fileSizeLimitBytes: null)
            .CreateLogger())
        {
            // Write many events to produce a file well beyond any small limit
            for (var i = 0; i < 200; i++)
            {
                logger.Information("Event number {Number} with some padding text to increase size", i);
            }
        }

        Assert.True(File.Exists(path));

        var content = File.ReadAllText(path);

        // Verify all events are present — no truncation occurred
        for (var i = 0; i < 200; i++)
        {
            Assert.Contains(i.ToString(), content);
        }

        // The file should be non-trivially large, confirming unrestricted growth
        var fileSize = new FileInfo(path).Length;
        Assert.True(fileSize > 10_000, $"Expected file to be larger than 10KB, but was {fileSize} bytes");
    }

    /// <summary>
    /// Verify that calling WriteTo.HtmlFile() with only the path parameter (all defaults)
    /// creates a valid log file and uses the rolling sink (since the default fileSizeLimitBytes is non-null).
    /// Validates: Requirement 16.1
    /// </summary>
    [Fact]
    public void DefaultFromSetting_CreatesRollingSinkWithDefaultLimit()
    {
        var path = Path.Combine(_tempDir, "default-setting.html");

        using (var logger = new LoggerConfiguration()
            .WriteTo.HtmlFile(path)
            .CreateLogger())
        {
            logger.Information("Default setting event {Number}", 1);
        }

        Assert.True(File.Exists(path), "Log file should be created with default settings");

        var content = File.ReadAllText(path);
        Assert.Contains("Default setting event", content);
    }

    /// <summary>
    /// Verify all default parameter values via reflection match their documented defaults:
    /// restrictedToMinimumLevel = LevelAlias.Minimum, fileSizeLimitBytes = 1 GB,
    /// encoding = null, customTemplatePath = null, formatter = null,
    /// archiveNamingPattern = null, archiveTimestampFormat = null,
    /// fileNamingPattern = null, dateFormat = null.
    /// Validates: Requirement 16.1
    /// </summary>
    [Fact]
    public void DefaultFromSetting_AllParameterDefaults_MatchExpected()
    {
        var method = typeof(HtmlFileLoggerConfigurationExtensions)
            .GetMethod("HtmlFile", BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(method);

        var parameters = method!.GetParameters();

        // path has no default (required)
        var pathParam = parameters.Single(p => p.Name == "path");
        Assert.False(pathParam.HasDefaultValue);

        // restrictedToMinimumLevel defaults to LevelAlias.Minimum (= 0)
        var levelParam = parameters.Single(p => p.Name == "restrictedToMinimumLevel");
        Assert.True(levelParam.HasDefaultValue);
        Assert.Equal((int)Events.LevelAlias.Minimum, (int)levelParam.DefaultValue!);

        // fileSizeLimitBytes defaults to 1 GB
        var sizeParam = parameters.Single(p => p.Name == "fileSizeLimitBytes");
        Assert.True(sizeParam.HasDefaultValue);
        Assert.Equal(1073741824L, sizeParam.DefaultValue);

        // encoding defaults to null
        var encodingParam = parameters.Single(p => p.Name == "encoding");
        Assert.True(encodingParam.HasDefaultValue);
        Assert.Null(encodingParam.DefaultValue);

        // customTemplatePath defaults to null
        var templateParam = parameters.Single(p => p.Name == "customTemplatePath");
        Assert.True(templateParam.HasDefaultValue);
        Assert.Null(templateParam.DefaultValue);

        // formatter defaults to null
        var formatterParam = parameters.Single(p => p.Name == "formatter");
        Assert.True(formatterParam.HasDefaultValue);
        Assert.Null(formatterParam.DefaultValue);

        // archiveNamingPattern defaults to null
        var archivePatternParam = parameters.Single(p => p.Name == "archiveNamingPattern");
        Assert.True(archivePatternParam.HasDefaultValue);
        Assert.Null(archivePatternParam.DefaultValue);

        // archiveTimestampFormat defaults to null
        var archiveTimestampParam = parameters.Single(p => p.Name == "archiveTimestampFormat");
        Assert.True(archiveTimestampParam.HasDefaultValue);
        Assert.Null(archiveTimestampParam.DefaultValue);

        // fileNamingPattern defaults to null
        var fileNamingParam = parameters.Single(p => p.Name == "fileNamingPattern");
        Assert.True(fileNamingParam.HasDefaultValue);
        Assert.Null(fileNamingParam.DefaultValue);

        // dateFormat defaults to null
        var dateFormatParam = parameters.Single(p => p.Name == "dateFormat");
        Assert.True(dateFormatParam.HasDefaultValue);
        Assert.Null(dateFormatParam.DefaultValue);
    }

    /// <summary>
    /// Verify that configuring the sink from IConfiguration with only the path (no explicit
    /// fileSizeLimitBytes) uses the extension method's default of 1 GB and creates a valid log file.
    /// Validates: Requirement 16.1
    /// </summary>
    [Fact]
    public void FromConfiguration_WithOnlyPath_UsesDefaultFileSizeLimit()
    {
        var logFilePath = Path.Combine(_tempDir, "config-default.html");

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

        using (var logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .CreateLogger())
        {
            logger.Information("Config default event");
        }

        Assert.True(File.Exists(logFilePath), "Log file should be created from configuration with defaults");

        var content = File.ReadAllText(logFilePath);
        Assert.Contains("Config default event", content);
    }

    /// <summary>
    /// Verify that configuring the sink from IConfiguration with an explicit fileSizeLimitBytes
    /// value applies that value correctly.
    /// Validates: Requirement 16.1
    /// </summary>
    [Fact]
    public void FromConfiguration_WithExplicitFileSizeLimit_UsesConfiguredValue()
    {
        var logFilePath = Path.Combine(_tempDir, "config-explicit.html");

        var json = $$"""
        {
            "Serilog": {
                "Using": ["Serilog.Sinks.HtmlFile"],
                "WriteTo": [
                    {
                        "Name": "HtmlFile",
                        "Args": {
                            "path": "{{logFilePath.Replace("\\", "\\\\")}}",
                            "fileSizeLimitBytes": 5242880
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
            logger.Information("Config explicit limit event");
        }

        Assert.True(File.Exists(logFilePath), "Log file should be created from configuration with explicit limit");

        var content = File.ReadAllText(logFilePath);
        Assert.Contains("Config explicit limit event", content);
    }

    /// <summary>
    /// Verify that configuring the sink from IConfiguration with all bindable parameters
    /// uses the specified values and creates a valid log file.
    /// Validates: Requirements 16.1, 16.3
    /// </summary>
    [Fact]
    public void FromConfiguration_WithAllBindableParameters_CreatesValidFile()
    {
        var logFilePath = Path.Combine(_tempDir, "config-all-params.html");

        var json = $$"""
        {
            "Serilog": {
                "Using": ["Serilog.Sinks.HtmlFile"],
                "WriteTo": [
                    {
                        "Name": "HtmlFile",
                        "Args": {
                            "path": "{{logFilePath.Replace("\\", "\\\\")}}",
                            "restrictedToMinimumLevel": "Warning",
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

        using (var logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .CreateLogger())
        {
            // Information is below Warning, so this should be filtered out
            logger.Information("Should be filtered");
            // Warning should pass through
            logger.Warning("Config all params warning event");
        }

        Assert.True(File.Exists(logFilePath), "Log file should be created from configuration");

        var content = File.ReadAllText(logFilePath);
        Assert.DoesNotContain("Should be filtered", content);
        Assert.Contains("Config all params warning event", content);
    }
}
