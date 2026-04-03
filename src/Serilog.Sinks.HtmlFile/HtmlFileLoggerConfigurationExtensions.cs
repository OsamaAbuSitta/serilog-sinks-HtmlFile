using System;
using System.Text;
using Serilog.Configuration;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Sinks.HtmlFile;

namespace Serilog
{
    /// <summary>
    /// Extends <see cref="LoggerSinkConfiguration"/> with methods to add the HTML file sink.
    /// </summary>
    public static class HtmlFileLoggerConfigurationExtensions
    {
        /// <summary>
        /// Write log events to a self-contained HTML file with an embedded interactive log viewer.
        /// </summary>
        /// <param name="sinkConfiguration">Logger sink configuration.</param>
        /// <param name="path">Path to the HTML log file. An absolute path is recommended; relative paths are resolved under the current working directory at runtime.</param>
        /// <param name="restrictedToMinimumLevel">The minimum level for events passed through the sink. Defaults to <see cref="LevelAlias.Minimum"/>.</param>
        /// <param name="fileSizeLimitBytes">The approximate maximum size, in bytes, to which a log file will be allowed to grow. For unlimited growth, pass <c>null</c>. The default is 1 GB. To avoid silent truncation, set this to a value of at least 1 byte. When the limit is reached and rolling is enabled, a new file is created.</param>
        /// <param name="encoding">Character encoding used to write the file. The default is UTF-8 without BOM.</param>
        /// <param name="customTemplatePath">Optional path to a custom HTML template file. When provided, the template is used instead of the built-in default.</param>
        /// <param name="formatter">Optional <see cref="ITextFormatter"/> to format log events. When <c>null</c>, <see cref="HtmlLogEventFormatter"/> is used.</param>
        /// <param name="archiveNamingPattern">Optional archive naming pattern with placeholders like <c>{BaseName}</c>, <c>{Timestamp}</c>, <c>{Extension}</c>. Defaults to <c>{BaseName}_{Timestamp}{Extension}</c>.</param>
        /// <param name="archiveTimestampFormat">Optional .NET DateTime format string for the <c>{Timestamp}</c> placeholder in archive names. Defaults to <c>yyyyMMddHHmmss</c>.</param>
        /// <param name="fileNamingPattern">Optional active file naming pattern with placeholders like <c>{Date}</c> and <c>{MachineName}</c>. When <c>null</c>, the <paramref name="path"/> parameter is used as-is.</param>
        /// <param name="dateFormat">Optional .NET DateTime format string for the <c>{Date}</c> placeholder in file naming patterns. Defaults to <c>yyyy-MM-dd</c>.</param>
        /// <returns>Configuration object allowing method chaining.</returns>
        public static LoggerConfiguration HtmlFile(
            this LoggerSinkConfiguration sinkConfiguration,
            string path,
            LogEventLevel restrictedToMinimumLevel = LevelAlias.Minimum,
            long? fileSizeLimitBytes = 1073741824L,
            Encoding? encoding = null,
            string? customTemplatePath = null,
            ITextFormatter? formatter = null,
            string? archiveNamingPattern = null,
            string? archiveTimestampFormat = null,
            string? fileNamingPattern = null,
            string? dateFormat = null)
        {
            if (sinkConfiguration == null) throw new ArgumentNullException(nameof(sinkConfiguration));
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (fileSizeLimitBytes.HasValue && fileSizeLimitBytes.Value < 1)
                throw new ArgumentException("The file size limit must be at least 1 byte or null.", nameof(fileSizeLimitBytes));

            var resolvedEncoding = encoding ?? new UTF8Encoding(false);
            var resolvedFormatter = formatter ?? new HtmlLogEventFormatter();
            var resolvedArchivePattern = archiveNamingPattern ?? ArchiveNamingHelper.DefaultPattern;
            var resolvedArchiveTimestampFormat = archiveTimestampFormat ?? ArchiveNamingHelper.DefaultTimestampFormat;
            var resolvedDateFormat = dateFormat ?? FileNamingHelper.DefaultDateFormat;

            IHtmlTemplate template = customTemplatePath != null
                ? new HtmlTemplate(customTemplatePath)
                : new HtmlTemplate();

            if (fileSizeLimitBytes.HasValue && fileSizeLimitBytes.Value > 0)
            {
                var sink = new RollingHtmlFileSink(
                    path,
                    resolvedFormatter,
                    template,
                    fileSizeLimitBytes.Value,
                    resolvedEncoding,
                    resolvedArchivePattern,
                    resolvedArchiveTimestampFormat,
                    fileNamingPattern,
                    resolvedDateFormat);
                return sinkConfiguration.Sink(sink, restrictedToMinimumLevel);
            }
            else
            {
                var resolvedPath = fileNamingPattern != null
                    ? FileNamingHelper.EvaluatePattern(fileNamingPattern, resolvedDateFormat)
                    : path;

                var sink = new HtmlFileSink(
                    resolvedPath, resolvedFormatter, template, null, resolvedEncoding);
                return sinkConfiguration.Sink(sink, restrictedToMinimumLevel);
            }
        }
    }
}
