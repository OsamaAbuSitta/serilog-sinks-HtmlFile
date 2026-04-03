using System;

namespace Serilog.Sinks.HtmlFile.Tests;

public class FileNamingHelperTests
{
    [Fact]
    public void EvaluatePattern_DatePlaceholder_ProducesDateStampedPath()
    {
        var utcNow = new DateTime(2025, 1, 15, 10, 30, 0, DateTimeKind.Utc);

        var result = FileNamingHelper.EvaluatePattern("logs/app-{Date}.html", utcNow: utcNow);

        Assert.Equal("logs/app-2025-01-15.html", result);
    }

    [Fact]
    public void EvaluatePattern_MachineNamePlaceholder_ProducesMachineNamedPath()
    {
        var result = FileNamingHelper.EvaluatePattern("logs/{MachineName}/app.html");

        var expected = $"logs/{Environment.MachineName}/app.html";
        Assert.Equal(expected, result);
    }

    [Fact]
    public void EvaluatePattern_PlainPath_ReturnsPathAsIs()
    {
        var result = FileNamingHelper.EvaluatePattern("logs/app.html");

        Assert.Equal("logs/app.html", result);
    }

    [Fact]
    public void EvaluatePattern_CustomDateFormat_AppliesFormatCorrectly()
    {
        var utcNow = new DateTime(2025, 6, 1, 14, 0, 0, DateTimeKind.Utc);

        var result = FileNamingHelper.EvaluatePattern("logs/app-{Date}.html", dateFormat: "yyyyMMdd", utcNow: utcNow);

        Assert.Equal("logs/app-20250601.html", result);
    }

    [Fact]
    public void EvaluatePattern_UnrecognizedPlaceholder_LeftAsLiteralText()
    {
        var utcNow = new DateTime(2025, 1, 15, 10, 30, 0, DateTimeKind.Utc);

        var result = FileNamingHelper.EvaluatePattern("logs/app-{Foo}-{Date}.html", utcNow: utcNow);

        Assert.Equal("logs/app-{Foo}-2025-01-15.html", result);
    }

    [Fact]
    public void Validate_EmptyResult_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => FileNamingHelper.Validate("   "));

        Assert.Contains("non-empty", ex.Message);
    }

    [Fact]
    public void Validate_ValidPattern_DoesNotThrow()
    {
        var exception = Record.Exception(
            () => FileNamingHelper.Validate("logs/app-{Date}.html"));

        Assert.Null(exception);
    }
}
