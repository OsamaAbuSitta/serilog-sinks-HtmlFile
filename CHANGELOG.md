# Changelog

All notable changes to this project will be documented in this file.

## 1.0.0

### Bug Fixes

- Quoted all JavaScript property keys in `HtmlLogEventFormatter` to produce valid JS object literals for keys containing hyphens, `@` prefixes, digits, and other non-identifier characters
- Fixed sample app date pattern from `{date}` to `{Date}` so date-based file naming works correctly

### API Hardening

- Marked `HtmlFileSink` and `RollingHtmlFileSink` as `internal` to reduce public API surface, following `serilog-sinks-file` conventions
- Enabled nullable reference types across the library
- Aligned `fileSizeLimitBytes` default to 1 GB (matching `serilog-sinks-file`) and added validation rejecting values less than 1

### Packaging

- Added complete NuGet package metadata (`PackageId`, `Description`, `PackageTags`, `PackageLicenseExpression`, etc.)
- Enabled Source Link with `Microsoft.SourceLink.GitHub` for debugger source stepping
- Enabled deterministic and reproducible builds
- Included symbol package (`.snupkg`) for improved debugging experience

### Documentation

- Added `README.md` with usage examples, parameter reference, rolling behavior docs, and custom template guide
- Documented `IHtmlTemplate.InsertionMarker` contract in XML doc comments
- Improved XML doc comments across public API surface

### CI/CD

- Added GitHub Actions workflow (`.github/workflows/ci.yml`) for automated build, test, pack on push/PR and NuGet publish on version tags

### Testing

- Added property-based tests using FsCheck for formatter correctness, thread safety, HTML structure preservation, and input validation
- Added integration tests for `appsettings.json` configuration binding
- Added end-to-end test for custom HTML templates
- Added lock-timeout and dropped-event tests
