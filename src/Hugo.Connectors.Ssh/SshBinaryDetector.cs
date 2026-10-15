namespace Hugo.Connectors.Ssh
{
    using System;
    using System.Text;

    /// <summary>
    /// Decides whether a chunk of file content is text that can be shown as UTF-8 or binary that must be shown as
    /// base64. Pure and thread-safe.
    /// </summary>
    public static class SshBinaryDetector
    {
        #region Private-Members

        private static readonly UTF8Encoding _StrictUtf8 = new UTF8Encoding(false, true);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether the bytes look binary: they contain a NUL byte, or they are not valid UTF-8. A chunk cut out of a
        /// larger file may start or end in the middle of a multi-byte character, so up to three continuation bytes at
        /// the start and one incomplete sequence at the end are ignored.
        /// </summary>
        /// <param name="data">Bytes to inspect. Null or empty is text.</param>
        /// <returns>True when the bytes should be treated as binary.</returns>
        public static bool LooksBinary(byte[]? data)
        {
            if (data == null || data.Length == 0) return false;
            if (Array.IndexOf(data, (byte)0) >= 0) return true;

            int start = 0;
            while (start < data.Length && start < 3 && (data[start] & 0xC0) == 0x80) start++;
            int end = data.Length;
            int back = 0;
            while (back < 3 && end - back - 1 >= start && (data[end - back - 1] & 0xC0) == 0x80) back++;
            int lead = end - back - 1;
            if (lead >= start)
            {
                int expected = SequenceLength(data[lead]);
                if (expected > 1 && back + 1 < expected) end = lead;
            }

            try
            {
                _StrictUtf8.GetCharCount(data, start, end - start);
                return false;
            }
            catch (DecoderFallbackException)
            {
                return true;
            }
        }

        #endregion

        #region Private-Methods

        private static int SequenceLength(byte lead)
        {
            if ((lead & 0x80) == 0) return 1;
            if ((lead & 0xE0) == 0xC0) return 2;
            if ((lead & 0xF0) == 0xE0) return 3;
            if ((lead & 0xF8) == 0xF0) return 4;
            return 0;
        }

        #endregion
    }
}
