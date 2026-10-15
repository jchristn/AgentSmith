namespace Hugo.Connectors.Ssh
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using Hugo.Connectors.Abstractions;
    using Renci.SshNet;
    using Renci.SshNet.Common;

    /// <summary>
    /// Parsed credential material for one SSH endpoint (target or bastion). Builds a fresh <see cref="ConnectionInfo"/>
    /// for every connection so SSH and SFTP clients never share authentication state. Holds key material in memory for
    /// the life of a pooled session; disposing it releases the key and every authentication method it created.
    /// Thread-safe for <see cref="BuildConnectionInfo"/>.
    /// </summary>
    public class SshCredential : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Login user name.
        /// </summary>
        public string Username { get; }

        /// <summary>
        /// Whether the credential uses a private key (otherwise a password).
        /// </summary>
        public bool UsesPrivateKey
        {
            get
            {
                return _Key != null;
            }
        }

        #endregion

        #region Private-Members

        private string? _Password;
        private PrivateKeyFile? _Key;
        private readonly List<IDisposable> _Created = new List<IDisposable>();
        private readonly object _Lock = new object();
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        private SshCredential(string username, string? password, PrivateKeyFile? key)
        {
            Username = username;
            _Password = password;
            _Key = key;
        }

        /// <summary>
        /// Build a credential from the resource's secret slots.
        /// </summary>
        /// <param name="secrets">Secrets.</param>
        /// <param name="username">User name.</param>
        /// <param name="passwordSlot">Password slot name.</param>
        /// <param name="keySlot">Private key slot name.</param>
        /// <param name="authMethod">Required method (<c>Password</c> or <c>PrivateKey</c>), or null to prefer a key and fall back to a password.</param>
        /// <param name="label">Endpoint label for messages (for example <c>target</c> or <c>jump host</c>).</param>
        /// <returns>Credential.</returns>
        /// <exception cref="ArgumentNullException">Thrown when secrets is null.</exception>
        /// <exception cref="ConnectorException">Thrown with <c>auth_failed</c> when the required secret is missing or the key cannot be read.</exception>
        public static SshCredential FromSecrets(SecretAccessor secrets, string username, string passwordSlot, string keySlot, string? authMethod, string label)
        {
            ArgumentNullException.ThrowIfNull(secrets);
            if (String.IsNullOrWhiteSpace(username)) throw new ConnectorException("invalid_argument", "No user name is configured for the " + label + ".");

            string? keyText = secrets.Get(keySlot, SshSettingKeys.PrivateKeyField);
            string? passphrase = secrets.Get(keySlot, SshSettingKeys.PassphraseField);
            string? password = secrets.Get(passwordSlot, SshSettingKeys.PasswordField);

            bool wantKey;
            if (String.Equals(authMethod, SshSettingKeys.AuthMethodPassword, StringComparison.OrdinalIgnoreCase)) wantKey = false;
            else if (String.Equals(authMethod, SshSettingKeys.AuthMethodPrivateKey, StringComparison.OrdinalIgnoreCase)) wantKey = true;
            else wantKey = !String.IsNullOrEmpty(keyText);

            if (wantKey)
            {
                if (String.IsNullOrWhiteSpace(keyText)) throw new ConnectorException("auth_failed", "No private key secret is bound to the '" + keySlot + "' slot for the " + label + ".");
                PrivateKeyFile key = ParseKey(keyText, passphrase, label);
                return new SshCredential(username, null, key);
            }

            if (String.IsNullOrEmpty(password)) throw new ConnectorException("auth_failed", "No password secret is bound to the '" + passwordSlot + "' slot for the " + label + ".");
            return new SshCredential(username, password, null);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Try to parse private key text without keeping it.
        /// </summary>
        /// <param name="keyText">PEM or OpenSSH private key text.</param>
        /// <param name="passphrase">Optional passphrase.</param>
        /// <param name="error">Error message when parsing fails; never contains key material.</param>
        /// <returns>True when the key parsed.</returns>
        public static bool TryValidateKey(string? keyText, string? passphrase, out string error)
        {
            error = String.Empty;
            if (String.IsNullOrWhiteSpace(keyText))
            {
                error = "The private key is empty.";
                return false;
            }
            try
            {
                using (PrivateKeyFile key = ParseKey(keyText, passphrase, "resource"))
                {
                    return true;
                }
            }
            catch (ConnectorException e)
            {
                error = e.Message;
                return false;
            }
        }

        /// <summary>
        /// Build connection information for one new connection.
        /// </summary>
        /// <param name="host">Host to connect to.</param>
        /// <param name="port">Port to connect to.</param>
        /// <param name="timeout">Per-operation timeout.</param>
        /// <returns>Connection information.</returns>
        /// <exception cref="ObjectDisposedException">Thrown after disposal.</exception>
        public ConnectionInfo BuildConnectionInfo(string host, int port, TimeSpan timeout)
        {
            lock (_Lock)
            {
                if (_Disposed) throw new ObjectDisposedException(nameof(SshCredential));
                List<AuthenticationMethod> methods = new List<AuthenticationMethod>();
                if (_Key != null)
                {
                    methods.Add(new PrivateKeyAuthenticationMethod(Username, _Key));
                }
                else
                {
                    string password = _Password ?? String.Empty;
                    methods.Add(new PasswordAuthenticationMethod(Username, password));
                    KeyboardInteractiveAuthenticationMethod interactive = new KeyboardInteractiveAuthenticationMethod(Username);
                    interactive.AuthenticationPrompt += (sender, e) =>
                    {
                        foreach (AuthenticationPrompt prompt in e.Prompts) prompt.Response = password;
                    };
                    methods.Add(interactive);
                }
                foreach (AuthenticationMethod m in methods)
                {
                    if (m is IDisposable d) _Created.Add(d);
                }
                ConnectionInfo info = new ConnectionInfo(host, port, Username, methods.ToArray());
                info.Timeout = timeout;
                return info;
            }
        }

        /// <summary>
        /// Release the key and authentication methods.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Release the key and authentication methods.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            lock (_Lock)
            {
                if (_Disposed) return;
                if (disposing)
                {
                    foreach (IDisposable d in _Created)
                    {
                        try
                        {
                            d.Dispose();
                        }
                        catch (ObjectDisposedException)
                        {
                        }
                    }
                    _Created.Clear();
                    _Key?.Dispose();
                    _Key = null;
                    _Password = null;
                }
                _Disposed = true;
            }
        }

        #endregion

        #region Private-Methods

        private static PrivateKeyFile ParseKey(string keyText, string? passphrase, string label)
        {
            try
            {
                string normalized = keyText.Replace("\r\n", "\n").Trim() + "\n";
                using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(normalized)))
                {
                    return String.IsNullOrEmpty(passphrase) ? new PrivateKeyFile(stream) : new PrivateKeyFile(stream, passphrase);
                }
            }
            catch (SshPassPhraseNullOrEmptyException)
            {
                throw new ConnectorException("auth_failed", "The private key for the " + label + " is encrypted and needs a passphrase.");
            }
            catch (Exception e) when (e is SshException || e is ArgumentException || e is FormatException || e is InvalidOperationException || e is NotSupportedException || e is System.Security.Cryptography.CryptographicException)
            {
                throw new ConnectorException("auth_failed", "The private key for the " + label + " could not be read. Check the key format and the passphrase.");
            }
        }

        #endregion
    }
}
