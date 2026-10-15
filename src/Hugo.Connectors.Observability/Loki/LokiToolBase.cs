namespace Hugo.Connectors.Observability
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Base for the <c>loki_*</c> tools: resolves settings, the tenant header, line caps, and result rendering.
    /// Thread-safe.
    /// </summary>
    public abstract class LokiToolBase : ObservabilityToolBase
    {
        #region Public-Members

        /// <summary>
        /// Provider name used in messages.
        /// </summary>
        public static readonly string ProviderName = "Loki";

        /// <summary>
        /// Longest log line kept; longer lines end with <c>...</c>. Minimum 64, maximum 65536, default 2000.
        /// </summary>
        public int MaxLineChars
        {
            get
            {
                return _MaxLineChars;
            }
            set
            {
                _MaxLineChars = Math.Clamp(value, 64, 65536);
            }
        }

        /// <summary>
        /// Lines returned when the caller omits <c>limit</c>. Minimum 1, maximum 5000, default 100. Always capped by the
        /// resource's maxLines and the call's line limit.
        /// </summary>
        public int DefaultLimit
        {
            get
            {
                return _DefaultLimit;
            }
            set
            {
                _DefaultLimit = Math.Clamp(value, 1, 5000);
            }
        }

        #endregion

        #region Private-Members

        private int _MaxLineChars = 2000;
        private int _DefaultLimit = 100;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="descriptor">Tool descriptor.</param>
        /// <exception cref="ArgumentNullException">Thrown when descriptor is null.</exception>
        protected LokiToolBase(ToolDescriptor descriptor) : base(descriptor)
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the API client for a resource, including the tenant header.
        /// </summary>
        /// <param name="resource">Resource.</param>
        /// <param name="settings">Settings.</param>
        /// <returns>Client.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static PromApiClient Client(ConnectorResourceContext resource, LokiSettings settings)
        {
            ArgumentNullException.ThrowIfNull(resource);
            ArgumentNullException.ThrowIfNull(settings);
            Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (settings.TenantId != null) headers[LokiSettings.TenantHeaderName] = settings.TenantId;
            return new PromApiClient(new ObservabilityTransport(settings.Endpoint, ProviderName, resource.Secrets, headers, resource.Logger));
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Parse the resource settings.
        /// </summary>
        /// <param name="context">Invocation context.</param>
        /// <returns>Settings.</returns>
        protected static LokiSettings ReadSettings(ConnectorInvocationContext context)
        {
            return new LokiSettings(context.Resource.Settings);
        }

        /// <summary>
        /// Line cap for a call: the requested limit or the default, capped by maxLines and the call's line limit.
        /// </summary>
        /// <param name="arguments">Arguments.</param>
        /// <param name="settings">Settings.</param>
        /// <param name="limits">Output limits.</param>
        /// <returns>Cap.</returns>
        protected int LineCap(ToolArguments arguments, LokiSettings settings, OutputLimits limits)
        {
            int cap = Math.Max(1, Math.Min(settings.MaxLines, limits.MaxLines));
            return arguments.GetInt("limit", Math.Min(_DefaultLimit, cap), 1, cap);
        }

        /// <summary>
        /// Read the direction argument.
        /// </summary>
        /// <param name="arguments">Arguments.</param>
        /// <returns>True for backward (newest first).</returns>
        /// <exception cref="ConnectorException">Thrown with <c>invalid_argument</c> for another value.</exception>
        protected static bool Backward(ToolArguments arguments)
        {
            string direction = (arguments.GetString("direction", "backward") ?? "backward").Trim().ToLowerInvariant();
            if (direction == "backward") return true;
            if (direction == "forward") return false;
            throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Argument 'direction' must be backward (newest first) or forward (oldest first).");
        }

        /// <summary>
        /// Run a query endpoint and render streams or metric results.
        /// </summary>
        /// <param name="context">Invocation context.</param>
        /// <param name="settings">Settings.</param>
        /// <param name="path">API path.</param>
        /// <param name="query">Query parameters.</param>
        /// <param name="logql">The LogQL query.</param>
        /// <param name="backward">Direction.</param>
        /// <param name="cap">Line or series cap.</param>
        /// <param name="window">Window, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        protected async Task<ToolResult> RunQueryAsync(ConnectorInvocationContext context, LokiSettings settings, string path, List<KeyValuePair<string, string?>> query, string logql, bool backward, int cap, ObservabilityTimeWindow? window, CancellationToken token)
        {
            PromResponse<PromQueryData> response = await Client(context.Resource, settings).GetAsync<PromQueryData>(path, query, token).ConfigureAwait(false);
            PromQueryData data = response.Data!;
            ObservabilityRendering r;
            if (String.Equals(data.ResultType, "streams", StringComparison.Ordinal))
            {
                List<LokiStream> streams = data.Result.ValueKind == JsonValueKind.Array
                    ? ObservabilityJson.DeserializeElement<List<LokiStream>>(data.Result, ProviderName) ?? new List<LokiStream>()
                    : new List<LokiStream>();
                r = LokiStreamRenderer.Render(streams, logql, backward, cap, _MaxLineChars, window);
            }
            else
            {
                List<string> notes = new List<string>();
                if (response.Warnings != null) foreach (string w in response.Warnings) notes.Add("Loki warning: " + w);
                r = PromResultRenderer.Render(data, ProviderName, logql, cap, window, 0, notes);
            }
            ToolResult result = Finish(r.Text, r.StructuredJson, context.Limits, r.Truncated);
            result.Metrics["returned"] = r.Returned;
            result.Metrics["omitted"] = r.Omitted;
            return result;
        }

        #endregion
    }
}
