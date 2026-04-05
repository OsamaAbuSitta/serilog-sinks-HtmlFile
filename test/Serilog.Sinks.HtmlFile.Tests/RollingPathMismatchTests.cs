using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Parsing;

namespace Serilog.Sinks.HtmlFile.Tests;

/// <summary>
/// Tests exposing a path mismatch between RollingHtmlFileSink and HtmlFileSink.
///
/// Bug: HtmlFileSink always evaluates FileNamingHelper.EvaluatePattern on the filename
/// (line 80), replacing {Date} and {MachineName} in the actual file path. But
/// RollingHtmlFileSink._currentPath stores the raw unevaluated path when
/// fileNamingPattern is null. When TryRoll() calls File.Move(_currentPath, archivePath),
/// the source path doesn't match the actual file on disk, so the move fails silently.
/// Rolling never actually works in this scenario.
/// </summary>
[Collection("SelfLog")]
public class RollingPathMismatchTests : IDisposable
{
    private readonly string _tempDir;
    private readonly HtmlLogEventFormatter _formatter = new();
    private readonly HtmlTemplate _template = new();
    private readonly Encoding _encoding = new UTF8Encoding(false);

    public RollingPathMismatchTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "RollingPathMismatch_" + Guid.NewGuid().ToString("N"));
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

    private static LogEvent CreateEvent(string message)
    {
        var tokens = new List<MessageTemplateToken> { new TextToken(message) };
        var template = new MessageTemplate(tokens);
        return new LogEvent(DateTimeOffset.UtcNow, LogEventLevel.Information, null, template,
            Array.Empty<LogEventProperty>());
    }

    /// <summary>
    /// When path contains {Date} but fileNamingPattern is null, HtmlFileSink creates
    /// the file at the evaluated path (e.g., app-2026-04-05.html), but
    /// RollingHtmlFileSink._currentPath remains "app-{Date}.html".
    /// On roll, File.Move tries to rename "app-{Date}.html" which doesn't exist,
    /// so the move fails and no archive is created.
    /// </summary>
    [Fact]
    public void PathWithDatePlaceholder_NoFileNamingPattern_RollingFailsToArchive()
    {
        var rawPath = Path.Combine(_tempDir, "app-{Date}.html");
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var evaluatedPath = Path.Combine(_tempDir, $"app-{today}.html");

        var selfLogMessages = new List<string>();
        SelfLog.Enable(msg => selfLogMessages.Add(msg));
        try
        {
            // RollingHtmlFileSink stores rawPath as _currentPath (fileNamingPattern is null)
            // HtmlFileSink evaluates {Date} internally and creates file at evaluatedPath
            using (var sink = new RollingHtmlFileSink(
                rawPath, _formatter, _template, 1, _encoding))
            {
                sink.Emit(CreateEvent("Event before roll attempt"));
                // Sleep to get a distinct timestamp for the archive
                System.Threading.Thread.Sleep(1100);
                sink.Emit(CreateEvent("Event triggering roll"));
            }
        }
        finally
        {
            SelfLog.Disable();
        }

        // The file should exist at the EVALUATED path (HtmlFileSink evaluated {Date})
        Assert.True(File.Exists(evaluatedPath),
            $"File should exist at evaluated path {evaluatedPath}");

        // BUG: No archive was created because File.Move("app-{Date}.html", ...)
        // failed — the source file doesn't exist at the unevaluated path
        var allFiles = Directory.GetFiles(_tempDir, "*.html");
        var archiveFiles = allFiles
            .Where(f => Path.GetFileName(f) != $"app-{today}.html")
            .ToArray();

        // This assertion exposes the bug: rolling doesn't produce archives
        // because _currentPath and the actual file path diverge
        Assert.Empty(archiveFiles); // No archives created — the roll failed silently

        // SelfLog should have recorded the failed File.Move
        Assert.Contains(selfLogMessages,
            m => m.Contains("roll", StringComparison.OrdinalIgnoreCase) ||
                 m.Contains("failed", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Contrast: when fileNamingPattern IS set, the paths are consistent and
    /// rolling works correctly — archives are created.
    /// </summary>
    [Fact]
    public void PathWithDatePlaceholder_WithFileNamingPattern_RollingWorksCorrectly()
    {
        var pattern = Path.Combine(_tempDir, "app-{Date}.html");
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var evaluatedName = $"app-{today}.html";

        using (var sink = new RollingHtmlFileSink(
            pattern, _formatter, _template, 1, _encoding,
            fileNamingPattern: pattern))
        {
            sink.Emit(CreateEvent("Event before roll"));
            System.Threading.Thread.Sleep(1100);
            sink.Emit(CreateEvent("Event after roll"));
        }

        var allFiles = Directory.GetFiles(_tempDir, "*.html");
        var archiveFiles = allFiles
            .Where(f => Path.GetFileName(f) != evaluatedName)
            .ToArray();

        // With fileNamingPattern set, _currentPath is correctly evaluated,
        // so File.Move succeeds and archives are created
        Assert.NotEmpty(archiveFiles);

        // Active file exists
        var activePath = Path.Combine(_tempDir, evaluatedName);
        Assert.True(File.Exists(activePath));
    }

    /// <summary>
    /// When path contains {MachineName} but fileNamingPattern is null, the same
    /// mismatch occurs: HtmlFileSink creates the file with the machine name
    /// substituted, but RollingHtmlFileSink tries to move the unevaluated path.
    /// </summary>
    [Fact]
    public void PathWithMachineNamePlaceholder_NoFileNamingPattern_RollingFailsToArchive()
    {
        var rawPath = Path.Combine(_tempDir, "app-{MachineName}.html");
        var machineName = Environment.MachineName;
        var evaluatedPath = Path.Combine(_tempDir, $"app-{machineName}.html");

        var selfLogMessages = new List<string>();
        SelfLog.Enable(msg => selfLogMessages.Add(msg));
        try
        {
            using (var sink = new RollingHtmlFileSink(
                rawPath, _formatter, _template, 1, _encoding))
            {
                sink.Emit(CreateEvent("Event with machine name"));
                System.Threading.Thread.Sleep(1100);
                sink.Emit(CreateEvent("Triggers roll with machine name"));
            }
        }
        finally
        {
            SelfLog.Disable();
        }

        // File exists at evaluated path
        Assert.True(File.Exists(evaluatedPath),
            $"File should exist at evaluated path {evaluatedPath}");

        // BUG: No archives created — same path mismatch as the {Date} case
        var archiveFiles = Directory.GetFiles(_tempDir, "*.html")
            .Where(f => Path.GetFileName(f) != $"app-{machineName}.html")
            .ToArray();

        Assert.Empty(archiveFiles);
    }

    /// <summary>
    /// Via the public API (extension method), when the user puts {Date} in the
    /// path parameter but doesn't set fileNamingPattern, the default fileSizeLimitBytes
    /// (1 GB) enables rolling. But rolling silently fails due to the path mismatch.
    /// This test shows the issue through the user-facing API.
    /// </summary>
    [Fact]
    public void PublicApi_PathWithDate_DefaultRolling_RollingFailsSilently()
    {
        var rawPath = Path.Combine(_tempDir, "log-{Date}.html");
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var evaluatedPath = Path.Combine(_tempDir, $"log-{today}.html");

        using (var logger = new LoggerConfiguration()
            .WriteTo.HtmlFile(
                path: rawPath,
                // fileNamingPattern NOT set — defaults to null
                fileSizeLimitBytes: 1) // tiny limit to trigger rolling
            .CreateLogger())
        {
            logger.Information("First event");
            System.Threading.Thread.Sleep(1100);
            logger.Information("Second event triggers roll attempt");
        }

        // File is created at the evaluated path
        Assert.True(File.Exists(evaluatedPath));

        // BUG: No archive files exist — rolling failed silently
        var allFiles = Directory.GetFiles(_tempDir, "*.html");
        Assert.Single(allFiles); // Only the active file, no archives

        // Both events end up in the same file because the roll failed
        var content = File.ReadAllText(evaluatedPath);
        Assert.Contains("First event", content);
        Assert.Contains("Second event", content);
    }

    /// <summary>
    /// Contrast: same scenario but with fileNamingPattern explicitly set — rolling works.
    /// </summary>
    [Fact]
    public void PublicApi_PathWithDate_WithFileNamingPattern_RollingWorks()
    {
        var pattern = Path.Combine(_tempDir, "log-{Date}.html");
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var evaluatedName = $"log-{today}.html";

        using (var logger = new LoggerConfiguration()
            .WriteTo.HtmlFile(
                path: pattern,
                fileNamingPattern: pattern, // explicitly set
                fileSizeLimitBytes: 1)
            .CreateLogger())
        {
            logger.Information("First event");
            System.Threading.Thread.Sleep(1100);
            logger.Information("Second event");
        }

        var allFiles = Directory.GetFiles(_tempDir, "*.html");

        // With fileNamingPattern set, rolling works — archives are created
        Assert.True(allFiles.Length >= 2,
            $"Expected at least 2 files (active + archive), found {allFiles.Length}: " +
            string.Join(", ", allFiles.Select(Path.GetFileName)));
    }
}
