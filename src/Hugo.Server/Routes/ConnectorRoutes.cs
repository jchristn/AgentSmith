namespace Hugo.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Connectors;
    using Hugo.Core.Enums;
    using Hugo.Core.Responses;
    using Hugo.Core.Services;
    using Hugo.Server.Services;
    using WatsonWebserver.Core;

    /// <summary>
    /// Connector catalog routes (P3.3, plan section 8.2): every registered connector with its settings schema, secret
    /// slots, and tools, so the dashboard and SDKs need no connector-specific code.
    /// </summary>
    public class ConnectorRoutes
    {
        #region Private-Members

        private static readonly string _Base = "/v1.0/api/connectors";
        private readonly ConnectorRegistry _Registry;
        private readonly PluginLoader? _Plugins;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="registry">Registry.</param>
        /// <param name="plugins">Plugin loader, or null.</param>
        /// <exception cref="ArgumentNullException">Thrown when registry is null.</exception>
        public ConnectorRoutes(ConnectorRegistry registry, PluginLoader? plugins)
        {
            _Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _Plugins = plugins;
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
            routes.Scoped(HttpMethod.GET, _Base, ResourceTypeEnum.Connector, OperationTypeEnum.Read, ListAsync, "List connectors with settings schema, secret slots, and tools", "Connectors", null, typeof(List<ConnectorView>));
            routes.Scoped(HttpMethod.GET, _Base + "/{type}", ResourceTypeEnum.Connector, OperationTypeEnum.Read, ReadAsync, "Read one connector descriptor with settings schema, secret slots, and tools", "Connectors", null, typeof(ConnectorView), "type");
        }

        /// <summary>
        /// Describe a connector.
        /// </summary>
        /// <param name="connector">Connector.</param>
        /// <param name="plugin">Whether it came from a plugin.</param>
        /// <returns>View.</returns>
        public static ConnectorView View(IConnector connector, bool plugin)
        {
            ConnectorDescriptor d = connector.Descriptor;
            ConnectorView view = new ConnectorView();
            view.Type = d.Type;
            view.Family = d.Family;
            view.DisplayName = d.DisplayName;
            view.Description = d.Description;
            view.Category = d.Category;
            view.IconKey = d.IconKey;
            view.Version = d.Version;
            view.DocumentationUrl = d.DocumentationUrl;
            view.DefaultInstructionsKey = d.DefaultInstructionsKey;
            view.Plugin = plugin;
            view.Settings = d.SettingsSchema.Settings.ToList();
            view.SecretSlots = d.SecretSlots.ToList();
            foreach (IConnectorTool tool in connector.Tools)
            {
                ConnectorToolView t = new ConnectorToolView();
                t.Name = tool.Descriptor.Name;
                t.Title = String.IsNullOrEmpty(tool.Descriptor.Title) ? tool.Descriptor.Name : tool.Descriptor.Title;
                t.Description = tool.Descriptor.Description;
                t.InputSchemaJson = tool.Descriptor.InputSchemaJson;
                t.BaseEffect = tool.Descriptor.BaseEffect;
                t.ClassifiedPerCall = tool.Descriptor.ClassifiedPerCall;
                t.UpdatesKnowledge = tool.Descriptor.UpdatesKnowledge;
                t.RequiresModel = tool.Descriptor.RequiresModel;
                t.OutputDescription = tool.Descriptor.OutputDescription;
                view.Tools.Add(t);
            }
            return view;
        }

        #endregion

        #region Private-Methods

        private async Task ListAsync(HttpContextBase http)
        {
            List<string> pluginTypes = _Plugins?.LoadedTypes() ?? new List<string>();
            List<ConnectorView> views = _Registry.All().Select(c => View(c, pluginTypes.Contains(c.Descriptor.Type))).ToList();
            await RouteHelpers.JsonAsync(http, 200, views).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase http)
        {
            string type = RouteHelpers.Param(http, "type") ?? String.Empty;
            IConnector connector = _Registry.Get(type) ?? throw new NotFoundException("Connector type '" + type + "' is not registered.");
            List<string> pluginTypes = _Plugins?.LoadedTypes() ?? new List<string>();
            await RouteHelpers.JsonAsync(http, 200, View(connector, pluginTypes.Contains(type))).ConfigureAwait(false);
        }

        #endregion
    }
}
