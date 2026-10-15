namespace Hugo.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Database;
    using Hugo.Core.Models;
    using Hugo.Core.Secrets;

    /// <summary>
    /// Reports resources and model endpoints that reference a secret, so the vault refuses to delete a secret in use
    /// (409 with the referrers, plan sections 7.7 and 12.2). Thread-safe.
    /// </summary>
    public class ResourceSecretReferrerProvider : ISecretReferrerProvider
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Database;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database.</param>
        /// <exception cref="ArgumentNullException">Thrown when database is null.</exception>
        public ResourceSecretReferrerProvider(DatabaseDriverBase database)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<List<SecretReferrer>> FindAsync(string tenantId, string secretId, CancellationToken token = default)
        {
            List<SecretReferrer> referrers = new List<SecretReferrer>();
            foreach (Resource resource in await _Database.Resources.ListBySecretAsync(tenantId, secretId, token).ConfigureAwait(false))
            {
                SecretReferrer referrer = new SecretReferrer();
                referrer.ReferrerType = "Resource";
                referrer.ReferrerId = resource.Id;
                referrer.ReferrerName = resource.Name;
                referrer.Detail = resource.ConnectorType;
                referrers.Add(referrer);
            }
            foreach (ModelEndpoint endpoint in await _Database.ModelEndpoints.ListBySecretAsync(tenantId, secretId, token).ConfigureAwait(false))
            {
                SecretReferrer referrer = new SecretReferrer();
                referrer.ReferrerType = "ModelEndpoint";
                referrer.ReferrerId = endpoint.Id;
                referrer.ReferrerName = endpoint.Name;
                referrer.Detail = endpoint.Provider + " " + endpoint.Model;
                referrers.Add(referrer);
            }
            return referrers;
        }

        #endregion
    }
}
