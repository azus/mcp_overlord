namespace mcp_overlord.Logging
{
    /// <summary>Splits text into lines a virtualized list can read by index.</summary>
    public static class TextLineList
    {
        /// <summary>A line is wrapped again when it is longer than this.</summary>
        public const int WrapWidth = 120;

        /// <summary>
        /// Decodes UTF-8 and wraps long lines.
        /// </summary>
        /// <param name="bytes">Body. Null is one empty line.</param>
        /// <returns>The lines.</returns>
        public static ComputedLineList FromBytes(byte[]? bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return new ComputedLineList(1, _ => "");
            }

            return FromText(System.Text.Encoding.UTF8.GetString(bytes));
        }

        /// <summary>
        /// Wraps <paramref name="text"/>. Existing newlines are kept. A line longer than <see cref="WrapWidth"/> is split.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <returns>The lines.</returns>
        public static ComputedLineList FromText(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return new ComputedLineList(1, _ => "");
            }

            int[] starts = Index(text);
            return new ComputedLineList(starts.Length, index => Slice(text, starts, index));
        }

        private static int[] Index(string text)
        {
            var starts = new List<int>();
            int index = 0;
            while (index < text.Length)
            {
                starts.Add(index);
                int end = FindEnd(text, index);
                index = end;
            }

            if (starts.Count == 0)
            {
                starts.Add(0);
            }

            return starts.ToArray();
        }

        private static int FindEnd(string text, int start)
        {
            int column = 0;
            int index = start;
            while (index < text.Length && column < WrapWidth)
            {
                if (text[index] == '\r')
                {
                    index++;
                    if (index < text.Length && text[index] == '\n')
                    {
                        index++;
                    }

                    return index;
                }

                if (text[index] == '\n')
                {
                    return index + 1;
                }

                index++;
                column++;
            }

            return index;
        }

        private static string Slice(string text, int[] starts, int line)
        {
            int start = starts[line];
            int end = line + 1 < starts.Length ? starts[line + 1] : text.Length;
            while (end > start && (text[end - 1] == '\n' || text[end - 1] == '\r'))
            {
                end--;
            }

            return text.Substring(start, end - start);
        }
    }
}
