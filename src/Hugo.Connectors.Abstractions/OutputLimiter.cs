namespace Hugo.Connectors.Abstractions
{
    using System;
    using System.Text;

    /// <summary>
    /// Helpers that bound text output by bytes and lines. Thread-safe.
    /// </summary>
    public static class OutputLimiter
    {
        /// <summary>
        /// Truncate text to a maximum number of UTF-8 bytes and lines, keeping the head.
        /// </summary>
        /// <param name="text">Text. Null is treated as empty.</param>
        /// <param name="maxBytes">Maximum UTF-8 bytes.</param>
        /// <param name="maxLines">Maximum lines.</param>
        /// <param name="result">Result whose <see cref="ToolResult.Truncated"/> and <see cref="ToolResult.OriginalBytes"/> are updated.</param>
        /// <returns>Bounded text, with a trailing marker when truncated.</returns>
        public static string Head(string? text, int maxBytes, int maxLines, ToolResult? result = null)
        {
            string value = text ?? String.Empty;
            int originalBytes = Encoding.UTF8.GetByteCount(value);
            bool truncated = false;

            if (maxLines > 0)
            {
                int lines = 0;
                for (int i = 0; i < value.Length; i++)
                {
                    if (value[i] != '\n') continue;
                    lines++;
                    if (lines >= maxLines)
                    {
                        if (i < value.Length - 1)
                        {
                            value = value.Substring(0, i + 1);
                            truncated = true;
                        }
                        break;
                    }
                }
            }

            if (maxBytes > 0 && Encoding.UTF8.GetByteCount(value) > maxBytes)
            {
                value = CutToBytes(value, maxBytes);
                truncated = true;
            }

            if (result != null)
            {
                result.OriginalBytes = Math.Max(result.OriginalBytes, originalBytes);
                result.Truncated = result.Truncated || truncated;
            }
            return truncated ? value + "\n[output truncated: " + originalBytes + " bytes total]" : value;
        }

        /// <summary>
        /// Truncate text to a maximum number of UTF-8 bytes and lines, keeping the tail (for logs and command output).
        /// </summary>
        /// <param name="text">Text. Null is treated as empty.</param>
        /// <param name="maxBytes">Maximum UTF-8 bytes.</param>
        /// <param name="maxLines">Maximum lines.</param>
        /// <param name="result">Result to update.</param>
        /// <returns>Bounded text, with a leading marker when truncated.</returns>
        public static string Tail(string? text, int maxBytes, int maxLines, ToolResult? result = null)
        {
            string value = text ?? String.Empty;
            int originalBytes = Encoding.UTF8.GetByteCount(value);
            bool truncated = false;

            if (maxLines > 0)
            {
                int lines = 0;
                int end = value.EndsWith('\n') ? value.Length - 2 : value.Length - 1;
                for (int i = end; i >= 0; i--)
                {
                    if (value[i] != '\n') continue;
                    lines++;
                    if (lines >= maxLines)
                    {
                        value = value.Substring(i + 1);
                        truncated = true;
                        break;
                    }
                }
            }

            if (maxBytes > 0 && Encoding.UTF8.GetByteCount(value) > maxBytes)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(value);
                int start = bytes.Length - maxBytes;
                while (start < bytes.Length && (bytes[start] & 0xC0) == 0x80) start++;
                value = Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
                truncated = true;
            }

            if (result != null)
            {
                result.OriginalBytes = Math.Max(result.OriginalBytes, originalBytes);
                result.Truncated = result.Truncated || truncated;
            }
            return truncated ? "[output truncated: " + originalBytes + " bytes total, showing the end]\n" + value : value;
        }

        private static string CutToBytes(string value, int maxBytes)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            int length = maxBytes;
            while (length > 0 && (bytes[length] & 0xC0) == 0x80) length--;
            return Encoding.UTF8.GetString(bytes, 0, length);
        }
    }
}
