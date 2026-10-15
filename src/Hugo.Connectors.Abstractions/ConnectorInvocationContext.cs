namespace Hugo.Connectors.Abstractions
{
    using System;

    /// <summary>
    /// Everything a tool needs for one call beyond its arguments. Not thread-safe for mutation.
    /// </summary>
    public class ConnectorInvocationContext
    {
        #region Public-Members

        /// <summary>
        /// The resolved resource. Never null.
        /// </summary>
        public ConnectorResourceContext Resource
        {
            get
            {
                return _Resource;
            }
            set
            {
                _Resource = value ?? throw new ArgumentNullException(nameof(Resource));
            }
        }

        /// <summary>
        /// Binding alias the caller used to address the resource.
        /// </summary>
        public string BindingAlias { get; set; } = String.Empty;

        /// <summary>
        /// Access mode of the binding. Connectors may use it for defense in depth (for example a read-only database
        /// transaction under <see cref="AccessModeEnum.ReadOnly"/>). Default ReadOnly.
        /// </summary>
        public AccessModeEnum AccessMode { get; set; } = AccessModeEnum.ReadOnly;

        /// <summary>
        /// Output limits. Never null.
        /// </summary>
        public OutputLimits Limits
        {
            get
            {
                return _Limits;
            }
            set
            {
                _Limits = value ?? new OutputLimits();
            }
        }

        /// <summary>
        /// Timeout for the call. Minimum 1 second, maximum 1 hour, default 120 seconds. The server also cancels the
        /// token passed to the tool when it elapses.
        /// </summary>
        public TimeSpan Timeout
        {
            get
            {
                return _Timeout;
            }
            set
            {
                if (value < TimeSpan.FromSeconds(1)) value = TimeSpan.FromSeconds(1);
                if (value > TimeSpan.FromHours(1)) value = TimeSpan.FromHours(1);
                _Timeout = value;
            }
        }

        /// <summary>
        /// Language model bound to the calling profile's inference endpoint, or null when the profile has none. Tools that
        /// need a model must return a clear <c>not_supported</c> error when this is null.
        /// </summary>
        public IConnectorModel? Model { get; set; } = null;

        /// <summary>
        /// Whether the binding allows this call to change the resource's durable knowledge (tools with
        /// <see cref="ToolDescriptor.UpdatesKnowledge"/>). Default true.
        /// </summary>
        public bool AllowKnowledgeUpdates { get; set; } = true;

        /// <summary>
        /// Non-secret description of the caller (for example <c>user anderson@hugo.local via MCP</c>), for logs.
        /// </summary>
        public string PrincipalSummary { get; set; } = String.Empty;

        /// <summary>
        /// Correlation identifier (request or invocation ID) for logs.
        /// </summary>
        public string CorrelationId { get; set; } = String.Empty;

        #endregion

        #region Private-Members

        private ConnectorResourceContext _Resource = new ConnectorResourceContext();
        private OutputLimits _Limits = new OutputLimits();
        private TimeSpan _Timeout = TimeSpan.FromSeconds(120);

        #endregion
    }
}
