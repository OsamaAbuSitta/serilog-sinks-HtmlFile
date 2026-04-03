using System;
using System.IO;
using System.Linq;
using Serilog.Events;
using Serilog.Formatting;

namespace Serilog.Sinks.HtmlFile
{
    /// <summary>
    /// Formats a <see cref="LogEvent"/> as a JavaScript object literal string
    /// suitable for embedding in an HTML log file's script block.
    /// </summary>
    public class HtmlLogEventFormatter : ITextFormatter
    {
        /// <inheritdoc />
        public void Format(LogEvent logEvent, TextWriter output)
        {
            if (logEvent == null) throw new ArgumentNullException(nameof(logEvent));
            if (output == null) throw new ArgumentNullException(nameof(output));

            output.Write("{\"type\":\"");
            output.Write(logEvent.Level.ToString());
            output.Write("\",\"time\":\"");
            output.Write(logEvent.Timestamp.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
            output.Write("\",\"msg\":\"");

            var message = logEvent.RenderMessage();
            if (logEvent.Exception != null)
            {
                if (message.Length > 0)
                    message += "\n" + logEvent.Exception;
                else
                    message = logEvent.Exception.ToString();
            }

            WriteEscapedJsString(output, message);

            output.Write("\",\"props\":{");
            WriteProperties(output, logEvent);
            output.Write("}},\n");
        }

        private static void WriteProperties(TextWriter output, LogEvent logEvent)
        {
            var properties = logEvent.Properties;
            if (properties == null || properties.Count == 0)
                return;

            var first = true;
            foreach (var kvp in properties)
            {
                if (!first)
                    output.Write(',');
                first = false;

                output.Write('"');
                output.Write(kvp.Key);
                output.Write('"');
                output.Write(':');
                WritePropertyValue(output, kvp.Value);
            }
        }

        private static void WritePropertyValue(TextWriter output, LogEventPropertyValue value)
        {
            if (value == null)
            {
                output.Write("null");
                return;
            }

            if (value is ScalarValue scalar)
            {
                if (scalar.Value == null)
                {
                    output.Write("null");
                    return;
                }

                if (scalar.Value is int || scalar.Value is long || scalar.Value is float
                    || scalar.Value is double || scalar.Value is decimal
                    || scalar.Value is short || scalar.Value is byte
                    || scalar.Value is uint || scalar.Value is ulong || scalar.Value is ushort
                    || scalar.Value is sbyte)
                {
                    output.Write(scalar.Value);
                    return;
                }

                if (scalar.Value is bool b)
                {
                    output.Write(b ? "true" : "false");
                    return;
                }

                // Everything else is rendered as a JS string
                output.Write('"');
                WriteEscapedJsString(output, scalar.Value.ToString());
                output.Write('"');
                return;
            }

            // For sequence, structure, and dictionary values, render as string
            output.Write('"');
            WriteEscapedJsString(output, value.ToString());
            output.Write('"');
        }

        /// <summary>
        /// Writes a string to the output with JavaScript/HTML-safe escaping.
        /// Escapes: backslash, double quote, single quote, newline, carriage return,
        /// tab, angle brackets (for HTML safety), and forward slash.
        /// </summary>
        internal static void WriteEscapedJsString(TextWriter output, string value)
        {
            if (value == null)
                return;

            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                switch (c)
                {
                    case '\\':
                        output.Write("\\\\");
                        break;
                    case '"':
                        output.Write("\\\"");
                        break;
                    case '\'':
                        output.Write("\\'");
                        break;
                    case '\n':
                        output.Write("\\n");
                        break;
                    case '\r':
                        output.Write("\\r");
                        break;
                    case '\t':
                        output.Write("\\t");
                        break;
                    case '<':
                        output.Write("\\u003c");
                        break;
                    case '>':
                        output.Write("\\u003e");
                        break;
                    case '/':
                        output.Write("\\/");
                        break;
                    default:
                        if (c < ' ')
                        {
                            // Escape other control characters as unicode
                            output.Write("\\u");
                            output.Write(((int)c).ToString("x4"));
                        }
                        else
                        {
                            output.Write(c);
                        }
                        break;
                }
            }
        }
    }
}
