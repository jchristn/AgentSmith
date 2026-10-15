namespace Hugo.Connectors.Ssh
{
    using System;
    using Renci.SshNet.Common;

    /// <summary>
    /// Host key verification for one connection attempt (D-028). When an expected fingerprint is set, any other key
    /// is refused, which makes SSH.NET abort the key exchange. When none is set, the key is accepted and recorded so the
    /// caller can pin it (trust on first use). Thread-safe for the single handshake it serves.
    /// </summary>
    public class SshHostKeyCheck
    {
        #region Public-Members

        /// <summary>
        /// Expected normalized fingerprint, or null for trust on first use.
        /// </summary>
        public string? Expected { get; }

        /// <summary>
        /// Fingerprint the server presented (<c>SHA256:...</c>), or null before the handshake.
        /// </summary>
        public string? Observed
        {
            get
            {
                return _Observed;
            }
        }

        /// <summary>
        /// Host key algorithm the server presented (for example <c>ssh-ed25519</c>), or null before the handshake.
        /// </summary>
        public string? Algorithm
        {
            get
            {
                return _Algorithm;
            }
        }

        /// <summary>
        /// Whether the server presented a key that did not match <see cref="Expected"/>.
        /// </summary>
        public bool Mismatch
        {
            get
            {
                return _Mismatch;
            }
        }

        #endregion

        #region Private-Members

        private volatile string? _Observed = null;
        private volatile string? _Algorithm = null;
        private volatile bool _Mismatch = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="expected">Expected fingerprint in any accepted format, or null or empty for trust on first use.</param>
        public SshHostKeyCheck(string? expected)
        {
            Expected = SshFingerprint.Normalize(expected);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Handler for <c>BaseClient.HostKeyReceived</c>.
        /// </summary>
        /// <param name="sender">Sender.</param>
        /// <param name="e">Event arguments.</param>
        public void OnHostKeyReceived(object? sender, HostKeyEventArgs e)
        {
            if (e == null) return;
            string observed = "SHA256:" + (e.FingerPrintSHA256 ?? String.Empty).TrimEnd('=');
            _Observed = observed;
            _Algorithm = e.HostKeyName;
            if (Expected == null)
            {
                e.CanTrust = true;
                return;
            }
            bool match = SshFingerprint.AreEqual(Expected, observed);
            _Mismatch = !match;
            e.CanTrust = match;
        }

        #endregion
    }
}
