namespace Hugo.Cli.Mcp
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Cli.Api;

    /// <summary>
    /// Holds the session token the relay presents and renews it. With an access key and secret key, a missing token
    /// is created on first use and a rejected token (HTTP 401) is replaced by signing in again; concurrent requests
    /// that were rejected with the same token share one renewal. Without keys, a rejected token cannot be renewed.
    /// Thread-safe.
    /// </summary>
    public class RelayAuthenticator : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// The token currently presented, or null.
        /// </summary>
        public string? CurrentToken
        {
            get
            {
                return _Token;
            }
        }

        /// <summary>
        /// Whether a rejected token can be renewed (an access key and secret key are available).
        /// </summary>
        public bool CanRenew
        {
            get
            {
                return !String.IsNullOrEmpty(_AccessKey) && !String.IsNullOrEmpty(_SecretKey);
            }
        }

        /// <summary>
        /// Number of tokens obtained by signing in with the keys.
        /// </summary>
        public int Renewals
        {
            get
            {
                return _Renewals;
            }
        }

        /// <summary>
        /// Called after a new token was obtained (for example to write it to the login cache), or null. Exceptions it
        /// throws are reported through <see cref="Log"/> and otherwise ignored.
        /// </summary>
        public Func<TokenResponse, CancellationToken, Task>? TokenRenewed { get; set; } = null;

        /// <summary>
        /// Diagnostic sink (standard error), or null.
        /// </summary>
        public Action<string>? Log { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly HugoApiClient _Api;
        private readonly string? _AccessKey;
        private readonly string? _SecretKey;
        private readonly string? _TenantId;
        private readonly SemaphoreSlim _Lock = new SemaphoreSlim(1, 1);
        private volatile string? _Token;
        private int _Renewals = 0;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="api">REST client for the server.</param>
        /// <param name="token">Initial session token, or null.</param>
        /// <param name="accessKey">Access key for renewal, or null.</param>
        /// <param name="secretKey">Secret key for renewal, or null.</param>
        /// <param name="tenantId">Tenant ID sent with renewals, or null.</param>
        /// <exception cref="ArgumentNullException">Thrown when api is null.</exception>
        public RelayAuthenticator(HugoApiClient api, string? token, string? accessKey, string? secretKey, string? tenantId)
        {
            _Api = api ?? throw new ArgumentNullException(nameof(api));
            _Token = String.IsNullOrEmpty(token) ? null : token;
            _AccessKey = accessKey;
            _SecretKey = secretKey;
            _TenantId = tenantId;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The token to present, signing in with the keys first when there is no token yet.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Token, or null when none is available.</returns>
        public async Task<string?> GetTokenAsync(CancellationToken token)
        {
            string? current = _Token;
            if (current != null || !CanRenew) return current;
            await RenewAsync(null, token).ConfigureAwait(false);
            return _Token;
        }

        /// <summary>
        /// Replace a rejected token. When another caller already replaced it, returns true without signing in again.
        /// </summary>
        /// <param name="rejectedToken">The token the server rejected, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a different token is now available.</returns>
        public async Task<bool> RenewAsync(string? rejectedToken, CancellationToken token)
        {
            await _Lock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                string? current = _Token;
                if (current != null && !String.Equals(current, rejectedToken, StringComparison.Ordinal)) return true;
                if (!CanRenew)
                {
                    Log?.Invoke("The session token was rejected and cannot be renewed (no access key). Run 'hugo login' again or pass --access-key and --secret-key.");
                    return false;
                }

                TokenResponse issued;
                try
                {
                    issued = await _Api.LoginWithAccessKeyAsync(_AccessKey!, _SecretKey!, _TenantId, token).ConfigureAwait(false);
                }
                catch (HugoApiException e)
                {
                    Log?.Invoke("Signing in with the access key failed (HTTP " + e.StatusCode + "): " + e.Message);
                    return false;
                }
                catch (CliException e)
                {
                    Log?.Invoke(e.Message);
                    return false;
                }

                _Token = issued.Token;
                Interlocked.Increment(ref _Renewals);
                Log?.Invoke(rejectedToken == null ? "Signed in with the access key." : "Renewed the session token with the access key.");
                if (TokenRenewed != null)
                {
                    try
                    {
                        await TokenRenewed(issued, token).ConfigureAwait(false);
                    }
                    catch (Exception e) when (!(e is OperationCanceledException))
                    {
                        Log?.Invoke("Could not save the renewed token: " + e.Message);
                    }
                }
                return true;
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
