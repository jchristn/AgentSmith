namespace Hugo.Connectors.Ssh
{
    using System;
    using System.Text;

    /// <summary>
    /// Fixed-capacity byte buffer that keeps the most recent bytes written to it and counts the total, so command output
    /// is bounded in memory however much the remote side produces. Thread-safe.
    /// </summary>
    public class SshTailBuffer
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
        /// <param name="capacity">Capacity in bytes. Values below 1 become 1.</param>
        public SshTailBuffer(int capacity)
        {
            Capacity = Math.Max(1, capacity);
            _Buffer = new byte[Capacity];
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Append bytes.
        /// </summary>
        /// <param name="data">Source.</param>
        /// <param name="offset">Offset.</param>
        /// <param name="count">Count.</param>
        /// <exception cref="ArgumentNullException">Thrown when data is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the range is outside the array.</exception>
        public void Write(byte[] data, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(data);
            if (offset < 0 || count < 0 || offset + count > data.Length) throw new ArgumentOutOfRangeException(nameof(count));
            lock (_Lock)
            {
                _Total += count;
                if (count >= Capacity)
                {
                    Array.Copy(data, offset + count - Capacity, _Buffer, 0, Capacity);
                    _Start = 0;
                    _Count = Capacity;
                    return;
                }
                for (int i = 0; i < count; i++)
                {
                    int pos = (_Start + _Count) % Capacity;
                    _Buffer[pos] = data[offset + i];
                    if (_Count < Capacity) _Count++;
                    else _Start = (_Start + 1) % Capacity;
                }
            }
        }

        /// <summary>
        /// Copy the held bytes in order.
        /// </summary>
        /// <returns>Bytes.</returns>
        public byte[] ToArray()
        {
            lock (_Lock)
            {
                byte[] result = new byte[_Count];
                for (int i = 0; i < _Count; i++) result[i] = _Buffer[(_Start + i) % Capacity];
                return result;
            }
        }

        /// <summary>
        /// Decode the held bytes as UTF-8, skipping a partial character at the start when bytes were dropped.
        /// </summary>
        /// <returns>Text.</returns>
        public override string ToString()
        {
            byte[] bytes = ToArray();
            int start = 0;
            if (Dropped)
            {
                while (start < bytes.Length && (bytes[start] & 0xC0) == 0x80) start++;
            }
            return Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
        }

        #endregion
    }
}
