using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace Serilog.Sinks.HtmlFile
{
    /// <summary>
    /// A unified HTML template that loads either from an embedded resource (default)
    /// or from a user-provided file path on disk. Replaces both
    /// <c>DefaultHtmlTemplate</c> and <c>CustomHtmlTemplate</c>.
    /// </summary>
    public class HtmlTemplate : IHtmlTemplate
    {
        private const string PlaceholderMarker = "<!-- LOG_ENTRIES_PLACEHOLDER -->";

        private readonly string _headerContent;
        private readonly string _tailContent;

        /// <summary>
        /// Creates a new <see cref="HtmlTemplate"/> instance.
        /// </summary>
        /// <param name="templateFilePath">
        /// Optional path to a custom HTML template file on disk.
        /// When <c>null</c>, the built-in default template is loaded from the
        /// assembly's embedded resources.
        /// </param>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the embedded resource is not found in the assembly, or when
        /// the loaded template content does not contain the required
        /// <c>&lt;!-- LOG_ENTRIES_PLACEHOLDER --&gt;</c> marker.
        /// </exception>
        /// <exception cref="FileNotFoundException">
        /// Thrown when <paramref name="templateFilePath"/> is non-null and the file
        /// does not exist.
        /// </exception>
        public HtmlTemplate(string? templateFilePath = null)
        {
            string content;

            if (templateFilePath == null)
            {
                var assembly = typeof(HtmlTemplate).Assembly;
                var resourceName = "Serilog.Sinks.HtmlFile.DefaultTemplate.html";
                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                        throw new InvalidOperationException(
                            $"Embedded resource '{resourceName}' not found in assembly '{assembly.FullName}'.");

                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        content = reader.ReadToEnd();
                    }
                }
            }
            else
            {
                if (!File.Exists(templateFilePath))
                    throw new FileNotFoundException(
                        $"Custom HTML template file not found: '{templateFilePath}'",
                        templateFilePath);

                content = File.ReadAllText(templateFilePath);
            }

            var markerIndex = content.IndexOf(PlaceholderMarker, StringComparison.Ordinal);
            if (markerIndex < 0)
                throw new InvalidOperationException(
                    $"HTML template is missing the required '{PlaceholderMarker}' marker.");

            _headerContent = content.Substring(0, markerIndex);
            _tailContent = content.Substring(markerIndex + PlaceholderMarker.Length);
        }

        /// <inheritdoc />
        public string InsertionMarker => "// __LOG_ENTRIES_END__";

        /// <inheritdoc />
        public void WriteHeader(TextWriter output)
        {
            output.Write(_headerContent);
            output.Write("var logEntries = [\n");
        }

        /// <inheritdoc />
        public void WriteTail(TextWriter output)
        {
            output.Write("// __LOG_ENTRIES_END__\n");
            output.Write("];\n");
            output.Write(_tailContent);
        }
    }
}
