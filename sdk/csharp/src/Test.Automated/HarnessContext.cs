namespace Test.Automated
{
    using System;
    using System.Globalization;
    using System.Security.Cryptography;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Sdk;
    using Hugo.Sdk.Models;
    using Hugo.Sdk.Requests;
    using Hugo.Sdk.Responses;

    /// <summary>
    /// Shared harness state: an administrator client, a freshly provisioned tenant, and a tenant administrator
    /// ("owner") client in that tenant. Disposing deletes the tenant.
    /// </summary>
    public class HarnessContext : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Environment.
        /// </summary>
        public TestEnvironment Environment { get; }

        /// <summary>
        /// System administrator client (signed in to the administrator's tenant).
        /// </summary>
        public HugoClient Admin { get; }

        /// <summary>
        /// Tenant administrator client in the provisioned tenant.
        /// </summary>
        public HugoClient Owner { get; }

        /// <summary>
        /// Provisioned tenant identifier.
        /// </summary>
        public string TenantId { get; private set; } = String.Empty;

        /// <summary>
        /// Owner email.
        /// </summary>
        public string OwnerEmail { get; private set; } = "owner@sdk-csharp.test";

        /// <summary>
        /// Owner password.
        /// </summary>
        public string OwnerPassword { get; private set; } = String.Empty;

        /// <summary>
        /// Owner user identifier.
        /// </summary>
        public string OwnerUserId { get; private set; } = String.Empty;

        /// <summary>
        /// Unique run suffix for names.
        /// </summary>
        public string RunId { get; }

        /// <summary>
        /// 429 retries for harness clients. Default 4 (each waits Retry-After, 60 seconds for the auth limiter).
        /// </summary>
        public int RateLimitRetries { get; set; } = 4;

        #endregion

        #region Private-Members

        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="environment">Environment.</param>
        public HarnessContext(TestEnvironment environment)
        {
            Environment = environment ?? throw new ArgumentNullException(nameof(environment));
            Admin = NewClient();
            Owner = NewClient();
            RunId = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "-" + RandomHex(2);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Sign in the administrator, create a tenant and its owner, and sign in the owner. A fresh server reports
        /// mustChangePassword for the seeded administrator; the flag is advisory, so the harness keeps going.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task SetupAsync(CancellationToken token = default)
        {
            await Admin.LoginAsync(Environment.AdminEmail, Environment.AdminPassword, Environment.AdminTenantId, token).ConfigureAwait(false);

            TenantRequest tenantRequest = new TenantRequest();
            tenantRequest.Name = "sdk-csharp-" + RunId;
            tenantRequest.Description = "Hugo C# SDK harness tenant (deleted at the end of the run).";
            Tenant tenant = await Admin.Tenants.CreateAsync(tenantRequest, token).ConfigureAwait(false);
            TenantId = tenant.Id;

            OwnerPassword = "Owner-" + RandomHex(8);
            HugoClient scoped = ForTenant(Admin, TenantId);
            UserRequest ownerRequest = new UserRequest();
            ownerRequest.Email = OwnerEmail;
            ownerRequest.Password = OwnerPassword;
            ownerRequest.FirstName = "Sdk";
            ownerRequest.LastName = "Owner";
            ownerRequest.IsTenantAdmin = true;
            ownerRequest.MustChangePassword = false;
            User owner = await scoped.Users.CreateAsync(ownerRequest, token).ConfigureAwait(false);
            OwnerUserId = owner.Id;

            await Owner.LoginAsync(OwnerEmail, OwnerPassword, TenantId, token).ConfigureAwait(false);
        }

        /// <summary>
        /// A new client sharing nothing with the others. It retries 429 responses (honoring Retry-After) so the run
        /// fits the server's default authentication rate limit (Auth.RateLimitPerMinute = 10).
        /// </summary>
        /// <returns>Client.</returns>
        public HugoClient NewClient()
        {
            HugoClient client = new HugoClient(Environment.ServerUrl);
            client.Transport.RateLimitRetries = RateLimitRetries;
            return client;
        }

        /// <summary>
        /// A client that reuses another client's token but addresses a different tenant (system administrators).
        /// </summary>
        /// <param name="source">Signed-in client.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <returns>Client.</returns>
        public HugoClient ForTenant(HugoClient source, string tenantId)
        {
            HugoClient client = NewClient();
            client.UseToken(source.Token ?? throw new InvalidOperationException("The source client is not signed in."), tenantId, source.Transport.TokenExpiresUtc);
            return client;
        }

        /// <summary>
        /// Random lowercase hex.
        /// </summary>
        /// <param name="bytes">Number of random bytes.</param>
        /// <returns>Hex text.</returns>
        public static string RandomHex(int bytes)
        {
            return Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();
        }

        /// <summary>
        /// Delete the provisioned tenant (best effort).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task CleanupAsync(CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(TenantId)) return;
            try
            {
                await Admin.Tenants.DeleteAsync(TenantId, token).ConfigureAwait(false);
                Console.WriteLine("Deleted tenant " + TenantId);
                TenantId = String.Empty;
            }
            catch (HugoException e)
            {
                Console.WriteLine("Cleanup failed for tenant " + TenantId + ": " + e.Message);
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
        /// Dispose pattern implementation.
        /// </summary>
        /// <param name="disposing">True when called from Dispose.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing)
            {
                Admin.Dispose();
                Owner.Dispose();
            }
            _Disposed = true;
        }

        #endregion
    }
}
