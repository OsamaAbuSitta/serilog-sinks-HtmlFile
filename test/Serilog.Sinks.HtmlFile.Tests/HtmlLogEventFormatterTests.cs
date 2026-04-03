using System;
using System.Collections.Generic;
using System.IO;
using Serilog.Events;
using Serilog.Parsing;

namespace Serilog.Sinks.HtmlFile.Tests;

public class HtmlLogEventFormatterTests
{
    private static readonly MessageTemplate EmptyTemplate =
        new MessageTemplate(Array.Empty<MessageTemplateToken>());

    private readonly HtmlLogEventFormatter _formatter = new();

    private string Format(LogEvent evt)
    {
        var sw = new StringWriter();
        _formatter.Format(evt, sw);
        return sw.ToString();
    }

    private static LogEvent CreateEvent(
        LogEventLevel level = LogEventLevel.Information,
        string message = "Hello",
        Exception? exception = null,
        DateTimeOffset? timestamp = null,
        params LogEventProperty[] properties)
    {
        var ts = timestamp ?? new DateTimeOffset(2025, 1, 15, 10, 30, 0, TimeSpan.Zero);
        var tokens = new List<MessageTemplateToken>
        {
            new TextToken(message)
        };
        var template = new MessageTemplate(tokens);
        var props = new List<LogEventProperty>(properties);
        return new LogEvent(ts, level, exception, template, props);
    }

    [Fact]
    public void Format_BasicEvent_ContainsAllFields()
    {
        var evt = CreateEvent(LogEventLevel.Warning, "Request timeout");
        var result = Format(evt);

        Assert.Contains("\"type\":\"Warning\"", result);
        Assert.Contains("\"time\":\"2025-01-15T10:30:00.000Z\"", result);
        Assert.Contains("\"msg\":\"Request timeout\"", result);
        Assert.Contains("\"props\":{}", result);
        Assert.EndsWith(",\n", result);
    }

    [Fact]
    public void Format_WithProperties_IncludesKeyValuePairs()
    {
        var evt = CreateEvent(
            LogEventLevel.Information,
            "Test",
            properties: new[]
            {
                new LogEventProperty("Path", new ScalarValue("/api/users")),
                new LogEventProperty("ElapsedMs", new ScalarValue(5000))
            });
        var result = Format(evt);

        Assert.Contains("\"props\":{\"Path\":\"\\/api\\/users\",\"ElapsedMs\":5000}", result);
    }

    [Fact]
    public void Format_WithException_AppendsExceptionToMessage()
    {
        var ex = new InvalidOperationException("Something broke");
        var evt = CreateEvent(LogEventLevel.Error, "Failed", exception: ex);
        var result = Format(evt);

        Assert.Contains("\"msg\":\"Failed\\n", result);
        Assert.Contains("Something broke", result);
    }

    [Fact]
    public void Format_EmptyMessage_ProducesValidOutput()
    {
        var evt = CreateEvent(LogEventLevel.Debug, "");
        var result = Format(evt);

        Assert.Contains("\"msg\":\"\"", result);
    }

    [Fact]
    public void Format_EmptyMessageWithException_ContainsExceptionOnly()
    {
        var ex = new Exception("Oops");
        var evt = CreateEvent(LogEventLevel.Error, "", exception: ex);
        var result = Format(evt);

        Assert.Contains("\"msg\":\"", result);
        Assert.Contains("Oops", result);
    }

    [Fact]
    public void Format_NullPropertyValue_WritesNull()
    {
        var evt = CreateEvent(
            properties: new[]
            {
                new LogEventProperty("Key", new ScalarValue(null))
            });
        var result = Format(evt);

        Assert.Contains("\"props\":{\"Key\":null}", result);
    }

    [Fact]
    public void Format_SpecialCharactersInMessage_AreEscaped()
    {
        var evt = CreateEvent(message: "Line1\nLine2\tTabbed \"quoted\" <script>alert('xss')</script>");
        var result = Format(evt);

        Assert.Contains("\\n", result);
        Assert.Contains("\\t", result);
        Assert.Contains("\\\"quoted\\\"", result);
        Assert.Contains("\\u003cscript\\u003e", result);
        Assert.Contains("alert(\\'xss\\')", result);
        Assert.Contains("\\u003c\\/script\\u003e", result);
    }

    [Fact]
    public void Format_BooleanProperty_WritesLowercase()
    {
        var evt = CreateEvent(
            properties: new[]
            {
                new LogEventProperty("IsActive", new ScalarValue(true)),
                new LogEventProperty("IsDeleted", new ScalarValue(false))
            });
        var result = Format(evt);

        Assert.Contains("\"IsActive\":true", result);
        Assert.Contains("\"IsDeleted\":false", result);
    }

    [Theory]
    [InlineData(LogEventLevel.Verbose, "Verbose")]
    [InlineData(LogEventLevel.Debug, "Debug")]
    [InlineData(LogEventLevel.Information, "Information")]
    [InlineData(LogEventLevel.Warning, "Warning")]
    [InlineData(LogEventLevel.Error, "Error")]
    [InlineData(LogEventLevel.Fatal, "Fatal")]
    public void Format_AllLogLevels_ProducesCorrectTypeName(LogEventLevel level, string expected)
    {
        var evt = CreateEvent(level: level);
        var result = Format(evt);

        Assert.Contains($"\"type\":\"{expected}\"", result);
    }

    [Fact]
    public void Format_TimestampIsUtcIso8601()
    {
        // Use a non-UTC offset to verify conversion
        var ts = new DateTimeOffset(2025, 6, 15, 14, 30, 45, 123, TimeSpan.FromHours(5));
        var evt = CreateEvent(timestamp: ts);
        var result = Format(evt);

        // 14:30:45.123 +05:00 = 09:30:45.123 UTC
        Assert.Contains("\"time\":\"2025-06-15T09:30:45.123Z\"", result);
    }

    [Fact]
    public void Format_EndsWithCommaAndNewline()
    {
        var evt = CreateEvent();
        var result = Format(evt);

        Assert.EndsWith(",\n", result);
    }

    [Fact]
    public void Format_BackslashInMessage_IsEscaped()
    {
        var evt = CreateEvent(message: "path\\to\\file");
        var result = Format(evt);

        Assert.Contains("path\\\\to\\\\file", result);
    }
}
