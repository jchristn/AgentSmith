namespace Hugo.Connectors.Abstractions
{
    using System;

    /// <summary>
    /// Effective output limits for one call. Connectors must honor them; the server truncates again as a backstop.
    /// </summary>
    public class OutputLimits
    {
        /// <summary>
        /// Maximum bytes of text returned. Minimum 1024, maximum 16 MiB, default 65536.
        /// </summary>
        public int MaxBytes
        {
            get
            {
                return _MaxBytes;
            }
            set
            {
                _MaxBytes = Math.Clamp(value, 1024, 16 * 1024 * 1024);
            }
        }

        /// <summary>
        /// Maximum rows or items returned. Minimum 1, maximum 100000, default 500.
        /// </summary>
        public int MaxRows
        {
            get
            {
                return _MaxRows;
            }
            set
            {
                _MaxRows = Math.Clamp(value, 1, 100000);
            }
        }

        /// <summary>
        /// Maximum lines of log or command output returned. Minimum 1, maximum 100000, default 1000.
        /// </summary>
        public int MaxLines
        {
            get
            {
                return _MaxLines;
            }
            set
            {
                _MaxLines = Math.Clamp(value, 1, 100000);
            }
        }

        private int _MaxBytes = 65536;
        private int _MaxRows = 500;
        private int _MaxLines = 1000;
    }
}
