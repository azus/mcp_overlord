namespace mcp_overlord.Services
{
    /// <summary>
    /// Splits a Windows command line into the executable and the argument string.
    /// </summary>
    public static class CommandLineSplitter
    {
        /// <summary>
        /// Returns the arguments that follow the executable. Quotes around the executable are not included.
        /// </summary>
        /// <param name="commandLine">Full command line, or null.</param>
        /// <returns>The argument string. Empty when there are no arguments.</returns>
        public static string GetArguments(string? commandLine)
        {
            if (string.IsNullOrWhiteSpace(commandLine))
            {
                return "";
            }

            string text = commandLine.Trim();
            if (text.Length == 0)
            {
                return "";
            }

            if (text[0] == '"')
            {
                int closingQuote = text.IndexOf('"', 1);
                if (closingQuote < 0)
                {
                    return "";
                }

                return text[(closingQuote + 1)..].TrimStart();
            }

            int space = text.IndexOf(' ');
            if (space < 0)
            {
                return "";
            }

            return text[(space + 1)..].TrimStart();
        }
    }
}
