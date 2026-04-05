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

    app.MapGet("/demo", () =>
    {
        // --- All log levels ---
        Log.Verbose("Verbose: Low-level trace for diagnosing internal state");
        Log.Debug("Debug: Variable {Name} resolved to {Value}", "connectionString", "Server=db01;Database=app");
        Log.Information("Information: Application started successfully on {MachineName}", Environment.MachineName);
        Log.Warning("Warning: Cache miss rate is {Rate}% — above the {Threshold}% threshold", 87.5, 75);
        Log.Error("Error: Failed to connect to payment gateway after {Retries} retries", 3);
        Log.Fatal("Fatal: Unrecoverable error — shutting down the order processing pipeline");

        // --- Structured properties: scalars, booleans, numbers ---
        Log.Information("User {UserId} authenticated via {Provider} (admin={IsAdmin})", 42, "OAuth2", true);
        Log.Information("Request completed in {ElapsedMs}ms with status {StatusCode}", 237, 200);
        Log.Debug("Config loaded: MaxRetries={MaxRetries}, Timeout={TimeoutSec}s, Enabled={Enabled}",
            5, 30.0, false);

        // --- Structured properties: objects and collections ---
        Log.Information("Order {OrderId} placed by {Customer} for {Items}",
            "ORD-98712",
            new { Name = "Alice Johnson", Email = "alice@example.com" },
            new[] { "Widget A", "Widget B", "Gadget X" });
        Log.Debug("Request headers: {@Headers}",
            new Dictionary<string, string>
            {
                ["Content-Type"] = "application/json",
                ["Authorization"] = "Bearer ***",
                ["X-Request-Id"] = Guid.NewGuid().ToString()
            });

        // --- Exception with stack trace ---
        try
        {
            int.Parse("not-a-number");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to parse input value {Input}", "not-a-number");
        }

        // --- Nested exception ---
        try
        {
            try
            {
                throw new InvalidOperationException("Database connection pool exhausted");
            }
            catch (Exception inner)
            {
                throw new ApplicationException("Order service unavailable", inner);
            }
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Critical failure in {Service}", "OrderService");
        }

        // --- Long message for search/scroll testing ---
        Log.Information(
            "Batch import completed: processed {Total} records, {Succeeded} succeeded, " +
            "{Failed} failed, {Skipped} skipped. Source: {Source}, Duration: {Duration}",
            10_000, 9_842, 103, 55, "/data/import/customers_2026.csv", TimeSpan.FromMinutes(4.3));

        // --- Special characters (HTML/JS escaping) ---
        Log.Warning("User input contained suspicious content: {Input}",
            "<script>alert('xss')</script> & \"quotes\" & <b>bold</b>");
        Log.Debug("File path with backslashes: {Path}", @"C:\Users\admin\Documents\report.pdf");
        Log.Information("Multi-line note:\nLine 1: Hello\nLine 2: World\n\tIndented line");

        // --- Unicode / multi-byte characters ---
        Log.Information("International greeting: {Greeting} from {City}",
            "こんにちは世界", "東京");
        Log.Debug("Emoji status: {Status} {Icon}", "All systems operational", "🟢✅🚀");

        // --- Null and empty values ---
        Log.Warning("Optional field is missing: UserId={UserId}, SessionId={SessionId}",
            (string?)null, "");

        // --- High-cardinality properties for filtering ---
        var endpoints = new[] { "/api/users", "/api/orders", "/api/products", "/api/health" };
        var methods = new[] { "GET", "POST", "PUT", "DELETE" };
        var statusCodes = new[] { 200, 201, 204, 400, 401, 403, 404, 500 };
        var random = new Random(42);
        for (var i = 0; i < 30; i++)
        {
            var endpoint = endpoints[random.Next(endpoints.Length)];
            var method = methods[random.Next(methods.Length)];
            var status = statusCodes[random.Next(statusCodes.Length)];
            var elapsed = random.Next(5, 2000);
            var level = status >= 500 ? LogEventLevel.Error
                      : status >= 400 ? LogEventLevel.Warning
                      : LogEventLevel.Information;

            Log.Write(level,
                "{Method} {Endpoint} responded {StatusCode} in {ElapsedMs}ms",
                method, endpoint, status, elapsed);
        }

        return "Demo log entries written with all levels, properties, exceptions, "
             + "special characters, and API simulation. Open the HTML log file to browse them.";
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
