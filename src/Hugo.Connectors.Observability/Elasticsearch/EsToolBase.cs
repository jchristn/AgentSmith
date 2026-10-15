namespace Hugo.Connectors.Observability
{
    using System;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Base for the <c>es_*</c> tools: resolves settings, the transport, and argument helpers. Thread-safe.
    /// </summary>
    public abstract class EsToolBase : ObservabilityToolBase
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="descriptor">Tool descriptor.</param>
        /// <exception cref="ArgumentNullException">Thrown when descriptor is null.</exception>
        protected EsToolBase(ToolDescriptor descriptor) : base(descriptor)
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Provider name for a flavor.
        /// </summary>
        /// <param name="flavor">Flavor.</param>
        /// <returns>Name.</returns>
        public static string ProviderName(ElasticsearchFlavorEnum flavor)
        {
            return flavor == ElasticsearchFlavorEnum.OpenSearch ? "OpenSearch" : "Elasticsearch";
        }

        /// <summary>
        /// Build the transport for a resource.
        /// </summary>
        /// <param name="resource">Resource.</param>
        /// <param name="settings">Settings.</param>
        /// <returns>Transport.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static ObservabilityTransport Transport(ConnectorResourceContext resource, ElasticsearchSettings settings)
        {
            ArgumentNullException.ThrowIfNull(resource);
            ArgumentNullException.ThrowIfNull(settings);
            return new ObservabilityTransport(settings.Endpoint, ProviderName(settings.Flavor), resource.Secrets, null, resource.Logger);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Parse the resource settings.
        /// </summary>
        /// <param name="context">Invocation context.</param>
        /// <returns>Settings.</returns>
        protected static ElasticsearchSettings ReadSettings(ConnectorInvocationContext context)
        {
            return new ElasticsearchSettings(context.Resource.Settings);
        }

        /// <summary>
        /// Read an optional JSON argument and check its kind by its first character.
        /// </summary>
        /// <param name="arguments">Arguments.</param>
        /// <param name="name">Argument name.</param>
        /// <param name="allowedStarts">Allowed first characters (for example <c>{</c> for objects).</param>
        /// <param name="expected">Description of the expected kind, for errors.</param>
        /// <returns>Raw JSON, or null when absent.</returns>
        /// <exception cref="ConnectorException">Thrown with <c>invalid_argument</c> when the kind is wrong.</exception>
        protected static string? RawJson(ToolArguments arguments, string name, string allowedStarts, string expected)
        {
            string? raw = arguments.GetRawJson(name);
            if (raw == null) return null;
            string trimmed = raw.TrimStart();
            if (trimmed.Length == 0 || allowedStarts.IndexOf(trimmed[0]) < 0)
                throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Argument '" + name + "' must be " + expected + ".");
            return trimmed;
        }

        #endregion
    }
}
