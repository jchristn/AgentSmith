namespace Hugo.Core.Profiles
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Connectors;
    using Hugo.Core.Database;
    using Hugo.Core.Models;
    using Hugo.Core.Security;

    /// <summary>
    /// Loads the bindings of a profile that a principal can use: enabled bindings whose resource exists, is active,
    /// is visible to the principal, and has a registered connector. Ordered by family, then alias (plan section 10.4).
    /// Thread-safe.
    /// </summary>
    public class ProfileBindingResolver
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly ConnectorRegistry _Registry;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database.</param>
        /// <param name="registry">Connector registry.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public ProfileBindingResolver(DatabaseDriverBase database, ConnectorRegistry registry)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Usable bindings of a profile.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="profileId">Profile identifier.</param>
        /// <param name="principal">Principal for visibility, or null for an internal caller.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Bound resources.</returns>
        public async Task<List<BoundResource>> ResolveAsync(string tenantId, string profileId, RequestContext? principal, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(profileId)) throw new ArgumentNullException(nameof(profileId));
            List<Binding> bindings = await _Database.Bindings.ListByProfileAsync(tenantId, profileId, token).ConfigureAwait(false);
            List<BoundResource> bound = new List<BoundResource>();
            foreach (Binding binding in bindings.Where(b => b.Enabled))
            {
                Resource? resource = await _Database.Resources.ReadAsync(tenantId, binding.ResourceId, token).ConfigureAwait(false);
                if (resource == null || !resource.Active || !ResourceVisibility.CanSee(resource, principal)) continue;
                IConnector? connector = _Registry.Get(resource.ConnectorType);
                if (connector == null) continue;
                BoundResource item = new BoundResource();
                item.Binding = binding;
                item.Resource = resource;
                item.Connector = connector;
                bound.Add(item);
            }
            return bound.OrderBy(b => b.Family, StringComparer.Ordinal).ThenBy(b => b.Binding.Alias, StringComparer.Ordinal).ToList();
        }

        #endregion
    }
}
