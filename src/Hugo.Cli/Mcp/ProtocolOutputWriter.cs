namespace Hugo.Cli.Mcp
{
    using System;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Writes JSON-RPC messages to the stdio transport, one compact message per line, flushing after each. Messages
    /// from concurrent responses never interleave. Thread-safe.
    /// </summary>
    public class ProtocolOutputWriter : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Number of messages written.
        /// </summary>
        public long MessagesWritten
        {
            get
            {
                return Interlocked.Read(ref _MessagesWritten);
            }
        }

        #endregion

        #region Private-Members

        private static readonly byte[] _NewLine = new byte[] { (byte)'\n' };
        private readonly Stream _Output;
        private readonly SemaphoreSlim _Lock = new SemaphoreSlim(1, 1);
        private long _MessagesWritten = 0;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate. The stream is not disposed with the writer.
        /// </summary>
        /// <param name="output">Output stream (standard output).</param>
        /// <exception cref="ArgumentNullException">Thrown when output is null.</exception>
        public ProtocolOutputWriter(Stream output)
        {
            _Output = output ?? throw new ArgumentNullException(nameof(output));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Write one message. The text must be compact JSON (see <see cref="CliJson.Minify"/>).
        /// </summary>
        /// <param name="compactJson">Compact JSON text without line breaks.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the text is null or empty.</exception>
        /// <exception cref="ArgumentException">Thrown when the text contains a line break.</exception>
        public async Task WriteAsync(string compactJson, CancellationToken token)
        {
            if (String.IsNullOrEmpty(compactJson)) throw new ArgumentNullException(nameof(compactJson));
            if (compactJson.IndexOf('\n', StringComparison.Ordinal) >= 0 || compactJson.IndexOf('\r', StringComparison.Ordinal) >= 0)
                throw new ArgumentException("A stdio message must not contain a line break.", nameof(compactJson));
            byte[] bytes = Encoding.UTF8.GetBytes(compactJson);
            await _Lock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await _Output.WriteAsync(bytes, 0, bytes.Length, token).ConfigureAwait(false);
                await _Output.WriteAsync(_NewLine, 0, 1, token).ConfigureAwait(false);
                await _Output.FlushAsync(token).ConfigureAwait(false);
                Interlocked.Increment(ref _MessagesWritten);
            }
            finally
            {
                _Lock.Release();
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
            if (disposing) _Lock.Dispose();
            _Disposed = true;
        }

        #endregion
    }
}
