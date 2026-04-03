using System;

namespace Serilog.Sinks.HtmlFile
{
    /// <summary>
    /// Centralizes archive file naming pattern validation and formatting logic.
    /// </summary>
    public static class ArchiveNamingHelper
    {
        /// <summary>
        /// The default archive naming pattern: {BaseName}_{Timestamp}{Extension}.
        /// </summary>
        public const string DefaultPattern = "{BaseName}_{Timestamp}{Extension}";

        /// <summary>
        /// The default timestamp format used in archive file names.
        /// </summary>
        public const string DefaultTimestampFormat = "yyyyMMddHHmmss";

        /// <summary>
        /// Validates the archive naming pattern and timestamp format.
        /// </summary>
        /// <param name="pattern">The archive naming pattern. Must contain {BaseName}.</param>
        /// <param name="timestampFormat">The .NET DateTime format string for the {Timestamp} placeholder.</param>
        /// <exception cref="ArgumentException">Pattern does not contain {BaseName}.</exception>
        /// <exception cref="FormatException">Timestamp format is invalid.</exception>
        public static void Validate(string pattern, string timestampFormat)
        {
            if (pattern == null) throw new ArgumentNullException(nameof(pattern));
            if (timestampFormat == null) throw new ArgumentNullException(nameof(timestampFormat));

            if (pattern.IndexOf("{BaseName}", StringComparison.Ordinal) < 0)
                throw new ArgumentException("The archive naming pattern must contain the {BaseName} placeholder.", nameof(pattern));

            try
            {
                DateTime.UtcNow.ToString(timestampFormat);
            }
            catch (FormatException)
            {
                throw new FormatException($"The archive timestamp format '{timestampFormat}' is not a valid .NET DateTime format string.");
            }
        }

        /// <summary>
        /// Formats an archive file name from the given pattern, base name, timestamp, and extension.
        /// </summary>
        /// <param name="pattern">The archive naming pattern containing placeholders.</param>
        /// <param name="baseName">The base file name without extension.</param>
        /// <param name="utcTimestamp">The UTC timestamp for the archive.</param>
        /// <param name="extension">The file extension including the leading dot.</param>
        /// <param name="timestampFormat">The .NET DateTime format string for the {Timestamp} placeholder.</param>
        /// <returns>The formatted archive file name.</returns>
        public static string FormatArchiveName(
            string pattern,
            string baseName,
            DateTime utcTimestamp,
            string extension,
            string timestampFormat)
        {
            var result = pattern;
            result = result.Replace("{BaseName}", baseName);
            result = result.Replace("{Timestamp}", utcTimestamp.ToString(timestampFormat));
            result = result.Replace("{Extension}", extension);
            return result;
        }
    }
}
