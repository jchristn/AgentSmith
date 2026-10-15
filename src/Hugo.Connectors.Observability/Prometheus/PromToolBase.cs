namespace Hugo.Connectors.Observability
{
    using System;
    using System.Collections.Generic;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Base for the <c>prom_*</c> tools: resolves the resource's settings and API client. Thread-safe.
    /// </summary>
    public abstract class PromToolBase : ObservabilityToolBase
    {
        #region Public-Members

        /// <summary>
        /// Provider name used in messages.
        /// </summary>
        public static readonly string ProviderName = "Prometheus";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="descriptor">Tool descriptor.</param>
        /// <exception cref="ArgumentNullException">Thrown when descriptor is null.</exception>
        protected PromToolBase(ToolDescriptor descriptor) : base(descriptor)
        {
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Parse the resource settings.
        /// </summary>
        /// <param name="context">Invocation context.</param>
        /// <returns>Settings.</returns>
        protected static PrometheusSettings ReadSettings(ConnectorInvocationContext context)
        {
            return new PrometheusSettings(context.Resource.Settings);
        }

        /// <summary>
        /// Build the API client for one call.
        /// </summary>
        /// <param name="context">Invocation context.</param>
        /// <param name="settings">Settings.</param>
        /// <returns>Client.</returns>
        protected static PromApiClient Client(ConnectorInvocationContext context, PrometheusSettings settings)
        {
            return new PromApiClient(new ObservabilityTransport(settings.Endpoint, ProviderName, context.Resource.Secrets, null, context.Resource.Logger));
        }

        /// <summary>
        /// Series cap for a call: the requested limit, capped by maxSeries and the call's row limit.
        /// </summary>
        /// <param name="arguments">Arguments.</param>
        /// <param name="settings">Settings.</param>
        /// <param name="limits">Output limits.</param>
        /// <returns>Cap.</returns>
        protected static int SeriesCap(ToolArguments arguments, PrometheusSettings settings, OutputLimits limits)
        {
            int cap = Math.Max(1, Math.Min(settings.MaxSeries, limits.MaxRows));
            return arguments.GetInt("limit", cap, 1, cap);
        }

        /// <summary>
        /// Notes from upstream warnings.
        /// </summary>
        /// <param name="warnings">Warnings, or null.</param>
        /// <returns>Notes.</returns>
        protected static List<string> Notes(List<string>? warnings)
        {
            List<string> notes = new List<string>();
            if (warnings != null) foreach (string w in warnings) notes.Add("Prometheus warning: " + w);
            return notes;
        }

        #endregion
    }
}
