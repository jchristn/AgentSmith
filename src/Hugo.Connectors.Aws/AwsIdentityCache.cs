namespace Hugo.Connectors.Aws
{
    using System;
    using System.Collections.Concurrent;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Process-wide cache of resolved caller identities, keyed by the resource session key (which includes the
    /// configuration hash). Classification is synchronous and cannot call AWS, so approval summaries read the account
    /// from here. Holds no secrets. Thread-safe.
    /// </summary>
    public static class AwsIdentityCache
    {
        #region Public-Members

        /// <summary>
        /// Most identities kept before the cache is cleared. Default 4096, minimum 16, maximum 1048576.
        /// </summary>
        public static int Capacity
        {
            get
            {
                return _Capacity;
            }
            set
            {
                _Capacity = Math.Clamp(value, 16, 1048576);
            }
        }

        #endregion

        #region Private-Members

        private static readonly ConcurrentDictionary<string, AwsCallerIdentity> _Identities = new ConcurrentDictionary<string, AwsCallerIdentity>(StringComparer.Ordinal);
        private static int _Capacity = 4096;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Remember the identity of a resource.
        /// </summary>
        /// <param name="resource">Resource.</param>
        /// <param name="identity">Identity.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static void Set(ConnectorResourceContext resource, AwsCallerIdentity identity)
        {
            ArgumentNullException.ThrowIfNull(resource);
            ArgumentNullException.ThrowIfNull(identity);
            if (_Identities.Count >= _Capacity) _Identities.Clear();
            _Identities[resource.SessionKey()] = identity;
        }

        /// <summary>
        /// Identity of a resource, or null when not yet resolved.
        /// </summary>
        /// <param name="resource">Resource, or null.</param>
        /// <returns>Identity or null.</returns>
        public static AwsCallerIdentity? Get(ConnectorResourceContext? resource)
        {
            if (resource == null) return null;
            return _Identities.TryGetValue(resource.SessionKey(), out AwsCallerIdentity? identity) ? identity : null;
        }

        /// <summary>
        /// Forget the identity of a resource.
        /// </summary>
        /// <param name="resource">Resource.</param>
        public static void Remove(ConnectorResourceContext? resource)
        {
            if (resource == null) return;
            _Identities.TryRemove(resource.SessionKey(), out AwsCallerIdentity? _);
        }

        #endregion
    }
}
