using System;
using System.IO;
using System.Linq;
using System.Reflection;
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
}
