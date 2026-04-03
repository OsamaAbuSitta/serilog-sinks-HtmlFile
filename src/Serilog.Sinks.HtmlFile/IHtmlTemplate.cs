using System.IO;

namespace Serilog.Sinks.HtmlFile
{
    /// <summary>
    /// Defines the HTML scaffold that wraps log entries in an HTML log file.
    /// </summary>
    public interface IHtmlTemplate
    {
        /// <summary>
        /// Writes the HTML content before the log entries insertion point.
        /// </summary>
        void WriteHeader(TextWriter output);

        /// <summary>
        /// Writes the HTML content after the log entries insertion point.
        /// </summary>
        void WriteTail(TextWriter output);

        /// <summary>
        /// Gets the marker string used to locate the insertion point in existing files.
        /// The marker must appear exactly once in the combined output of
        /// <see cref="WriteHeader"/> and <see cref="WriteTail"/>.
        /// The sink uses this marker to locate the byte offset where new log entries
        /// are incrementally appended into an existing HTML file.
        /// </summary>
        string InsertionMarker { get; }
    }
}
