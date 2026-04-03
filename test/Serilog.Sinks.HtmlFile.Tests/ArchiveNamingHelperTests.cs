using System;

namespace Serilog.Sinks.HtmlFile.Tests;

public class ArchiveNamingHelperTests
{
    [Fact]
    public void Validate_MissingBaseName_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => ArchiveNamingHelper.Validate("{Timestamp}{Extension}", "yyyyMMddHHmmss"));

        Assert.Contains("{BaseName}", ex.Message);
    }

    [Fact]
    public void Validate_InvalidTimestampFormat_ThrowsFormatException()
    {
        var ex = Assert.Throws<FormatException>(
            () => ArchiveNamingHelper.Validate("{BaseName}_{Timestamp}", "%"));

        Assert.Contains("%", ex.Message);
    }

    [Fact]
    public void Validate_ValidPatternAndFormat_DoesNotThrow()
    {
        var exception = Record.Exception(
            () => ArchiveNamingHelper.Validate("{BaseName}_{Timestamp}{Extension}", "yyyyMMddHHmmss"));

        Assert.Null(exception);
    }

    [Fact]
    public void FormatArchiveName_DefaultPattern_ProducesExpectedName()
    {
        var timestamp = new DateTime(2025, 1, 15, 10, 30, 0, DateTimeKind.Utc);

        var result = ArchiveNamingHelper.FormatArchiveName(
            ArchiveNamingHelper.DefaultPattern,
            "app",
            timestamp,
            ".html",
            ArchiveNamingHelper.DefaultTimestampFormat);

        Assert.Equal("app_20250115103000.html", result);
    }

    [Fact]
    public void FormatArchiveName_CustomPattern_SubstitutesAllPlaceholders()
    {
        var timestamp = new DateTime(2025, 6, 1, 14, 0, 0, DateTimeKind.Utc);

        var result = ArchiveNamingHelper.FormatArchiveName(
            "{BaseName}-archive-{Timestamp}{Extension}",
            "mylog",
            timestamp,
            ".html",
            "yyyy-MM-dd");

        Assert.Equal("mylog-archive-2025-06-01.html", result);
    }

    [Fact]
    public void FormatArchiveName_UnrecognizedPlaceholder_LeftAsLiteralText()
    {
        var timestamp = new DateTime(2025, 1, 15, 10, 30, 0, DateTimeKind.Utc);

        var result = ArchiveNamingHelper.FormatArchiveName(
            "{BaseName}_{Foo}_{Timestamp}{Extension}",
            "app",
            timestamp,
            ".html",
            "yyyyMMddHHmmss");

        Assert.Equal("app_{Foo}_20250115103000.html", result);
    }
}
