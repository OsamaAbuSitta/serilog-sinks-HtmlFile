# Serilog.Sinks.HtmlFile

[![Build Status](https://github.com/OsamaAbuSitta/serilog-sinks-HtmlFile/actions/workflows/ci-cd.yml/badge.svg)](https://github.com/OsamaAbuSitta/serilog-sinks-HtmlFile/actions/workflows/ci-cd.yml)
[![NuGet](https://img.shields.io/nuget/v/Serilog.Sinks.HtmlFile.svg)](https://www.nuget.org/packages/Serilog.Sinks.HtmlFile)
![Built with AI](https://img.shields.io/badge/Built%20with-AI-orange)

A Serilog sink that writes log events to self-contained interactive HTML files. Each log file includes an embedded viewer with filtering, search, and level highlighting — no external dependencies or server required. Just open the `logs.html` file in a browser.

<a href="https://osamaabusitta.github.io/serilog-sinks-HtmlFile/demo.html" target="_blank">Live Demo</a> - see the interactive log viewer in action.

## NuGet Package

Available on NuGet: <a href="https://www.nuget.org/packages/Serilog.Sinks.HtmlFile" target="_blank">Serilog.Sinks.HtmlFile</a>

```shell
dotnet add package Serilog.Sinks.HtmlFile
```

## Quick Start

```csharp
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.HtmlFile("logs/app.html")
    .CreateLogger();

Log.Information("Hello, HTML logs!");
Log.CloseAndFlush();
```

## Full Configuration

All parameters with their default values:

```csharp
Log.Logger = new LoggerConfiguration()
    .WriteTo.HtmlFile(
        path: "logs/app.html",                                    // Required. Absolute path recommended; relative paths resolve under the current working directory.
        restrictedToMinimumLevel: LogEventLevel.Verbose,          // Default: LevelAlias.Minimum (Verbose)
        fileSizeLimitBytes: 1073741824L,                          // Default: 1 GB. Pass null for unlimited growth.
        encoding: null,                                           // Default: UTF-8 without BOM
        customTemplatePath: null,                                 // Default: null (uses built-in template)
        formatter: null,                                          // Default: null (uses HtmlLogEventFormatter)
        archiveNamingPattern: "{BaseName}_{Timestamp}{Extension}",// Default archive naming pattern
        archiveTimestampFormat: "yyyyMMddHHmmss",                 // Default timestamp format for archives
        fileNamingPattern: null,                                  // Default: null (uses path as-is)
        dateFormat: "yyyy-MM-dd")                                 // Default date format for {Date} placeholder
    .CreateLogger();
```

## JSON Configuration

The sink can also be configured via `appsettings.json` using [Serilog.Settings.Configuration](https://github.com/serilog/serilog-settings-configuration):

```shell
dotnet add package Serilog.Settings.Configuration
```

```json
{
  "Serilog": {
    "Using": ["Serilog.Sinks.HtmlFile"],
    "WriteTo": [
      {
        "Name": "HtmlFile",
        "Args": {
          "path": "logs/app-{Date}.html"
        }
      }
    ]
  }
}
```

Then in your startup code:

```csharp
var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .Build();

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(configuration)
    .CreateLogger();
```

## Size-Based Rolling and Archive Naming

When `fileSizeLimitBytes` is set (default: 1 GB), the sink automatically rolls the log file once it reaches the size limit. The current file is renamed to an archive using the `archiveNamingPattern`, and a new active file is created.

The default archive naming pattern is `{BaseName}_{Timestamp}{Extension}`, which produces names like:

```
app_20250115143022.html
```

Available placeholders for `archiveNamingPattern`:

| Placeholder     | Description                                      |
|-----------------|--------------------------------------------------|
| `{BaseName}`    | The file name without extension (e.g., `app`)    |
| `{Timestamp}`   | UTC timestamp formatted by `archiveTimestampFormat` |
| `{Extension}`   | The file extension including the dot (e.g., `.html`) |

The `archiveTimestampFormat` controls how the `{Timestamp}` placeholder is formatted. The default is `yyyyMMddHHmmss`.

You can also use date-based active file naming with the `fileNamingPattern` parameter. The `{Date}` placeholder is replaced with the current date (formatted by `dateFormat`, default `yyyy-MM-dd`), and `{MachineName}` is replaced with the machine name:

```csharp
.WriteTo.HtmlFile(
    path: "logs/app.html",
    fileNamingPattern: "logs/app-{Date}.html",
    fileSizeLimitBytes: 5 * 1024 * 1024)
```

This produces files like `logs/app-2025-01-15.html`.

Set `fileSizeLimitBytes` to `null` to disable rolling and allow unlimited file growth.

## Custom Templates

You can replace the built-in HTML template with your own by providing a path to a custom `.html` file via the `customTemplatePath` parameter.

### Creating a Custom Template

Your custom template must be an `.html` file containing exactly one `<!-- LOG_ENTRIES_PLACEHOLDER -->` comment marker. The sink splits the file at this marker: everything before it becomes the header, and everything after becomes the tail. Log entries are inserted between the two.

Here is a minimal example:

```html
<!DOCTYPE html>
<html>
<head>
    <title>My Custom Log Viewer</title>
    <style>
        body { font-family: monospace; background: #1e1e1e; color: #d4d4d4; }
        .log-entry { padding: 2px 8px; border-bottom: 1px solid #333; }
    </style>
</head>
<body>
    <h1>Application Logs</h1>
    <div id="log-container">
        <script>var logEntries = [
<!-- LOG_ENTRIES_PLACEHOLDER -->
        ];</script>
    </div>
    <script>
        // Your custom viewer logic here
        logEntries.forEach(function(entry) {
            var div = document.createElement('div');
            div.className = 'log-entry';
            div.textContent = entry.time + ' [' + entry.type + '] ' + entry.msg;
            document.getElementById('log-container').appendChild(div);
        });
    </script>
</body>
</html>
```

### Using a Custom Template

```csharp
.WriteTo.HtmlFile(
    path: "logs/app.html",
    customTemplatePath: "templates/my-template.html")
```

When using a custom template, the default viewer scripts are not included. Your template should contain its own CSS and JavaScript for rendering and interacting with the log entries.

## Sample Application

See [`samples/SampleWebApp`](samples/SampleWebApp) for a working ASP.NET Core example that demonstrates the sink with date-based file naming and size-based rolling.

The sample includes a `/demo` endpoint that generates log entries covering all log levels, structured properties, exceptions, special characters, Unicode, and simulated API traffic — useful for exploring the full capabilities of the HTML log viewer.

## License

This project is licensed under the [Apache License 2.0](LICENSE).
