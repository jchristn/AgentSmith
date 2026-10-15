namespace Hugo.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using Hugo.Core.Database;
    using Hugo.Core.Enums;
    using Hugo.Core.Requests;
    using Hugo.Core.Responses;
    using Hugo.Server.Services;
    using WatsonWebserver.Core;

    /// <summary>
    /// Settings routes: read with metadata, update, restart, and database connectivity test. System administrators only.
    /// </summary>
    public class SettingsRoutes
    {
        #region Private-Members

        private readonly ServerContext _Context;
        private readonly SettingsService _Settings;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="context">Server context.</param>
        /// <param name="settings">Settings service.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public SettingsRoutes(ServerContext context, SettingsService settings)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="routes">Registrar.</param>
        public void Register(RouteRegistrar routes)
        {
            routes.Scoped(HttpMethod.GET, "/v1.0/api/settings", ResourceTypeEnum.Settings, OperationTypeEnum.Read, ReadAsync, "Read settings with source, apply mode, and editability metadata", "Settings", null, typeof(SettingsView));
            routes.Scoped(HttpMethod.PUT, "/v1.0/api/settings", ResourceTypeEnum.Settings, OperationTypeEnum.Update, UpdateAsync, "Update settings", "Settings", typeof(SettingsUpdateRequest), typeof(SettingsSaveResponse));
            routes.Scoped(HttpMethod.POST, "/v1.0/api/settings/restart", ResourceTypeEnum.Settings, OperationTypeEnum.Admin, RestartAsync, "Restart this server process (the container runtime restarts it)", "Settings", null, typeof(RestartResponse));
            routes.Scoped(HttpMethod.POST, "/v1.0/api/system/restart", ResourceTypeEnum.Settings, OperationTypeEnum.Admin, RestartAsync, "Restart this server process (same as /v1.0/api/settings/restart; exit code 75 for the container restart policy)", "Settings", null, typeof(RestartResponse));
            routes.Scoped(HttpMethod.POST, "/v1.0/api/settings/database/test", ResourceTypeEnum.Settings, OperationTypeEnum.Admin, TestDatabaseAsync, "Test database connectivity", "Settings", null, typeof(DatabaseTestResponse));
        }

        #endregion

        #region Private-Methods

        private async Task ReadAsync(HttpContextBase context)
        {
            await RouteHelpers.JsonAsync(context, 200, _Settings.View()).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase context)
        {
            SettingsSaveResponse response = await _Settings.UpdateAsync(RouteHelpers.RequiredBody<SettingsUpdateRequest>(context), RouteHelpers.Context(context).PrincipalName, context.Token).ConfigureAwait(false);
            await _Context.Audit.WriteAsync(RouteHelpers.Context(context), AuditEventTypeEnum.SettingsChanged, "Settings", null, "Changed: " + String.Join(", ", response.AppliedLive) + (response.PendingRestart.Count > 0 ? "; pending restart: " + String.Join(", ", response.PendingRestart) : String.Empty), null, context.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 200, response).ConfigureAwait(false);
        }

        private async Task RestartAsync(HttpContextBase context)
        {
            await _Context.Audit.WriteAsync(RouteHelpers.Context(context), AuditEventTypeEnum.RestartRequested, "Settings", null, "Restart requested.", null, context.Token).ConfigureAwait(false);
            _Context.Logging.Warn("[Settings] restart requested by " + RouteHelpers.Context(context).PrincipalName);
            await RouteHelpers.JsonAsync(context, 202, new RestartResponse { Restarting = true, Node = _Context.NodeId }).ConfigureAwait(false);
            RestartCoordinator.RequestRestart(750);
        }

        private async Task TestDatabaseAsync(HttpContextBase context)
        {
            DatabaseTestResponse response = new DatabaseTestResponse();
            response.Provider = _Context.Settings.Database.Type.ToString();
            try
            {
                response.Ok = await _Context.Database.PingAsync(context.Token).ConfigureAwait(false);
                response.Message = response.Ok ? "Database answered." : "Database did not answer.";
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                response.Ok = false;
                response.Message = e.Message;
            }
            await RouteHelpers.JsonAsync(context, 200, response).ConfigureAwait(false);
        }

        #endregion
    }
}
