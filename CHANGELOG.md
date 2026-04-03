# Changelog

All notable changes to this project will be documented in this file.

## 1.0.0

Initial release of Serilog.Sinks.HtmlFile — a Serilog sink that writes log events to self-contained interactive HTML files with an embedded log viewer.

### Features

- HTML file sink with embedded interactive log viewer (filter, search, sort, expand details, light/dark theme)
- Rolling file support with configurable size limits and archive naming patterns
- Customizable HTML templates via `customTemplatePath` parameter
- Unified `HtmlTemplate` class loading from embedded resource (default) or user-provided file
- File naming patterns with `{Date}` and `{MachineName}` placeholders
- Thread-safe concurrent writes with lock timeout and graceful event dropping
- Nullable reference types enabled across the library
- `netstandard2.0` target for broad compatibility

### Packaging

- Complete NuGet package metadata
- Source Link with `Microsoft.SourceLink.GitHub` for debugger source stepping
- Deterministic and reproducible builds
- Symbol package (`.snupkg`) included

### Documentation

- `README.md` with usage examples, parameter reference, rolling behavior docs, and custom template guide
- XML doc comments across public API surface

### CI/CD

- GitHub Actions workflow for automated build, test, pack on push/PR and NuGet publish on version tags

### Testing

- Property-based tests using FsCheck for template splitting, framing, marker rejection, and formatter correctness
- Integration, end-to-end, lock-timeout, and backward compatibility tests
