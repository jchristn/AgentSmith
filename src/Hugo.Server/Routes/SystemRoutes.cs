namespace Hugo.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using Hugo.Core.Enums;
    using Hugo.Core.Secrets;
    using WatsonWebserver.Core;

    /// <summary>
    /// System administration routes (P2.10): master key rotation status and start. System administrators only.
    /// </summary>
    public class SystemRoutes
    {
        #region Private-Members

        private readonly ServerContext _Context;
        private readonly MasterKeyRotationService _Rotation;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="context">Server context.</param>
        /// <param name="rotation">Rotation service.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public SystemRoutes(ServerContext context, MasterKeyRotationService rotation)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
            _Rotation = rotation ?? throw new ArgumentNullException(nameof(rotation));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="routes">Registrar.</param>
        /// <exception cref="ArgumentNullException">Thrown when routes is null.</exception>
        public void Register(RouteRegistrar routes)
        {
            if (routes == null) throw new ArgumentNullException(nameof(routes));
            routes.Scoped(HttpMethod.GET, "/v1.0/api/system/master-key", ResourceTypeEnum.Admin, OperationTypeEnum.Read, StatusAsync, "Master key version and rotation progress (system administrators)", "System", null, typeof(MasterKeyRotationStatus));
            routes.Scoped(HttpMethod.POST, "/v1.0/api/system/master-key/rotate", ResourceTypeEnum.Admin, OperationTypeEnum.Admin, RotateAsync, "Re-encrypt every secret value and credential secret under the current master key version, in batches; resumable (system administrators)", "System", null, typeof(MasterKeyRotationStatus), statuses: new[] { 202 });
        }

        #endregion

        #region Private-Methods

        private async Task StatusAsync(HttpContextBase http)
        {
            await RouteHelpers.JsonAsync(http, 200, await _Rotation.StatusAsync(http.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task RotateAsync(HttpContextBase http)
        {
            bool started = _Rotation.TryStart(_Context.Stopping);
            if (started)
            {
                await _Context.Audit.WriteAsync(RouteHelpers.Context(http), AuditEventTypeEnum.MasterKeyRotated, "MasterKey", null, "Master key rotation to version " + _Context.Cipher.KeyVersion + " started.", null, http.Token).ConfigureAwait(false);
            }
            MasterKeyRotationStatus status = await _Rotation.StatusAsync(http.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(http, started ? 202 : 409, status).ConfigureAwait(false);
        }

        #endregion
    }
}
