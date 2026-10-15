namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Models;
    using Hugo.Core.Secrets;

    /// <summary>
    /// A referrer provider that reports one resource referencing a given secret.
    /// </summary>
    public class FakeReferrers : ISecretReferrerProvider
    {
        #region Private-Members

        private readonly string _SecretId;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="secretId">Secret the fake resource references.</param>
        public FakeReferrers(string secretId)
        {
            _SecretId = secretId ?? throw new ArgumentNullException(nameof(secretId));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<List<SecretReferrer>> FindAsync(string tenantId, string secretId, CancellationToken token = default)
        {
            List<SecretReferrer> result = new List<SecretReferrer>();
            if (secretId == _SecretId)
            {
                SecretReferrer referrer = new SecretReferrer();
                referrer.ReferrerType = "Resource";
                referrer.ReferrerId = "res_prod";
                referrer.ReferrerName = "res-prod";
                referrer.Detail = "slot accessKey";
                result.Add(referrer);
            }
            return Task.FromResult(result);
        }

        #endregion
    }
}
