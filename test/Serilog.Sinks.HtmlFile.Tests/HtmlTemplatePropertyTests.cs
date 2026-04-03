using System;
using System.IO;
using System.Linq;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace Serilog.Sinks.HtmlFile.Tests;

/// <summary>
/// Feature: embedded-html-template, Property 1: Template split round-trip
/// Feature: embedded-html-template, Property 2: WriteHeader/WriteTail framing
/// Feature: embedded-html-template, Property 3: Missing marker rejection
/// Feature: embedded-html-template, Property 4: Non-existent file path rejection
/// Validates: Requirements 2.4, 2.6, 2.7, 5.1, 5.2, 7.1
/// </summary>
public class HtmlTemplatePropertyTests : IDisposable
{
    private const string PlaceholderMarker = "<!-- LOG_ENTRIES_PLACEHOLDER -->";

    private readonly string _tempDir;

    public HtmlTemplatePropertyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "HtmlTemplatePropTests_" + Guid.NewGuid().ToString("N"));
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
    /// Property 1: Template split round-trip.
    /// For any two arbitrary non-empty strings (neither containing the placeholder marker),
    /// concatenating them with the marker in between, writing to a temp file, constructing
    /// HtmlTemplate(path), then capturing WriteHeader + WriteTail output, the combined output
    /// should contain the original header content, "var logEntries = [\n" framing,
    /// "// __LOG_ENTRIES_END__\n];\n" framing, and original tail content in order.
    ///
    /// **Validates: Requirements 2.4, 7.1**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(TemplatePairArbitraries) })]
    public bool TemplateSplitRoundTrip_PreservesContentWithFraming(TemplatePair pair)
    {
        var headerContent = pair.Header;
        var tailContent = pair.Tail;

        // Build template file: header + marker + tail
        var templateContent = headerContent + PlaceholderMarker + tailContent;
        var filePath = Path.Combine(_tempDir, Guid.NewGuid().ToString("N") + ".html");
        File.WriteAllText(filePath, templateContent);

        // Construct HtmlTemplate from the file
        var template = new HtmlTemplate(filePath);

        // Capture WriteHeader output
        var headerWriter = new StringWriter();
        template.WriteHeader(headerWriter);
        var headerOutput = headerWriter.ToString();

        // Capture WriteTail output
        var tailWriter = new StringWriter();
        template.WriteTail(tailWriter);
        var tailOutput = tailWriter.ToString();

        // Combined output
        var combined = headerOutput + tailOutput;

        // Expected combined output: headerContent + "var logEntries = [\n" + "// __LOG_ENTRIES_END__\n" + "];\n" + tailContent
        var expected = headerContent + "var logEntries = [\n" + "// __LOG_ENTRIES_END__\n" + "];\n" + tailContent;

        return combined == expected;
    }

    /// <summary>
    /// Property 2: WriteHeader/WriteTail framing.
    /// For any two arbitrary non-empty strings (neither containing the placeholder marker),
    /// constructing a valid template and an HtmlTemplate from it, the WriteHeader output
    /// must end with "var logEntries = [\n" and the WriteTail output must start with
    /// "// __LOG_ENTRIES_END__\n".
    ///
    /// **Validates: Requirements 5.1, 5.2**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(TemplatePairArbitraries) })]
    public bool WriteHeaderWriteTail_FramingIsCorrect(TemplatePair pair)
    {
        // Build template file: header + marker + tail
        var templateContent = pair.Header + PlaceholderMarker + pair.Tail;
        var filePath = Path.Combine(_tempDir, Guid.NewGuid().ToString("N") + ".html");
        File.WriteAllText(filePath, templateContent);

        var template = new HtmlTemplate(filePath);

        // Capture WriteHeader output
        var headerWriter = new StringWriter();
        template.WriteHeader(headerWriter);
        var headerOutput = headerWriter.ToString();

        // Capture WriteTail output
        var tailWriter = new StringWriter();
        template.WriteTail(tailWriter);
        var tailOutput = tailWriter.ToString();

        // WriteHeader must end with "var logEntries = [\n"
        var headerEndsCorrectly = headerOutput.EndsWith("var logEntries = [\n");

        // WriteTail must start with "// __LOG_ENTRIES_END__\n"
        var tailStartsCorrectly = tailOutput.StartsWith("// __LOG_ENTRIES_END__\n");

        return headerEndsCorrectly && tailStartsCorrectly;
    }

    /// <summary>
    /// Property 3: Missing marker rejection.
    /// For any string that does not contain the placeholder marker,
    /// constructing an HtmlTemplate from that content (via a temp file)
    /// should throw an InvalidOperationException.
    /// Edge cases: empty strings, partial marker matches, similar HTML comments.
    ///
    /// **Validates: Requirements 2.7**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(NoMarkerStringArbitraries) })]
    public bool MissingMarker_ThrowsInvalidOperationException(NoMarkerString input)
    {
        var filePath = Path.Combine(_tempDir, Guid.NewGuid().ToString("N") + ".html");
        File.WriteAllText(filePath, input.Value);

        try
        {
            _ = new HtmlTemplate(filePath);
            return false; // Should have thrown
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    /// <summary>
    /// Property 4: Non-existent file path rejection.
    /// For any file path that does not exist on disk,
    /// constructing new HtmlTemplate(path) should throw a FileNotFoundException.
    ///
    /// **Validates: Requirements 2.6**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(NonExistentPathArbitraries) })]
    public bool NonExistentFilePath_ThrowsFileNotFoundException(NonExistentPath input)
    {
        try
        {
            _ = new HtmlTemplate(input.Value);
            return false; // Should have thrown
        }
        catch (FileNotFoundException)
        {
            return true;
        }
    }
}

/// <summary>
/// Wrapper type for a pair of non-empty strings that do not contain the placeholder marker.
/// </summary>
public class TemplatePair
{
    public string Header { get; }
    public string Tail { get; }

    public TemplatePair(string header, string tail)
    {
        Header = header;
        Tail = tail;
    }

    public override string ToString() =>
        $"Header=({Header.Length} chars), Tail=({Tail.Length} chars)";
}

/// <summary>
/// Custom Arbitrary provider that generates pairs of non-empty strings,
/// neither containing the placeholder marker.
/// </summary>
public class TemplatePairArbitraries
{
    private const string PlaceholderMarker = "<!-- LOG_ENTRIES_PLACEHOLDER -->";

    private static readonly char[] SafeChars =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 \t\n<>/{}[]()=:;.,!?-_+#@$%^&*"
            .ToCharArray();

    public static Arbitrary<TemplatePair> TemplatePairArbitrary()
    {
        var charGen = Gen.Elements(SafeChars);

        var safeStringGen =
            from len in Gen.Choose(1, 200)
            from chars in Gen.ListOf<char>(charGen, len)
            let s = new string(chars.ToArray())
            where !s.Contains(PlaceholderMarker)
            select s;

        var pairGen =
            from header in safeStringGen
            from tail in safeStringGen
            select new TemplatePair(header, tail);

        return pairGen.ToArbitrary();
    }
}

/// <summary>
/// Wrapper type for a string that does not contain the placeholder marker.
/// Includes empty strings, partial marker matches, and similar HTML comments.
/// </summary>
public class NoMarkerString
{
    public string Value { get; }

    public NoMarkerString(string value)
    {
        Value = value;
    }

    public override string ToString() =>
        $"NoMarkerString({Value.Length} chars): \"{(Value.Length <= 80 ? Value : Value.Substring(0, 80) + "...")}\"";
}

/// <summary>
/// Custom Arbitrary provider that generates strings not containing the placeholder marker.
/// Includes edge cases: empty strings, partial marker matches, and similar HTML comments.
/// </summary>
public class NoMarkerStringArbitraries
{
    private const string PlaceholderMarker = "<!-- LOG_ENTRIES_PLACEHOLDER -->";

    private static readonly char[] SafeChars =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 \t\n<>/{}[]()=:;.,!?-_+#@$%^&*"
            .ToCharArray();

    /// <summary>
    /// Generates strings that never contain the full placeholder marker.
    /// Mixes random safe strings with edge-case strings (empty, partial markers, similar comments).
    /// </summary>
    public static Arbitrary<NoMarkerString> NoMarkerStringArbitrary()
    {
        var charGen = Gen.Elements(SafeChars);

        // Random strings (including empty) that don't contain the marker
        var randomStringGen =
            from len in Gen.Choose(0, 300)
            from chars in Gen.ListOf<char>(charGen, len)
            let s = new string(chars.ToArray())
            where !s.Contains(PlaceholderMarker)
            select s;

        // Edge-case strings: partial markers, similar comments, near-misses
        var edgeCases = new[]
        {
            "",                                              // empty string
            "<!-- LOG_ENTRIES -->",                           // similar but different comment
            "<!-- LOG_ENTRIES_PLACEHOLDER",                   // missing closing -->
            "LOG_ENTRIES_PLACEHOLDER -->",                    // missing opening <!--
            "<!-- LOG_ENTRIES_PLACEHOLDER -- >",              // extra space before >
            "<!--LOG_ENTRIES_PLACEHOLDER-->",                 // no spaces around marker text
            "<!-- log_entries_placeholder -->",               // lowercase variant
            "<html><body><!-- not the marker --></body></html>", // valid HTML with different comment
            "<!-- LOG_ENTRIES_PLACEHOLDER -->\n<!-- LOG_ENTRIES_PLACEHOLDER -->".Replace(PlaceholderMarker, "NOPE"), // replaced marker
            "some content before <!-- LOG_ENTRIES partial",  // partial match in context
        };

        var edgeCaseGen = Gen.Elements(edgeCases);

        // Mix: 80% random strings, 20% edge cases
        var combinedGen = Gen.Frequency(
            (4, randomStringGen),
            (1, edgeCaseGen)
        );

        return combinedGen.Select(s => new NoMarkerString(s)).ToArbitrary();
    }
}


/// <summary>
/// Wrapper type for a file path string that does not exist on disk.
/// </summary>
public class NonExistentPath
{
    public string Value { get; }

    public NonExistentPath(string value)
    {
        Value = value;
    }

    public override string ToString() =>
        $"NonExistentPath: \"{Value}\"";
}

/// <summary>
/// Custom Arbitrary provider that generates file path strings guaranteed not to exist on disk.
/// Uses Path.Combine with a non-existent GUID-based directory under the temp path.
/// </summary>
public class NonExistentPathArbitraries
{
    private static readonly char[] FileNameChars =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-".ToCharArray();

    public static Arbitrary<NonExistentPath> NonExistentPathArbitrary()
    {
        var charGen = Gen.Elements(FileNameChars);

        var fileNameGen =
            from len in Gen.Choose(1, 30)
            from chars in Gen.ListOf<char>(charGen, len)
            select new string(chars.ToArray());

        var extensionGen = Gen.Elements(".html", ".htm", ".txt", ".template", ".log");

        var pathGen =
            from fileName in fileNameGen
            from ext in extensionGen
            let nonExistentDir = Path.Combine(Path.GetTempPath(), "nonexistent_" + Guid.NewGuid().ToString("N"))
            let fullPath = Path.Combine(nonExistentDir, fileName + ext)
            where !File.Exists(fullPath)
            select new NonExistentPath(fullPath);

        return pathGen.ToArbitrary();
    }
}
