using System;

namespace Serilog.Sinks.HtmlFile
{
    /// <summary>
    /// Centralizes active file naming pattern evaluation and validation logic.
    /// </summary>
    public static class FileNamingHelper
    {
        /// <summary>
        /// The default date format used in file naming patterns.
        /// </summary>
        public const string DefaultDateFormat = "yyyy-MM-dd";

        /// <summary>
        /// Validates the file naming pattern by evaluating it and checking the result.
        /// </summary>
        /// <param name="pattern">The file naming pattern to validate.</param>
        /// <exception cref="ArgumentException">Pattern evaluates to an empty or whitespace-only path.</exception>
        public static void Validate(string pattern)
        {
            if (pattern == null) throw new ArgumentNullException(nameof(pattern));

            var result = EvaluatePattern(pattern);
            if (string.IsNullOrWhiteSpace(result))
                throw new ArgumentException("The file naming pattern must produce a non-empty, non-whitespace path after placeholder evaluation.", nameof(pattern));
        }

        /// <summary>
        /// Evaluates a file naming pattern by replacing placeholders with current values.
        /// </summary>
        /// <param name="pattern">The naming pattern (e.g., "logs/app-{Date}.html").</param>
        /// <param name="dateFormat">The format for the {Date} placeholder.</param>
        /// <param name="utcNow">The current UTC time (injectable for testing).</param>
        /// <returns>The evaluated concrete file path.</returns>
        public static string EvaluatePattern(
            string pattern,
            string dateFormat = DefaultDateFormat,
            DateTime? utcNow = null)
        {
            var now = utcNow ?? DateTime.UtcNow;
            var result = pattern;
            result = result.Replace("{Date}", now.ToString(dateFormat));
            result = result.Replace("{MachineName}", Environment.MachineName);
            return result;
        }
    }
}
