namespace Hugo.Connectors.Agents
{
    using System;

    /// <summary>
    /// Character-based clipping for individual remote values (a tool description, a schema, a reply) before the whole
    /// result is bounded by the call's output limits. Thread-safe.
    /// </summary>
    public static class TextClip
    {
        /// <summary>
        /// Clip text to a maximum number of characters, appending a marker when clipped.
        /// </summary>
        /// <param name="value">Text. Null is treated as empty.</param>
        /// <param name="maxChars">Maximum characters kept. Zero or less means no limit.</param>
        /// <returns>The text, clipped with a trailing <c>[clipped, N chars total]</c> marker when longer than the limit.</returns>
        public static string Clip(string? value, int maxChars)
        {
            string v = value ?? String.Empty;
            if (maxChars <= 0 || v.Length <= maxChars) return v;
            int cut = maxChars;
            if (Char.IsHighSurrogate(v[cut - 1])) cut--;
            return v.Substring(0, cut) + " [clipped, " + v.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + " chars total]";
        }

        /// <summary>
        /// Collapse line breaks and runs of whitespace into single spaces (for one-line summaries).
        /// </summary>
        /// <param name="value">Text. Null is treated as empty.</param>
        /// <returns>Single-line text.</returns>
        public static string OneLine(string? value)
        {
            string v = value ?? String.Empty;
            return String.Join(' ', v.Split(new char[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
