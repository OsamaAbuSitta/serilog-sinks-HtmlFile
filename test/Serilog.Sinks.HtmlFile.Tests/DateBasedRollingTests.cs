using System;
using System.IO;
using System.Linq;

namespace Serilog.Sinks.HtmlFile.Tests;

/// <summary>
/// Tests for date-based file naming via fileNamingPattern combined with
/// size-based rolling, including multiple rolls on the same day.
/// </summary>
public class DateBasedRollingTests : IDisposable
{
    private readonly string _tempDir;

    public DateBasedRollingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DateRollingTests_" + Guid.NewGuid().ToString("N"));
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
    /// When fileNamingPattern contains {Date}, the active file should be created
    /// with today's date in the file name instead of using the raw path.
    /// </summary>
    [Fact]
    public void FileNamingPattern_WithDate_CreatesFileWithTodaysDate()
    {
        var pattern = Path.Combine(_tempDir, "app-{Date}.html");
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var expectedPath = Path.Combine(_tempDir, $"app-{today}.html");

        using (var logger = new LoggerConfiguration()
            .WriteTo.HtmlFile(
                path: pattern,
                fileNamingPattern: pattern)
            .CreateLogger())
        {
            logger.Information("Date pattern event");
        }

        Assert.True(File.Exists(expectedPath), $"Expected file at {expectedPath}");
        var content = File.ReadAllText(expectedPath);
        Assert.Contains("Date pattern event", content);
    }

    /// <summary>
    /// When fileNamingPattern contains {Date} and a custom dateFormat is used,
    /// the active file name should reflect the custom format.
    /// </summary>
    [Fact]
    public void FileNamingPattern_WithCustomDateFormat_UsesCustomFormat()
    {
        var pattern = Path.Combine(_tempDir, "log-{Date}.html");
        var today = DateTime.UtcNow.ToString("yyyyMMdd");
        var expectedPath = Path.Combine(_tempDir, $"log-{today}.html");

        using (var logger = new LoggerConfiguration()
            .WriteTo.HtmlFile(
                path: pattern,
                fileNamingPattern: pattern,
                dateFormat: "yyyyMMdd")
            .CreateLogger())
        {
            logger.Information("Custom date format event");
        }

        Assert.True(File.Exists(expectedPath), $"Expected file at {expectedPath}");
    }

    /// <summary>
    /// When fileNamingPattern with {Date} is combined with a small fileSizeLimitBytes,
    /// rolling should produce an archive file whose name is based on the date-evaluated
    /// active file name (not the raw pattern).
    /// </summary>
    [Fact]
    public void FileNamingPattern_WithDate_AndRolling_ArchiveUsesEvaluatedBaseName()
    {
        var pattern = Path.Combine(_tempDir, "app-{Date}.html");
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var expectedActiveName = $"app-{today}.html";

        using (var logger = new LoggerConfiguration()
            .WriteTo.HtmlFile(
                path: pattern,
                fileNamingPattern: pattern,
                fileSizeLimitBytes: 1) // tiny limit to force immediate rolling
            .CreateLogger())
        {
            logger.Information("First event forces roll");
            logger.Information("Second event after roll");
        }

        var files = Directory.GetFiles(_tempDir);
        Assert.True(files.Length >= 2, $"Expected at least 2 files (active + archive), found {files.Length}");

        // The active file should have today's date
        Assert.True(files.Any(f => Path.GetFileName(f) == expectedActiveName),
            $"Expected active file named {expectedActiveName}");

        // Archive files should be based on the date-evaluated name, not the raw pattern
        var archiveFiles = files
            .Where(f => Path.GetFileName(f) != expectedActiveName)
            .ToArray();

        Assert.NotEmpty(archiveFiles);
        foreach (var archive in archiveFiles)
        {
            var name = Path.GetFileName(archive);
            // Archive should start with "app-{today}_" (evaluated base name + underscore)
            Assert.StartsWith($"app-{today}_", name);
            Assert.EndsWith(".html", name);
        }
    }

    /// <summary>
    /// When two rolls happen on the same day but in different seconds, each archive
    /// should get a unique timestamp suffix (yyyyMMddHHmmss), so no files are lost.
    /// A 1-second sleep ensures the default timestamp format produces distinct names.
    /// </summary>
    [Fact]
    public void TwoRolls_SameDay_ProducesDistinctArchiveFiles()
    {
        var path = Path.Combine(_tempDir, "multi-roll.html");
        var longMessage = new string('X', 500);

        using (var logger = new LoggerConfiguration()
            .WriteTo.HtmlFile(
                path: path,
                fileSizeLimitBytes: 1) // tiny limit to force rolling on every emit
            .CreateLogger())
        {
            logger.Information("Roll 1: {Msg}", longMessage);
            // Sleep to ensure the next roll gets a distinct second-level timestamp
            System.Threading.Thread.Sleep(1100);
            logger.Information("Roll 2: {Msg}", longMessage);
        }

        var files = Directory.GetFiles(_tempDir);

        // Should have the active file plus at least 2 archives
        // (header-only roll + first event roll = 2 archives, plus the active file)
        Assert.True(files.Length >= 3,
            $"Expected at least 3 files (active + 2 archives), found {files.Length}: " +
            string.Join(", ", files.Select(Path.GetFileName)));

        // All archive file names should be unique
        var archiveFiles = files
            .Where(f => Path.GetFileName(f) != "multi-roll.html")
            .Select(Path.GetFileName)
            .ToArray();

        Assert.Equal(archiveFiles.Length, archiveFiles.Distinct().Count());
    }

    /// <summary>
    /// When two rolls happen within the same second, the default archive timestamp
    /// format (yyyyMMddHHmmss) produces the same archive name. The second File.Move
    /// fails silently (logged via SelfLog) and the sink continues writing to a new file.
    /// </summary>
    [Fact]
    public void TwoRolls_SameSecond_SecondRollFailsSilently_SinkContinuesWorking()
    {
        var path = Path.Combine(_tempDir, "same-second.html");

        using (var logger = new LoggerConfiguration()
            .WriteTo.HtmlFile(
                path: path,
                fileSizeLimitBytes: 1) // tiny limit to force rolling on every emit
            .CreateLogger())
        {
            // Both emits happen within the same second — second archive rename collides
            logger.Information("Event one");
            logger.Information("Event two");
        }

        // The active file should still exist and be writable despite the collision
        Assert.True(File.Exists(path), "Active file should exist after timestamp collision");

        var content = File.ReadAllText(path);
        Assert.Contains("<html", content);
        Assert.Contains("</html>", content);
    }

    /// <summary>
    /// When fileNamingPattern with {Date} is used and two rolls happen on the same day
    /// in different seconds, archives should have distinct timestamps and the date-based
    /// active file should still be valid after all rolls.
    /// </summary>
    [Fact]
    public void FileNamingPattern_WithDate_TwoRollsSameDay_ProducesDistinctArchives()
    {
        var pattern = Path.Combine(_tempDir, "daily-{Date}.html");
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var expectedActiveName = $"daily-{today}.html";

        using (var logger = new LoggerConfiguration()
            .WriteTo.HtmlFile(
                path: pattern,
                fileNamingPattern: pattern,
                fileSizeLimitBytes: 1) // tiny limit to force rolling
            .CreateLogger())
        {
            logger.Information("Roll A");
            // Sleep to ensure distinct second-level timestamps for archives
            System.Threading.Thread.Sleep(1100);
            logger.Information("Roll B");
        }

        var files = Directory.GetFiles(_tempDir);
        Assert.True(files.Length >= 3,
            $"Expected at least 3 files, found {files.Length}: " +
            string.Join(", ", files.Select(Path.GetFileName)));

        // Active file should exist with today's date
        var activePath = Path.Combine(_tempDir, expectedActiveName);
        Assert.True(File.Exists(activePath), $"Expected active file at {activePath}");

        // Active file should be valid HTML
        var content = File.ReadAllText(activePath);
        Assert.Contains("<html", content);
        Assert.Contains("</html>", content);

        // All archive names should be distinct and follow the date-based pattern
        var archiveNames = files
            .Select(Path.GetFileName)
            .Where(n => n != expectedActiveName)
            .ToArray();

        Assert.True(archiveNames.Length >= 2,
            $"Expected at least 2 archive files, found {archiveNames.Length}");
        Assert.Equal(archiveNames.Length, archiveNames.Distinct().Count());

        foreach (var name in archiveNames)
        {
            Assert.StartsWith($"daily-{today}_", name);
            Assert.EndsWith(".html", name);
        }
    }

    /// <summary>
    /// When fileNamingPattern contains {MachineName}, the active file should
    /// include the machine name in its path.
    /// </summary>
    [Fact]
    public void FileNamingPattern_WithMachineName_CreatesFileWithMachineName()
    {
        var pattern = Path.Combine(_tempDir, "log-{MachineName}.html");
        var machineName = Environment.MachineName;
        var expectedPath = Path.Combine(_tempDir, $"log-{machineName}.html");

        using (var logger = new LoggerConfiguration()
            .WriteTo.HtmlFile(
                path: pattern,
                fileNamingPattern: pattern)
            .CreateLogger())
        {
            logger.Information("Machine name event");
        }

        Assert.True(File.Exists(expectedPath), $"Expected file at {expectedPath}");
    }

    /// <summary>
    /// When fileNamingPattern contains both {Date} and {MachineName},
    /// both placeholders should be evaluated in the active file name.
    /// </summary>
    [Fact]
    public void FileNamingPattern_WithDateAndMachineName_EvaluatesBothPlaceholders()
    {
        var pattern = Path.Combine(_tempDir, "app-{Date}-{MachineName}.html");
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var machineName = Environment.MachineName;
        var expectedPath = Path.Combine(_tempDir, $"app-{today}-{machineName}.html");

        using (var logger = new LoggerConfiguration()
            .WriteTo.HtmlFile(
                path: pattern,
                fileNamingPattern: pattern)
            .CreateLogger())
        {
            logger.Information("Both placeholders event");
        }

        Assert.True(File.Exists(expectedPath), $"Expected file at {expectedPath}");
        var content = File.ReadAllText(expectedPath);
        Assert.Contains("Both placeholders event", content);
    }

    /// <summary>
    /// Verify that after rolling with a date-based pattern, the new active file
    /// is still writable and contains the post-roll event. The archived event
    /// should be in a separate archive file (with distinct timestamps).
    /// </summary>
    [Fact]
    public void FileNamingPattern_AfterRoll_NewActiveFileIsWritable()
    {
        var pattern = Path.Combine(_tempDir, "fresh-{Date}.html");
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var activePath = Path.Combine(_tempDir, $"fresh-{today}.html");
        var uniqueMarker = "POST_ROLL_MARKER_" + Guid.NewGuid().ToString("N");

        using (var logger = new LoggerConfiguration()
            .WriteTo.HtmlFile(
                path: pattern,
                fileNamingPattern: pattern,
                fileSizeLimitBytes: 1) // force roll on every emit
            .CreateLogger())
        {
            logger.Information("Pre-roll event");
            // Sleep to ensure the next roll gets a distinct timestamp
            System.Threading.Thread.Sleep(1100);
            logger.Information(uniqueMarker);
        }

        Assert.True(File.Exists(activePath));

        var activeContent = File.ReadAllText(activePath);
        // The unique marker should be in the active file
        Assert.Contains(uniqueMarker, activeContent);
        // The pre-roll event should be in an archive, not the active file
        Assert.DoesNotContain("Pre-roll event", activeContent);

        // Verify the archive exists and contains the pre-roll event
        var archiveFiles = Directory.GetFiles(_tempDir)
            .Where(f => f != activePath)
            .ToArray();
        Assert.NotEmpty(archiveFiles);

        var allArchiveContent = string.Join("", archiveFiles.Select(File.ReadAllText));
        Assert.Contains("Pre-roll event", allArchiveContent);
    }
}
