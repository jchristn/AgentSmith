namespace Hugo.Core.Secrets
{
    using System;
    using System.Security.Cryptography;
    using System.Text;

    /// <summary>
    /// Reads the SSH wire encoding (RFC 4251: uint32, string, mpint) from a byte array. Not thread-safe.
    /// </summary>
    internal class SshWireReader
    {
        #region Public-Members

        /// <summary>
        /// Whether every byte has been read.
        /// </summary>
        public bool AtEnd
        {
            get
            {
                return _Position >= _Data.Length;
            }
        }

        #endregion

        #region Private-Members

        private readonly byte[] _Data;
        private int _Position = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="data">Data.</param>
        /// <exception cref="ArgumentNullException">Thrown when data is null.</exception>
        public SshWireReader(byte[] data)
        {
            _Data = data ?? throw new ArgumentNullException(nameof(data));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Read raw bytes.
        /// </summary>
        /// <param name="count">Byte count.</param>
        /// <returns>Bytes.</returns>
        /// <exception cref="CryptographicException">Thrown when the data is truncated.</exception>
        public byte[] ReadBytes(int count)
        {
            if (count < 0 || _Position + count > _Data.Length) throw new CryptographicException("The SSH key data is truncated.");
            byte[] result = new byte[count];
            Buffer.BlockCopy(_Data, _Position, result, 0, count);
            _Position += count;
            return result;
        }

        /// <summary>
        /// Read a big-endian uint32.
        /// </summary>
        /// <returns>Value.</returns>
        /// <exception cref="CryptographicException">Thrown when the data is truncated or the value exceeds Int32.MaxValue.</exception>
        public int ReadUInt32()
        {
            byte[] b = ReadBytes(4);
            uint value = ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
            if (value > Int32.MaxValue) throw new CryptographicException("The SSH key data has an invalid length.");
            return (int)value;
        }

        /// <summary>
        /// Read a length-prefixed byte string.
        /// </summary>
        /// <returns>Bytes.</returns>
        public byte[] ReadString()
        {
            int length = ReadUInt32();
            return ReadBytes(length);
        }

        /// <summary>
        /// Read a length-prefixed UTF-8 string.
        /// </summary>
        /// <returns>Text.</returns>
        public string ReadText()
        {
            return Encoding.UTF8.GetString(ReadString());
        }

        #endregion
    }
}
