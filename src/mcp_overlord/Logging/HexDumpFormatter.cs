using System.Text;

namespace mcp_overlord.Logging
{
    /// <summary>Formats bytes as an offset, sixteen hex bytes, and an ASCII column.</summary>
    public static class HexDumpFormatter
    {
        /// <summary>Bytes shown on one line.</summary>
        public const int BytesPerLine = 16;

        /// <summary>
        /// Number of lines needed for <paramref name="length"/> bytes.
        /// </summary>
        /// <param name="length">Byte count.</param>
        /// <returns>Zero when there are no bytes.</returns>
        public static int LineCount(int length)
        {
            if (length <= 0)
            {
                return 0;
            }

            return (length + BytesPerLine - 1) / BytesPerLine;
        }

        /// <summary>
        /// Formats one line. The index is the line, not the byte offset.
        /// </summary>
        /// <param name="data">Bytes. A short line is padded so the ASCII column stays aligned.</param>
        /// <param name="lineIndex">Zero-based line.</param>
        /// <returns>The line.</returns>
        public static string FormatLine(byte[] data, int lineIndex)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            int offset = lineIndex * BytesPerLine;
            if (lineIndex < 0 || offset >= data.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(lineIndex));
            }

            int count = Math.Min(BytesPerLine, data.Length - offset);
            var builder = new StringBuilder(78);
            builder.Append(offset.ToString("X8"));
            builder.Append("  ");
            AppendHex(builder, data, offset, count);
            builder.Append("  ");
            AppendAscii(builder, data, offset, count);
            return builder.ToString();
        }

        /// <summary>
        /// A line list over <paramref name="data"/>.
        /// </summary>
        /// <param name="data">Bytes. Null is an empty list.</param>
        /// <returns>The lines.</returns>
        public static ComputedLineList Lines(byte[]? data)
        {
            byte[] bytes = data ?? Array.Empty<byte>();
            int count = LineCount(bytes.Length);
            return new ComputedLineList(count, index => FormatLine(bytes, index));
        }

        private static void AppendHex(StringBuilder builder, byte[] data, int offset, int count)
        {
            for (int index = 0; index < BytesPerLine; index++)
            {
                if (index > 0)
                {
                    builder.Append(' ');
                }

                if (index < count)
                {
                    builder.Append(data[offset + index].ToString("X2"));
                }
                else
                {
                    builder.Append("  ");
                }
            }
        }

        private static void AppendAscii(StringBuilder builder, byte[] data, int offset, int count)
        {
            for (int index = 0; index < count; index++)
            {
                byte value = data[offset + index];
                builder.Append(value >= 32 && value <= 126 ? (char)value : '.');
            }
        }
    }
}
