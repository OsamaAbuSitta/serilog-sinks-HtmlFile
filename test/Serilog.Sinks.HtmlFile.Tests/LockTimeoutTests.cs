using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Parsing;

namespace Serilog.Sinks.HtmlFile.Tests;

[Collection("SelfLog")]
public class LockTimeoutTests : IDisposable
{
    private readonly string _tempDir;
    private readonly HtmlLogEventFormatter _formatter = new();
    private readonly HtmlTemplate _template = new();
    private readonly Encoding _encoding = new UTF8Encoding(false);

    public LockTimeoutTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "LockTimeoutTests_" + Guid.NewGuid().ToString("N"));
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

    private static LogEvent CreateEvent(string message = "Hello")
    {
        var tokens = new List<MessageTemplateToken> { new TextToken(message) };
        var template = new MessageTemplate(tokens);
        return new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            null,
            template,
            Array.Empty<LogEventProperty>());
    }

    private static object GetSyncRoot(HtmlFileSink sink)
    {
        var field = typeof(HtmlFileSink).GetField("_syncRoot", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? throw new InvalidOperationException("Could not find _syncRoot field on HtmlFileSink");
        return field.GetValue(sink)
               ?? throw new InvalidOperationException("_syncRoot field value is null");
    }

    /// <summary>
    /// Validates: Requirement 13.1 — When a background thread holds the HtmlFileSink lock
    /// and a second thread attempts to emit a log event, the event is dropped after timeout.
    /// </summary>
    [Fact]
    public void Emit_WhenLockHeld_DropsEventAfterTimeout()
    {
        var path = Path.Combine(_tempDir, "lock-timeout.html");

        using var sink = new HtmlFileSink(path, _formatter, _template, null, _encoding);

        // Write one event first so we know the file works
        sink.Emit(CreateEvent("before-lock"));

        var syncRoot = GetSyncRoot(sink);
        var lockAcquired = new ManualResetEventSlim(false);
        var emitFinished = new ManualResetEventSlim(false);

        // Background thread: hold the lock for longer than the 5-second timeout
        var lockHolder = new Thread(() =>
        {
            Monitor.Enter(syncRoot);
            try
            {
                lockAcquired.Set();
                // Hold lock until the emit thread finishes (it will time out after 5s)
                emitFinished.Wait(TimeSpan.FromSeconds(30));
            }
            finally
            {
                Monitor.Exit(syncRoot);
            }
        });
        lockHolder.IsBackground = true;
        lockHolder.Start();

        // Wait for the background thread to acquire the lock
        lockAcquired.Wait(TimeSpan.FromSeconds(5));

        // Emit from the current thread — this should time out and drop the event
        sink.Emit(CreateEvent("dropped-event"));
        emitFinished.Set();

        lockHolder.Join(TimeSpan.FromSeconds(10));

        // Dispose the sink to flush
        sink.Dispose();

        var content = File.ReadAllText(path);
        Assert.Contains("before-lock", content);
        Assert.DoesNotContain("dropped-event", content);
    }

    /// <summary>
    /// Validates: Requirement 13.2 — When a log event is dropped due to lock timeout,
    /// a warning is written to Serilog SelfLog.
    /// </summary>
    [Fact]
    public void Emit_WhenLockHeld_WritesSelfLogWarning()
    {
        var path = Path.Combine(_tempDir, "lock-selflog.html");
        var selfLogMessages = new List<string>();

        using var sink = new HtmlFileSink(path, _formatter, _template, null, _encoding);

        var syncRoot = GetSyncRoot(sink);
        var lockAcquired = new ManualResetEventSlim(false);
        var emitFinished = new ManualResetEventSlim(false);

        SelfLog.Enable(msg => selfLogMessages.Add(msg));
        try
        {
            // Background thread: hold the lock
            var lockHolder = new Thread(() =>
            {
                Monitor.Enter(syncRoot);
                try
                {
                    lockAcquired.Set();
                    emitFinished.Wait(TimeSpan.FromSeconds(30));
                }
                finally
                {
                    Monitor.Exit(syncRoot);
                }
            });
            lockHolder.IsBackground = true;
            lockHolder.Start();

            lockAcquired.Wait(TimeSpan.FromSeconds(5));

            // Emit — will time out after 5 seconds
            sink.Emit(CreateEvent("will-be-dropped"));
            emitFinished.Set();

            lockHolder.Join(TimeSpan.FromSeconds(10));
        }
        finally
        {
            SelfLog.Disable();
        }

        // Verify SelfLog received a warning about the dropped event
        Assert.Contains(selfLogMessages, m =>
            m.Contains("lock", StringComparison.OrdinalIgnoreCase) &&
            m.Contains("dropping", StringComparison.OrdinalIgnoreCase));
    }
}
