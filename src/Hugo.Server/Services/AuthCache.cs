namespace Hugo.Server.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Linq;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Database;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Security;
    using Hugo.Core.Telemetry;

    /// <summary>
    /// Short-lived cache of the records every authenticated request reads (P10.24): user and credential sessions,
    /// tenants, users, and credentials. Run-token sessions are never cached, and failed lookups are never cached, so a new
    /// record works at once. Invalidation is explicit: any change to tenants, users, credentials, roles, permissions, or
    /// assignments made through the API clears the cache on every replica (through <see cref="AuthorizationService"/>
    /// and its PermissionsChanged backplane message), and revoking a session drops it on every replica (a SessionRevoked
    /// message). <see cref="CacheSeconds"/> bounds how long a change made any other way, or a missed backplane message,
    /// can go unnoticed. Callers get copies, never the cached instances. Thread-safe.
    /// </summary>
    public class AuthCache
    {
        #region Public-Members

        /// <summary>
        /// Seconds a record stays cached. Minimum 0 (no cache: every lookup reads the database), maximum 60, default 5.
        /// Refreshed from the cluster setting Auth.CacheSeconds.
        /// </summary>
        public int CacheSeconds
        {
            get
            {
                return Volatile.Read(ref _CacheSeconds);
            }
            set
            {
                int clamped = Math.Clamp(value, 0, 60);
                int previous = Interlocked.Exchange(ref _CacheSeconds, clamped);
                if (clamped < previous) ClearLocal();
            }
        }

        /// <summary>
        /// Most records kept per kind; expired records are dropped beyond it, and the kind is cleared if that is not
        /// enough. Minimum 100, maximum 1000000, default 10000.
        /// </summary>
        public int MaxEntries
        {
            get
            {
                return Volatile.Read(ref _MaxEntries);
            }
            set
            {
                Volatile.Write(ref _MaxEntries, Math.Clamp(value, 100, 1000000));
            }
        }


        /// <summary>
        /// Lookups answered from the cache.
        /// </summary>
        public long Hits
        {
            get
            {
                return Interlocked.Read(ref _Hits);
            }
        }

        /// <summary>
        /// Lookups that read the database.
        /// </summary>
        public long Misses
        {
            get
            {
                return Interlocked.Read(ref _Misses);
            }
        }

        /// <summary>
        /// Times the whole cache was cleared.
        /// </summary>
        public long Clears
        {
            get
            {
                return Interlocked.Read(ref _Clears);
            }
        }

        #endregion

        #region Private-Members

        private static readonly MethodInfo _MemberwiseClone = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;

        private readonly DatabaseDriverBase _Database;
        private readonly ConcurrentDictionary<string, AuthCacheEntry<AuthSession>> _Sessions = new ConcurrentDictionary<string, AuthCacheEntry<AuthSession>>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, AuthCacheEntry<Tenant>> _Tenants = new ConcurrentDictionary<string, AuthCacheEntry<Tenant>>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, AuthCacheEntry<User>> _Users = new ConcurrentDictionary<string, AuthCacheEntry<User>>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, AuthCacheEntry<Credential>> _Credentials = new ConcurrentDictionary<string, AuthCacheEntry<Credential>>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, AuthCacheEntry<Credential>> _AccessKeys = new ConcurrentDictionary<string, AuthCacheEntry<Credential>>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, DateTime> _Touched = new ConcurrentDictionary<string, DateTime>(StringComparer.Ordinal);
        private readonly object _StoreLock = new object();
        private int _CacheSeconds = 5;
        private int _MaxEntries = 10000;
        private long _Generation = 0;
        private long _Hits = 0;
        private long _Misses = 0;
        private long _Clears = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database.</param>
        /// <exception cref="ArgumentNullException">Thrown when database is null.</exception>
        public AuthCache(DatabaseDriverBase database)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Read the session a token names, by session identifier, and check that it carries the token's identifier.
        /// Sessions of users and credentials are cached; run-token sessions are always read from the database.
        /// </summary>
        /// <param name="sessionId">Session identifier from the validated token.</param>
        /// <param name="tokenId">Token identifier from the validated token.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A copy of the session, or null when there is none for this token.</returns>
        /// <exception cref="ArgumentNullException">Thrown when sessionId or tokenId is null or empty.</exception>
        public async Task<AuthSession?> SessionAsync(string sessionId, string tokenId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(sessionId)) throw new ArgumentNullException(nameof(sessionId));
            if (String.IsNullOrEmpty(tokenId)) throw new ArgumentNullException(nameof(tokenId));
            AuthSession? cached = Get(_Sessions, sessionId, "session");
            if (cached != null) return PasswordHasher.FixedTimeEquals(cached.TokenId, tokenId) ? cached : null;

            long generation = Interlocked.Read(ref _Generation);
            AuthSession? session = await _Database.Sessions.ReadByTokenIdAsync(tokenId, token).ConfigureAwait(false);
            if (session == null || session.Id != sessionId) return null;
            if (session.PrincipalType == PrincipalTypeEnum.User || session.PrincipalType == PrincipalTypeEnum.Credential) Put(_Sessions, session.Id, session, generation);
            return session;
        }

        /// <summary>
        /// Read a tenant.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A copy of the tenant, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when tenantId is null or empty.</exception>
        public async Task<Tenant?> TenantAsync(string tenantId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            Tenant? cached = Get(_Tenants, tenantId, "tenant");
            if (cached != null) return cached;

            long generation = Interlocked.Read(ref _Generation);
            Tenant? tenant = await _Database.Tenants.ReadAsync(tenantId, token).ConfigureAwait(false);
            if (tenant != null) Put(_Tenants, tenant.Id, tenant, generation);
            return tenant;
        }

        /// <summary>
        /// Read a user of a tenant.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="userId">User identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A copy of the user, or null when there is none in that tenant.</returns>
        /// <exception cref="ArgumentNullException">Thrown when tenantId or userId is null or empty.</exception>
        public async Task<User?> UserAsync(string tenantId, string userId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            User? cached = Get(_Users, userId, "user");
            if (cached != null) return String.Equals(cached.TenantId, tenantId, StringComparison.Ordinal) ? cached : null;

            long generation = Interlocked.Read(ref _Generation);
            User? user = await _Database.Users.ReadAsync(tenantId, userId, token).ConfigureAwait(false);
            if (user != null) Put(_Users, user.Id, user, generation);
            return user;
        }

        /// <summary>
        /// Read a credential of a tenant.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="credentialId">Credential identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A copy of the credential, or null when there is none in that tenant.</returns>
        /// <exception cref="ArgumentNullException">Thrown when tenantId or credentialId is null or empty.</exception>
        public async Task<Credential?> CredentialAsync(string tenantId, string credentialId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(credentialId)) throw new ArgumentNullException(nameof(credentialId));
            Credential? cached = Get(_Credentials, credentialId, "credential");
            if (cached != null) return String.Equals(cached.TenantId, tenantId, StringComparison.Ordinal) ? cached : null;

            long generation = Interlocked.Read(ref _Generation);
            Credential? credential = await _Database.Credentials.ReadAsync(tenantId, credentialId, token).ConfigureAwait(false);
            if (credential != null) Put(_Credentials, credential.Id, credential, generation);
            return credential;
        }

        /// <summary>
        /// Read a credential by its access key.
        /// </summary>
        /// <param name="accessKey">Access key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A copy of the credential, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when accessKey is null or empty.</exception>
        public async Task<Credential?> CredentialByAccessKeyAsync(string accessKey, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(accessKey)) throw new ArgumentNullException(nameof(accessKey));
            Credential? cached = Get(_AccessKeys, accessKey, "credential");
            if (cached != null) return cached;

            long generation = Interlocked.Read(ref _Generation);
            Credential? credential = await _Database.Credentials.ReadByAccessKeyAsync(accessKey, token).ConfigureAwait(false);
            if (credential != null) Put(_AccessKeys, accessKey, credential, generation);
            return credential;
        }

        /// <summary>
        /// Record that a credential was used. With the cache on, the credential's lastUsedUtc is written at most once per
        /// <see cref="CacheSeconds"/> per replica; with it off, on every use.
        /// </summary>
        /// <param name="credentialId">Credential identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">Thrown when credentialId is null or empty.</exception>
        public async Task TouchCredentialAsync(string credentialId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(credentialId)) throw new ArgumentNullException(nameof(credentialId));
            int seconds = CacheSeconds;
            DateTime now = DateTime.UtcNow;
            if (seconds > 0)
            {
                if (_Touched.TryGetValue(credentialId, out DateTime last) && last > now.AddSeconds(-seconds)) return;
                _Touched[credentialId] = now;
                if (_Touched.Count > MaxEntries) _Touched.Clear();
            }
            await _Database.Credentials.TouchLastUsedAsync(credentialId, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Drop a revoked session here and, through the backplane, on every other replica.
        /// </summary>
        /// <param name="sessionId">Session identifier; null or empty is ignored.</param>
        public void InvalidateSession(string? sessionId)
        {
            if (String.IsNullOrEmpty(sessionId)) return;
            InvalidateSessionLocal(sessionId);
        }

        /// <summary>
        /// Drop a session on this replica only (a SessionRevoked message from another replica).
        /// </summary>
        /// <param name="sessionId">Session identifier; null or empty is ignored.</param>
        public void InvalidateSessionLocal(string? sessionId)
        {
            if (String.IsNullOrEmpty(sessionId)) return;
            lock (_StoreLock)
            {
                Interlocked.Increment(ref _Generation);
                _Sessions.TryRemove(sessionId, out AuthCacheEntry<AuthSession>? _);
            }
        }

        /// <summary>
        /// Drop every cached record on this replica (identity, role, or permission changes, here or on another replica,
        /// or a backplane that may have missed messages).
        /// </summary>
        public void ClearLocal()
        {
            lock (_StoreLock)
            {
                Interlocked.Increment(ref _Generation);
                _Sessions.Clear();
                _Tenants.Clear();
                _Users.Clear();
                _Credentials.Clear();
                _AccessKeys.Clear();
            }
            Interlocked.Increment(ref _Clears);
        }

        #endregion

        #region Private-Methods

        private T? Get<T>(ConcurrentDictionary<string, AuthCacheEntry<T>> map, string key, string entity) where T : class
        {
            if (CacheSeconds <= 0) return null;
            if (map.TryGetValue(key, out AuthCacheEntry<T>? entry) && entry.ExpiresUtc > DateTime.UtcNow)
            {
                Interlocked.Increment(ref _Hits);
                HugoMetrics.Count(HugoMetricCatalog.AuthCacheLookups, "entity", entity, "result", "hit");
                return Copy(entry.Value);
            }
            Interlocked.Increment(ref _Misses);
            HugoMetrics.Count(HugoMetricCatalog.AuthCacheLookups, "entity", entity, "result", "miss");
            return null;
        }

        private void Put<T>(ConcurrentDictionary<string, AuthCacheEntry<T>> map, string key, T value, long generation) where T : class
        {
            int seconds = CacheSeconds;
            if (seconds <= 0) return;
            AuthCacheEntry<T> entry = new AuthCacheEntry<T>(Copy(value), DateTime.UtcNow.AddSeconds(seconds));
            lock (_StoreLock)
            {
                // A read that started before an invalidation may have returned the old record; never cache it.
                if (Interlocked.Read(ref _Generation) != generation) return;
                if (map.Count >= MaxEntries)
                {
                    DateTime now = DateTime.UtcNow;
                    foreach (string expired in map.Where(p => p.Value.ExpiresUtc <= now).Select(p => p.Key).ToList()) map.TryRemove(expired, out AuthCacheEntry<T>? _);
                    if (map.Count >= MaxEntries) map.Clear();
                }
                map[key] = entry;
            }
        }

        private static T Copy<T>(T value) where T : class
        {
            // A shallow copy: requests may change their copy without changing what other requests see.
            return (T)_MemberwiseClone.Invoke(value, null)!;
        }

        #endregion
    }
}
