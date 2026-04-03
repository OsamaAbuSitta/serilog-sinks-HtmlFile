using System;
using System.IO;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace Serilog.Sinks.HtmlFile.Tests;

/// <summary>
/// Feature: production-readiness, Property 5: Invalid fileSizeLimitBytes throws ArgumentException
/// Validates: Requirements 16.2
/// </summary>
public class ConfigurationPropertyTests : IDisposable
{
    private readonly string _tempDir;

    public ConfigurationPropertyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ConfigPropTests_" + Guid.NewGuid().ToString("N"));
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
    /// Property 5: Invalid fileSizeLimitBytes throws ArgumentException.
    /// For any non-null long value less than 1 (including 0 and negative values),
    /// calling WriteTo.HtmlFile() SHALL throw an ArgumentException.
    ///
    /// **Validates: Requirements 16.2**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(InvalidFileSizeLimitArbitraries) })]
    public bool InvalidFileSizeLimitBytes_ThrowsArgumentException(long invalidLimit)
    {
        var path = Path.Combine(_tempDir, $"test-{Guid.NewGuid():N}.html");

        try
        {
            new LoggerConfiguration()
                .WriteTo.HtmlFile(path, fileSizeLimitBytes: invalidLimit);
            return false; // Should have thrown
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch
        {
            return false; // Wrong exception type
        }
    }
}

/// <summary>
/// Custom Arbitrary provider that generates non-null long values less than 1
/// (0 and negative values) for testing fileSizeLimitBytes validation.
/// </summary>
public class InvalidFileSizeLimitArbitraries
{
    public static Arbitrary<long> LongArbitrary()
    {
        var gen = Gen.OneOf(
            Gen.Constant(0L),
            Gen.Constant(long.MinValue),
            Gen.Constant(-1L),
            Gen.Choose(int.MinValue, -1).Select(i => (long)i),
            Gen.Choose(-1000, -1).Select(i => (long)i)
        );
        return gen.ToArbitrary();
    }
}
