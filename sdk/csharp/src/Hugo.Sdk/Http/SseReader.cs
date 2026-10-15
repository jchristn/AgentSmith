namespace Hugo.Sdk.Http
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Server-sent event parser shared by the events stream and chat streaming. Follows the WHATWG rules: a blank
    /// line dispatches an event, lines starting with a colon are comments, data lines are joined with a line feed,
    /// one leading space after the colon is removed, and the retry field is ignored. Stateless and thread-safe.
    /// </summary>
    public static class SseReader
    {
        #region Public-Methods

        /// <summary>
        /// Read events from a stream until it ends or the token is cancelled.
        /// </summary>
        /// <param name="stream">Response stream.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Events in arrival order.</returns>
        /// <exception cref="ArgumentNullException">Thrown when stream is null.</exception>
        public static async IAsyncEnumerable<SseEvent> ReadAsync(Stream stream, [EnumeratorCancellation] CancellationToken token = default)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, false, 4096, true))
            {
                SseEvent current = new SseEvent();
                StringBuilder data = new StringBuilder();
                bool hasData = false;
                while (!token.IsCancellationRequested)
                {
                    string? line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                    if (line == null) break;
                    if (line.Length == 0)
                    {
                        if (hasData || current.Event != null)
                        {
                            current.Data = data.ToString();
                            yield return current;
                        }
                        current = new SseEvent();
                        data.Clear();
                        hasData = false;
                        continue;
                    }
                    if (line[0] == ':') continue;
                    int colon = line.IndexOf(':');
                    string field = colon < 0 ? line : line.Substring(0, colon);
                    string value = colon < 0 ? String.Empty : line.Substring(colon + 1);
                    if (value.Length > 0 && value[0] == ' ') value = value.Substring(1);
                    if (field == "event")
                    {
                        current.Event = value;
                    }
                    else if (field == "id")
                    {
                        current.Id = value;
                    }
                    else if (field == "data")
                    {
                        if (hasData) data.Append('\n');
                        data.Append(value);
                        hasData = true;
                    }
                }
                if (hasData && !token.IsCancellationRequested)
                {
                    current.Data = data.ToString();
                    yield return current;
                }
            }
        }

        /// <summary>
        /// Parse a complete SSE text (for tests and buffered responses).
        /// </summary>
        /// <param name="text">SSE text.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Events.</returns>
        /// <exception cref="ArgumentNullException">Thrown when text is null.</exception>
        public static async Task<List<SseEvent>> ParseAsync(string text, CancellationToken token = default)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            List<SseEvent> events = new List<SseEvent>();
            using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(text)))
            {
                await foreach (SseEvent item in ReadAsync(stream, token).ConfigureAwait(false))
                {
                    events.Add(item);
                }
            }
            return events;
        }

        #endregion
    }
}
