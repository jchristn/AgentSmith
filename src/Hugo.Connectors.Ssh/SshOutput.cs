namespace Hugo.Connectors.Ssh
{
    using System;
    using System.Text;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Output bounding helpers built on <see cref="OutputLimiter"/>, aware of bytes already dropped during capture.
    /// Thread-safe.
    /// </summary>
    public static class SshOutput
    {
        /// <summary>
        /// Bound captured stream output by bytes and lines, keeping the end. When capture already dropped bytes, the
        /// truncation marker reports the true total and the first (probably partial) line is dropped.
        /// </summary>
        /// <param name="captured">Captured text (the tail of the stream).</param>
        /// <param name="totalBytes">Total bytes the stream produced.</param>
        /// <param name="dropped">Whether capture dropped bytes.</param>
        /// <param name="maxBytes">Maximum bytes to return.</param>
        /// <param name="maxLines">Maximum lines to return.</param>
        /// <param name="result">Result whose truncation fields are updated. May be null.</param>
        /// <returns>Bounded text, with a leading marker when truncated.</returns>
        public static string BoundTail(string? captured, long totalBytes, bool dropped, int maxBytes, int maxLines, ToolResult? result)
        {
            string text = captured ?? String.Empty;
            ToolResult probe = new ToolResult();
            string bounded = OutputLimiter.Tail(text, maxBytes, maxLines, probe);
            long original = Math.Max(totalBytes, Encoding.UTF8.GetByteCount(text));

            if (!dropped)
            {
                if (result != null)
                {
                    result.OriginalBytes = Math.Max(result.OriginalBytes, probe.OriginalBytes);
                    result.Truncated = result.Truncated || probe.Truncated;
                }
                return bounded;
            }

            if (probe.Truncated)
            {
                int newline = bounded.IndexOf('\n');
                bounded = newline >= 0 ? bounded.Substring(newline + 1) : String.Empty;
            }
            else
            {
                int newline = bounded.IndexOf('\n');
                if (newline >= 0 && newline < bounded.Length - 1) bounded = bounded.Substring(newline + 1);
            }
            if (result != null)
            {
                result.OriginalBytes = Math.Max(result.OriginalBytes, original);
                result.Truncated = true;
            }
            return "[output truncated: " + original + " bytes total, showing the end]\n" + bounded;
        }
    }
}
