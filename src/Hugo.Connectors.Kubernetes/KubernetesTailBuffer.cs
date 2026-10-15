namespace Hugo.Connectors.Kubernetes
{
    using System;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Fixed-capacity byte buffer that keeps the most recent bytes written to it and counts the total, so streamed
    /// output is bounded in memory however much the remote side produces. Thread-safe.
    /// </summary>
    public class KubernetesTailBuffer
    {
        #region Public-Members

        /// <summary>
        /// Capacity in bytes. Minimum 1.
        /// </summary>
        public int Capacity { get; }

        /// <summary>
        /// Total bytes written, including bytes no longer held.
        /// </summary>
        public long TotalBytes
        {
            get
            {
                lock (_Lock)
                {
                    return _Total;
                }
            }
        }

        /// <summary>
        /// Whether bytes were dropped because the total exceeded the capacity.
        /// </summary>
        public bool Dropped
        {
            get
            {
                lock (_Lock)
                {
                    return _Total > Capacity;
                }
            }
        }

        #endregion

        #region Private-Members

        private readonly byte[] _Buffer;
        private int _Start = 0;
        private int _Count = 0;
        private long _Total = 0;
        private readonly object _Lock = new object();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="capacity">Capacity in bytes; values below 1 become 1.</param>
        public KubernetesTailBuffer(int capacity)
        {
            Capacity = Math.Max(1, capacity);
            _Buffer = new byte[Capacity];
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Append bytes.
        /// </summary>
        /// <param name="data">Data.</param>
        /// <param name="offset">Offset.</param>
        /// <param name="count">Count.</param>
        public void Write(byte[] data, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(data);
            if (count <= 0) return;
            lock (_Lock)
            {
                _Total += count;
                if (count >= Capacity)
                {
                    Buffer.BlockCopy(data, offset + count - Capacity, _Buffer, 0, Capacity);
                    _Start = 0;
                    _Count = Capacity;
                    return;
                }
                for (int i = 0; i < count; i++)
                {
                    int end = (_Start + _Count) % Capacity;
                    _Buffer[end] = data[offset + i];
                    if (_Count < Capacity) _Count++;
                    else _Start = (_Start + 1) % Capacity;
                }
            }
        }

        /// <summary>
        /// Copy a stream into the buffer until it ends.
        /// </summary>
        /// <param name="stream">Stream.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task PumpAsync(Stream stream, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(stream);
            byte[] chunk = new byte[16384];
            while (true)
            {
                int read = await stream.ReadAsync(chunk.AsMemory(0, chunk.Length), token).ConfigureAwait(false);
                if (read <= 0) return;
                Write(chunk, 0, read);
            }
        }

        /// <summary>
        /// The held bytes decoded as UTF-8 (a partial leading character is dropped).
        /// </summary>
        /// <returns>Text.</returns>
        public string Text()
        {
            byte[] copy;
            lock (_Lock)
            {
                copy = new byte[_Count];
                for (int i = 0; i < _Count; i++) copy[i] = _Buffer[(_Start + i) % Capacity];
            }
            int skip = 0;
            while (skip < copy.Length && (copy[skip] & 0xC0) == 0x80) skip++;
            return Encoding.UTF8.GetString(copy, skip, copy.Length - skip);
        }

        #endregion
    }
}
