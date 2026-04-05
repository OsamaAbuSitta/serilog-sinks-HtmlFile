using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Serilog.Events;
using Serilog.Parsing;

namespace Serilog.Sinks.HtmlFile.Tests;

/// <summary>
/// Tests for edge cases and potential issues discovered through code review.
/// </summary>
public class EdgeCaseTests : IDisposable
{
    private readonly string _tempDir;
    private readonly HtmlLogEventFormatter _formatter = new();
    private readonly HtmlTemplate _template = new();
    private readonly Encoding _encoding = new UTF8Encoding(false);

    public EdgeCaseTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "EdgeCaseTests_" + Guid.NewGuid().ToString("N"));
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

    private static LogEvent CreateEvent(
        string message = "Hello",
        LogEventLevel level = LogEventLevel.Information,
        Exception? exception = null,
        params LogEventProperty[] properties)
    {
        var tokens = new List<MessageTemplateToken> { new TextToken(message) };
        var template = new MessageTemplate(tokens);
        return new LogEvent(DateTimeOffset.UtcNow, level, exception, template, properties);
    }

    #region Property key with special characters (potential JS breakage)

    /// <summary>
    /// Property keys containing double quotes are written unescaped by
    /// HtmlLogEventFormatter.WriteProperties, which produces invalid JS.
    /// This test documents the current behavior.
    /// </summary>
    [Fact]
    public void PropertyKey_WithDoubleQuote_OutputContainsUnescapedQuote()
    {
        var evt = CreateEvent(
            properties: new[]
            {
                new LogEventProperty("key\"break", new ScalarValue("value"))
            });

        var sw = new StringWriter();
        _formatter.Format(evt, sw);
        var result = sw.ToString();

        // The key is written unescaped — this produces: "key"break":"value"
        // which is invalid JS. This test documents the current behavior.
        Assert.Contains("\"key\"break\"", result);
    }

    /// <summary>
    /// Property keys containing backslash are written unescaped.
    /// This test documents the current behavior.
    /// </summary>
    [Fact]
    public void PropertyKey_WithBackslash_OutputContainsUnescapedBackslash()
    {
        var evt = CreateEvent(
            properties: new[]
            {
                new LogEventProperty("path\\key", new ScalarValue("value"))
            });

        var sw = new StringWriter();
        _formatter.Format(evt, sw);
        var result = sw.ToString();

        // Backslash in key is not escaped
        Assert.Contains("\"path\\key\"", result);
    }

    /// <summary>
    /// Property keys containing angle brackets are written unescaped.
    /// This test documents the current behavior.
    /// </summary>
    [Fact]
    public void PropertyKey_WithAngleBrackets_OutputContainsUnescapedBrackets()
    {
        var evt = CreateEvent(
            properties: new[]
            {
                new LogEventProperty("<script>", new ScalarValue("xss"))
            });

        var sw = new StringWriter();
        _formatter.Format(evt, sw);
        var result = sw.ToString();

        // Angle brackets in key are not escaped
        Assert.Contains("\"<script>\"", result);
    }

    #endregion

    #region Custom template validation

    /// <summary>
    /// HtmlTemplate constructor should throw FileNotFoundException when
    /// the custom template path points to a non-existent file.
    /// </summary>
    [Fact]
    public void CustomTemplate_FileNotFound_ThrowsFileNotFoundException()
    {
        var missingPath = Path.Combine(_tempDir, "does-not-exist.html");

        Assert.Throws<FileNotFoundException>(
            () => new HtmlTemplate(missingPath));
    }

    /// <summary>
    /// HtmlTemplate constructor should throw InvalidOperationException when
    /// the custom template file does not contain the required placeholder marker.
    /// </summary>
    [Fact]
    public void CustomTemplate_MissingPlaceholder_ThrowsInvalidOperationException()
    {
        var templatePath = Path.Combine(_tempDir, "no-placeholder.html");
        File.WriteAllText(templatePath, "<html><body>No placeholder here</body></html>");

        var ex = Assert.Throws<InvalidOperationException>(
            () => new HtmlTemplate(templatePath));

        Assert.Contains("LOG_ENTRIES_PLACEHOLDER", ex.Message);
    }

    /// <summary>
    /// HtmlTemplate constructor should succeed when the custom template file
    /// contains the placeholder marker.
    /// </summary>
    [Fact]
    public void CustomTemplate_WithPlaceholder_Succeeds()
    {
        var templatePath = Path.Combine(_tempDir, "valid-template.html");
        File.WriteAllText(templatePath,
            "<html><body><!-- LOG_ENTRIES_PLACEHOLDER --></body></html>");

        var exception = Record.Exception(() => new HtmlTemplate(templatePath));
        Assert.Null(exception);
    }

    #endregion

    #region Null argument validation

    /// <summary>
    /// WriteTo.HtmlFile with null path should throw ArgumentNullException.
    /// </summary>
    [Fact]
    public void ExtensionMethod_NullPath_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new LoggerConfiguration().WriteTo.HtmlFile(path: null!));
    }

    /// <summary>
    /// ArchiveNamingHelper.Validate with null pattern should throw ArgumentNullException.
    /// </summary>
    [Fact]
    public void ArchiveNamingHelper_Validate_NullPattern_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ArchiveNamingHelper.Validate(null!, "yyyyMMddHHmmss"));
    }

    /// <summary>
    /// ArchiveNamingHelper.Validate with null timestamp format should throw ArgumentNullException.
    /// </summary>
    [Fact]
    public void ArchiveNamingHelper_Validate_NullTimestampFormat_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ArchiveNamingHelper.Validate("{BaseName}_{Timestamp}", null!));
    }

    /// <summary>
    /// FileNamingHelper.Validate with null pattern should throw ArgumentNullException.
    /// </summary>
    [Fact]
    public void FileNamingHelper_Validate_NullPattern_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            FileNamingHelper.Validate(null!));
    }

    /// <summary>
    /// FileNamingHelper.EvaluatePattern with null throws NullReferenceException
    /// because string.Replace is called on null. There is no explicit null guard.
    /// This test documents the current behavior.
    /// </summary>
    [Fact]
    public void FileNamingHelper_EvaluatePattern_NullPattern_Throws()
    {
        Assert.ThrowsAny<NullReferenceException>(() =>
            FileNamingHelper.EvaluatePattern(null!));
    }

    #endregion

    #region Multi-byte UTF-8 characters (seek offset correctness)

    /// <summary>
    /// Messages containing multi-byte UTF-8 characters (emoji, CJK) should not
    /// corrupt the file structure. The seek-based insertion must correctly account
    /// for byte offsets vs character offsets.
    /// </summary>
    [Fact]
    public void MultiByte_Emoji_InMessage_FileRemainsStructurallyValid()
    {
        var path = Path.Combine(_tempDir, "emoji.html");

        using (var sink = new HtmlFileSink(path, _formatter, _template, null, _encoding))
        {
            sink.Emit(CreateEvent("Hello 🌍🔥 World"));
            sink.Emit(CreateEvent("Second entry after emoji"));
        }

        var content = File.ReadAllText(path);

        Assert.Contains("Hello", content);
        Assert.Contains("World", content);
        Assert.Contains("Second entry after emoji", content);
        Assert.Contains("</html>", content);
        Assert.Contains("var logEntries = [", content);
    }

    /// <summary>
    /// Reopening a file containing multi-byte characters should correctly recover
    /// the insertion offset and append without corruption.
    /// </summary>
    [Fact]
    public void MultiByte_CJK_ReopenFile_AppendsCorrectly()
    {
        var path = Path.Combine(_tempDir, "cjk.html");

        using (var sink = new HtmlFileSink(path, _formatter, _template, null, _encoding))
        {
            sink.Emit(CreateEvent("日本語テスト"));
            sink.Emit(CreateEvent("中文测试"));
        }

        // Reopen and append
        using (var sink = new HtmlFileSink(path, _formatter, _template, null, _encoding))
        {
            sink.Emit(CreateEvent("After reopen with CJK"));
        }

        var content = File.ReadAllText(path);

        Assert.Contains("日本語テスト", content);
        Assert.Contains("中文测试", content);
        Assert.Contains("After reopen with CJK", content);
        Assert.Contains("</html>", content);
    }

    /// <summary>
    /// Properties containing multi-byte characters should be correctly escaped
    /// and not corrupt the output.
    /// </summary>
    [Fact]
    public void MultiByte_InPropertyValue_FormatsCorrectly()
    {
        var path = Path.Combine(_tempDir, "mb-props.html");

        using (var logger = new LoggerConfiguration()
            .WriteTo.HtmlFile(path, fileSizeLimitBytes: null)
            .CreateLogger())
        {
            logger.Information("User {Name} logged in from {Location}", "田中太郎", "東京");
        }

        var content = File.ReadAllText(path);
        Assert.Contains("田中太郎", content);
        Assert.Contains("東京", content);
        Assert.Contains("</html>", content);
    }

    #endregion

    #region Auto directory creation

    /// <summary>
    /// HtmlFileSink should automatically create the parent directory if it does not exist.
    /// </summary>
    [Fact]
    public void AutoCreateDirectory_WhenDirectoryDoesNotExist()
    {
        var nestedDir = Path.Combine(_tempDir, "nested", "deep", "dir");
        var path = Path.Combine(nestedDir, "auto-dir.html");

        Assert.False(Directory.Exists(nestedDir));

        using (var sink = new HtmlFileSink(path, _formatter, _template, null, _encoding))
        {
            sink.Emit(CreateEvent("Auto directory test"));
        }

        Assert.True(Directory.Exists(nestedDir));
        Assert.True(File.Exists(path));

        var content = File.ReadAllText(path);
        Assert.Contains("Auto directory test", content);
    }

    #endregion

    #region File path with spaces

    /// <summary>
    /// File paths containing spaces should work correctly.
    /// </summary>
    [Fact]
    public void FilePath_WithSpaces_WorksCorrectly()
    {
        var dirWithSpaces = Path.Combine(_tempDir, "my log dir");
        Directory.CreateDirectory(dirWithSpaces);
        var path = Path.Combine(dirWithSpaces, "my log file.html");

        using (var logger = new LoggerConfiguration()
            .WriteTo.HtmlFile(path, fileSizeLimitBytes: null)
            .CreateLogger())
        {
            logger.Information("Spaces in path test");
        }

        Assert.True(File.Exists(path));
        var content = File.ReadAllText(path);
        Assert.Contains("Spaces in path test", content);
    }

    #endregion

    #region Double Dispose safety

    /// <summary>
    /// Disposing HtmlFileSink twice should not throw.
    /// </summary>
    [Fact]
    public void HtmlFileSink_DoubleDispose_DoesNotThrow()
    {
        var path = Path.Combine(_tempDir, "double-dispose.html");
        var sink = new HtmlFileSink(path, _formatter, _template, null, _encoding);
        sink.Emit(CreateEvent("Before dispose"));

        var exception = Record.Exception(() =>
        {
            sink.Dispose();
            sink.Dispose();
        });

        Assert.Null(exception);
    }

    /// <summary>
    /// Disposing RollingHtmlFileSink twice should not throw.
    /// </summary>
    [Fact]
    public void RollingHtmlFileSink_DoubleDispose_DoesNotThrow()
    {
        var path = Path.Combine(_tempDir, "rolling-double-dispose.html");
        var sink = new RollingHtmlFileSink(path, _formatter, _template, 1073741824L, _encoding);
        sink.Emit(CreateEvent("Before dispose"));

        var exception = Record.Exception(() =>
        {
            sink.Dispose();
            sink.Dispose();
        });

        Assert.Null(exception);
    }

    #endregion

    #region Emit after Dispose

    /// <summary>
    /// Emitting after Dispose should be silently ignored, not throw.
    /// </summary>
    [Fact]
    public void HtmlFileSink_EmitAfterDispose_SilentlyIgnored()
    {
        var path = Path.Combine(_tempDir, "emit-after-dispose.html");
        var sink = new HtmlFileSink(path, _formatter, _template, null, _encoding);
        sink.Emit(CreateEvent("Before dispose"));
        sink.Dispose();

        var exception = Record.Exception(() =>
            sink.Emit(CreateEvent("After dispose")));

        Assert.Null(exception);
    }

    /// <summary>
    /// Emitting after Dispose on RollingHtmlFileSink should be silently ignored.
    /// </summary>
    [Fact]
    public void RollingHtmlFileSink_EmitAfterDispose_SilentlyIgnored()
    {
        var path = Path.Combine(_tempDir, "rolling-emit-after-dispose.html");
        var sink = new RollingHtmlFileSink(path, _formatter, _template, 1073741824L, _encoding);
        sink.Emit(CreateEvent("Before dispose"));
        sink.Dispose();

        var exception = Record.Exception(() =>
            sink.Emit(CreateEvent("After dispose")));

        Assert.Null(exception);
    }

    #endregion

    #region Exception in log message

    /// <summary>
    /// A deeply nested exception should be fully serialized without truncation
    /// and the file should remain valid.
    /// </summary>
    [Fact]
    public void DeepNestedException_FullySerialized_FileRemainsValid()
    {
        var path = Path.Combine(_tempDir, "nested-ex.html");

        Exception deepEx;
        try
        {
            try
            {
                try
                {
                    throw new InvalidOperationException("innermost");
                }
                catch (Exception ex1)
                {
                    throw new ArgumentException("middle", ex1);
                }
            }
            catch (Exception ex2)
            {
                throw new ApplicationException("outermost", ex2);
            }
        }
        catch (Exception ex3)
        {
            deepEx = ex3;
        }

        using (var sink = new HtmlFileSink(path, _formatter, _template, null, _encoding))
        {
            sink.Emit(CreateEvent("Error occurred", LogEventLevel.Error, deepEx));
        }

        var content = File.ReadAllText(path);
        Assert.Contains("innermost", content);
        Assert.Contains("middle", content);
        Assert.Contains("outermost", content);
        Assert.Contains("</html>", content);
    }

    #endregion

    #region Very long message

    /// <summary>
    /// A very long message should be written without truncation and the file
    /// should remain structurally valid.
    /// </summary>
    [Fact]
    public void VeryLongMessage_NotTruncated_FileRemainsValid()
    {
        var path = Path.Combine(_tempDir, "long-msg.html");
        var longMessage = "START_" + new string('A', 100_000) + "_END";

        using (var sink = new HtmlFileSink(path, _formatter, _template, null, _encoding))
        {
            sink.Emit(CreateEvent(longMessage));
        }

        var content = File.ReadAllText(path);
        Assert.Contains("START_", content);
        Assert.Contains("_END", content);
        Assert.Contains("</html>", content);
    }

    #endregion

    #region fileSizeLimitBytes boundary values

    /// <summary>
    /// fileSizeLimitBytes = 0 should throw ArgumentException.
    /// </summary>
    [Fact]
    public void FileSizeLimitBytes_Zero_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            new LoggerConfiguration().WriteTo.HtmlFile(
                Path.Combine(_tempDir, "zero.html"),
                fileSizeLimitBytes: 0));
    }

    /// <summary>
    /// fileSizeLimitBytes = -1 should throw ArgumentException.
    /// </summary>
    [Fact]
    public void FileSizeLimitBytes_Negative_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            new LoggerConfiguration().WriteTo.HtmlFile(
                Path.Combine(_tempDir, "negative.html"),
                fileSizeLimitBytes: -1));
    }

    /// <summary>
    /// fileSizeLimitBytes = 1 (minimum valid) should not throw.
    /// </summary>
    [Fact]
    public void FileSizeLimitBytes_One_DoesNotThrow()
    {
        var exception = Record.Exception(() =>
        {
            using var logger = new LoggerConfiguration()
                .WriteTo.HtmlFile(
                    Path.Combine(_tempDir, "one-byte.html"),
                    fileSizeLimitBytes: 1)
                .CreateLogger();
        });

        Assert.Null(exception);
    }

    #endregion
}
