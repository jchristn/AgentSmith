namespace Hugo.Core.Secrets
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Models;

    /// <summary>
    /// Finds records that reference a secret. Later phases register one provider per referencing entity (resource secret
    /// slot bindings, model endpoint API keys); the secret service asks every registered provider before a delete and
    /// for the referrers view.
    /// </summary>
    public interface ISecretReferrerProvider
    {
        /// <summary>
        /// Find referrers of a secret within a tenant.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="secretId">Secret identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Referrers (empty when none).</returns>
        Task<List<SecretReferrer>> FindAsync(string tenantId, string secretId, CancellationToken token = default);
    }
}
