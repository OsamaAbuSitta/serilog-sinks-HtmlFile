using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Serilog.Events;
using Serilog.Parsing;

namespace Serilog.Sinks.HtmlFile.Tests;

/// <summary>
/// Feature: production-readiness, Property 1: All property keys are double-quoted
/// Validates: Requirements 5.1, 5.2
/// </summary>
public class FormatterPropertyTests
{
    private static readonly HtmlLogEventFormatter Formatter = new();

    /// <summary>
    /// Property 1: All property keys are double-quoted.
    /// For any valid LogEvent with any number of properties (including keys containing
    /// hyphens, @ prefixes, digits, spaces, or other non-identifier characters),
    /// the formatted output SHALL enclose every property key in double quotes.
    ///
    /// **Validates: Requirements 5.1, 5.2**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(LogEventArbitraries) })]
    public bool FormatterQuotesAllPropertyKeys(LogEvent logEvent)
    {
        var sw = new StringWriter();
        Formatter.Format(logEvent, sw);
        var output = sw.ToString();

        // 1. Verify top-level keys are quoted
        if (!output.Contains("\"type\":")) return false;
        if (!output.Contains("\"time\":")) return false;
        if (!output.Contains("\"msg\":")) return false;
        if (!output.Contains("\"props\":")) return false;

        // 2. Extract the props object content and verify all property keys are quoted
        var propsStart = output.IndexOf("\"props\":{", StringComparison.Ordinal);
        if (propsStart < 0) return false;

        var propsContentStart = propsStart + "\"props\":{".Length;
        var braceDepth = 1;
        var propsContentEnd = propsContentStart;
        for (var i = propsContentStart; i < output.Length && braceDepth > 0; i++)
        {
            if (output[i] == '{') braceDepth++;
            else if (output[i] == '}') braceDepth--;
            if (braceDepth == 0) propsContentEnd = i;
        }

        var propsContent = output.Substring(propsContentStart, propsContentEnd - propsContentStart);

        if (string.IsNullOrEmpty(propsContent)) return true;

        return VerifyAllKeysQuoted(propsContent);
    }

    /// <summary>
    /// Feature: production-readiness, Property 2: Log entry round-trip
    ///
    /// Property 2: Log entry round-trip.
    /// For any valid LogEvent instance, formatting it with HtmlLogEventFormatter and then
    /// parsing the resulting string as a JSON object SHALL produce an object containing
    /// the original log level, UTC timestamp, and rendered message.
    ///
    /// **Validates: Requirements 5.3, 11.4**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(LogEventArbitraries) })]
    public bool FormatterRoundTrip(LogEvent logEvent)
    {
        var sw = new StringWriter();
        Formatter.Format(logEvent, sw);
        var output = sw.ToString();

        // The formatter appends a trailing comma and newline: {...},\n
        // Trim to get valid JSON
        var json = output.TrimEnd('\n', '\r', ',');

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Verify log level
        var expectedLevel = logEvent.Level.ToString();
        if (root.GetProperty("type").GetString() != expectedLevel)
            return false;

        // Verify UTC timestamp
        var expectedTime = logEvent.Timestamp.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        if (root.GetProperty("time").GetString() != expectedTime)
            return false;

        // Verify rendered message
        var expectedMessage = logEvent.RenderMessage();
        if (logEvent.Exception != null)
        {
            expectedMessage = expectedMessage.Length > 0
                ? expectedMessage + "\n" + logEvent.Exception
                : logEvent.Exception.ToString();
        }

        var actualMsg = root.GetProperty("msg").GetString();
        if (actualMsg != expectedMessage)
            return false;

        return true;
    }

    /// <summary>
    /// Verifies that all keys in a JS object literal content string are double-quoted.
    /// </summary>
    private static bool VerifyAllKeysQuoted(string content)
    {
        var i = 0;
        while (i < content.Length)
        {
            while (i < content.Length && char.IsWhiteSpace(content[i])) i++;
            if (i >= content.Length) break;

            // We expect a quoted key here
            if (content[i] != '"') return false;

            // Skip past the quoted key
            i++;
            while (i < content.Length && content[i] != '"')
            {
                if (content[i] == '\\') i++;
                i++;
            }
            if (i >= content.Length) return false;
            i++;

            // Expect colon
            while (i < content.Length && char.IsWhiteSpace(content[i])) i++;
            if (i >= content.Length || content[i] != ':') return false;
            i++;

            // Skip the value
            i = SkipValue(content, i);

            // Skip comma if present
            while (i < content.Length && char.IsWhiteSpace(content[i])) i++;
            if (i < content.Length && content[i] == ',') i++;
        }

        return true;
    }

    private static int SkipValue(string content, int i)
    {
        while (i < content.Length && char.IsWhiteSpace(content[i])) i++;
        if (i >= content.Length) return i;

        if (content[i] == '"')
        {
            i++;
            while (i < content.Length && content[i] != '"')
            {
                if (content[i] == '\\') i++;
                i++;
            }
            if (i < content.Length) i++;
        }
        else if (content[i] == '{')
        {
            var depth = 1;
            i++;
            while (i < content.Length && depth > 0)
            {
                if (content[i] == '{') depth++;
                else if (content[i] == '}') depth--;
                else if (content[i] == '"')
                {
                    i++;
                    while (i < content.Length && content[i] != '"')
                    {
                        if (content[i] == '\\') i++;
                        i++;
                    }
                }
                i++;
            }
        }
        else
        {
            while (i < content.Length && content[i] != ',' && content[i] != '}')
                i++;
        }

        return i;
    }
}

/// <summary>
/// Custom Arbitrary provider for LogEvent that generates random log events with varied
/// property keys including hyphens, @ prefixes, digits, spaces, and other non-identifier characters.
/// </summary>
public class LogEventArbitraries
{
    private static readonly LogEventLevel[] AllLevels =
    {
        LogEventLevel.Verbose,
        LogEventLevel.Debug,
        LogEventLevel.Information,
        LogEventLevel.Warning,
        LogEventLevel.Error,
        LogEventLevel.Fatal
    };

    public static Arbitrary<LogEvent> LogEventArbitrary()
    {
        var keyGen = Gen.OneOf(
            Gen.Elements("Name", "Count", "Path", "Value", "Status"),
            Gen.Elements("Content-Type", "X-Request-Id", "Accept-Encoding", "Cache-Control"),
            Gen.Elements("@RequestId", "@Timestamp", "@Level", "@Message"),
            Gen.Elements("123Count", "0Index", "42Answer", "1stPlace"),
            Gen.Elements("User Name", "Log Level", "Request Path", "Time Stamp"),
            Gen.Elements("key.with.dots", "key/with/slashes", "key+plus", "key=equals")
        );

        var valueGen = Gen.OneOf(
            Gen.Elements("hello", "world", "test", "/api/data", "foo bar")
                .Select(s => (LogEventPropertyValue)new ScalarValue(s)),
            Gen.Choose(-1000, 1000)
                .Select(n => (LogEventPropertyValue)new ScalarValue(n)),
            Gen.Elements(true, false)
                .Select(b => (LogEventPropertyValue)new ScalarValue(b)),
            Gen.Constant((LogEventPropertyValue)new ScalarValue(null))
        );

        var propertyGen = keyGen.SelectMany(
            key => valueGen,
            (key, value) => new LogEventProperty(key, value));

        var propsGen = Gen.Choose(0, 10).SelectMany(count =>
            count == 0
                ? Gen.Constant(new List<LogEventProperty>())
                : Gen.ListOf(propertyGen, count)
                    .Select(lst => new List<LogEventProperty>(lst)));

        var levelGen = Gen.Elements(AllLevels);
        var messageGen = Gen.Elements(
            "Request received", "Processing complete", "",
            "Error occurred", "User logged in", "Connection timeout");

        var timestampGen =
            from year in Gen.Choose(2020, 2030)
            from month in Gen.Choose(1, 12)
            from day in Gen.Choose(1, 28)
            from hour in Gen.Choose(0, 23)
            from minute in Gen.Choose(0, 59)
            from second in Gen.Choose(0, 59)
            select new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero);

        var logEventGen =
            from level in levelGen
            from message in messageGen
            from timestamp in timestampGen
            from props in propsGen
            select CreateLogEvent(level, message, timestamp, props);

        return logEventGen.ToArbitrary();
    }

    private static LogEvent CreateLogEvent(
        LogEventLevel level,
        string message,
        DateTimeOffset timestamp,
        List<LogEventProperty> properties)
    {
        var tokens = new List<MessageTemplateToken> { new TextToken(message) };
        var template = new MessageTemplate(tokens);
        return new LogEvent(timestamp, level, null, template, properties);
    }
}
