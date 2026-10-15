namespace Hugo.Cli.Mcp
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Reads server-sent events from a stream (the <c>text/event-stream</c> format: <c>id</c>, <c>event</c>,
    /// <c>data</c>, and <c>retry</c> fields, comment lines starting with a colon, events ended by a blank line). Not
    /// thread-safe.
    /// </summary>
    public class SseEventReader : IDisposable
    {
        #region Private-Members

        private readonly StreamReader _Reader;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate. The stream is disposed with the reader.
        /// </summary>
        /// <param name="stream">Event stream.</param>
        /// <exception cref="ArgumentNullException">Thrown when stream is null.</exception>
        public SseEventReader(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            _Reader = new StreamReader(stream, new UTF8Encoding(false), false, 8192, false);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Read the next event.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The event, or null at end of stream. A partial event at end of stream is discarded, as the format requires.</returns>
        /// <exception cref="IOException">Thrown when the connection breaks.</exception>
        /// <exception cref="OperationCanceledException">Thrown when cancelled.</exception>
        public async Task<SseEvent?> ReadNextAsync(CancellationToken token)
        {
            SseEvent current = new SseEvent();
            StringBuilder data = new StringBuilder();
            bool hasData = false;
            bool hasFields = false;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                string? line = await _Reader.ReadLineAsync(token).ConfigureAwait(false);
                if (line == null) return null;
                if (line.Length == 0)
                {
                    if (!hasFields) continue;
                    current.Data = data.ToString();
                    return current;
                }
                if (line[0] == ':') continue;

                string field;
                string value;
                int colon = line.IndexOf(':', StringComparison.Ordinal);
                if (colon < 0)
                {
                    field = line;
                    value = String.Empty;
                }
                else
                {
                    field = line.Substring(0, colon);
                    value = line.Substring(colon + 1);
                    if (value.StartsWith(" ", StringComparison.Ordinal)) value = value.Substring(1);
                }

                hasFields = true;
                switch (field)
                {
                    case "data":
                        if (hasData) data.Append('\n');
                        data.Append(value);
                        hasData = true;
                        break;
                    case "id":
                        if (value.IndexOf('\0', StringComparison.Ordinal) < 0) current.Id = value;
                        break;
                    case "event":
                        current.EventType = String.IsNullOrEmpty(value) ? "message" : value;
                        break;
                    case "retry":
                        if (Int32.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int retry)) current.RetryMs = retry;
                        break;
                    default:
                        break;
                }
            }
        }

        /// <summary>
        /// Dispose.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Dispose.
        /// </summary>
        /// <param name="disposing">Disposing.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing) _Reader.Dispose();
            _Disposed = true;
        }

        #endregion
    }
}
