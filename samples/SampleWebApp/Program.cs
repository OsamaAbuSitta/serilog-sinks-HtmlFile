using Serilog;
using Serilog.Events;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .WriteTo.HtmlFile(
        path:"logs/log.html",
        fileNamingPattern: "logs/log-{Date}.html",
        restrictedToMinimumLevel: LogEventLevel.Debug,
        fileSizeLimitBytes: 5 * 1024 * 1024) // 5 MB rolling
    // ---------------------------------------------------------------
    // Alternative: Custom archive naming pattern
    // Controls how rolled archive files are named when the size limit
    // is reached. Default pattern is "{BaseName}_{Timestamp}{Extension}".
    //
    // .WriteTo.HtmlFile(
    //     path: "logs/app.html",
    //     restrictedToMinimumLevel: LogEventLevel.Debug,
    //     fileSizeLimitBytes: 5 * 1024 * 1024,
    //     archiveNamingPattern: "{BaseName}-{Timestamp}{Extension}",
    //     archiveTimestampFormat: "yyyy-MM-dd_HHmmss")
    //
    // ---------------------------------------------------------------
    // Alternative: Date-based active file naming pattern
    // The {Date} placeholder is evaluated at sink construction and
    // re-evaluated on each roll, producing date-partitioned log files.
    //
    // .WriteTo.HtmlFile(
    //     path: "logs/app.html",
    //     restrictedToMinimumLevel: LogEventLevel.Debug,
    //     fileSizeLimitBytes: 5 * 1024 * 1024,
    //     fileNamingPattern: "logs/app-{Date}.html")
    //
    // ---------------------------------------------------------------
    .CreateLogger();

try
{
    Log.Information("Starting SampleWebApp");

    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();

    var app = builder.Build();

    app.MapGet("/", () =>
    {
        Log.Information("Home endpoint hit");
        return "Hello! Check logs/app.html for the log viewer.";
    });

    app.MapGet("/warn", () =>
    {
        Log.Warning("This is a warning from {Endpoint}", "/warn");
        return "Warning logged.";
    });

    app.MapGet("/error", () =>
    {
        try
        {
            throw new InvalidOperationException("Something went wrong!");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Caught exception at {Endpoint}", "/error");
        }
        return "Error logged.";
    });

    app.MapGet("/flood", () =>
    {
        for (var i = 0; i < 50; i++)
        {
            var level = i % 5;
            switch (level)
            {
                case 0: Log.Verbose("Flood entry {Index}", i); break;
                case 1: Log.Debug("Flood entry {Index} with {Detail}", i, "debug-info"); break;
                case 2: Log.Information("Flood entry {Index}", i); break;
                case 3: Log.Warning("Flood entry {Index}", i); break;
                case 4: Log.Error("Flood entry {Index}", i); break;
            }
        }
        return "50 log entries written. Open logs/app.html to browse them.";
    });

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
