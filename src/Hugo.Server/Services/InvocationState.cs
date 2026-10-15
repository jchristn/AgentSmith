namespace Hugo.Server.Services
{
    using System;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Models;
    using Hugo.Core.Responses;
    using Hugo.Core.Security;

    /// <summary>
    /// Working state of one call as it moves through the invocation pipeline. Not thread-safe.
    /// </summary>
    public class InvocationState
    {
        /// <summary>
        /// Caller.
        /// </summary>
        public RequestContext Principal { get; set; } = new RequestContext();

        /// <summary>
        /// Tenant identifier.
        /// </summary>
        public string TenantId { get; set; } = String.Empty;

        /// <summary>
        /// Profile, or null for an administrator resource invoke.
        /// </summary>
        public AgentProfile? Profile { get; set; } = null;

        /// <summary>
        /// Binding, or null for an administrator resource invoke.
        /// </summary>
        public Binding? Binding { get; set; } = null;

        /// <summary>
        /// Resource.
        /// </summary>
        public Resource? Resource { get; set; } = null;

        /// <summary>
        /// Connector.
        /// </summary>
        public IConnector? Connector { get; set; } = null;

        /// <summary>
        /// Tool.
        /// </summary>
        public IConnectorTool? Tool { get; set; } = null;

        /// <summary>
        /// Arguments JSON without the resource argument.
        /// </summary>
        public string ArgumentsJson { get; set; } = "{}";

        /// <summary>
        /// Effective access mode.
        /// </summary>
        public AccessModeEnum AccessMode { get; set; } = AccessModeEnum.ReadOnly;

        /// <summary>
        /// The tool invocation record.
        /// </summary>
        public ToolInvocation Record { get; set; } = new ToolInvocation();

        /// <summary>
        /// The outcome being built.
        /// </summary>
        public ToolInvocationOutcome Outcome { get; set; } = new ToolInvocationOutcome();

        /// <summary>
        /// Whether the record row already exists (approval flows).
        /// </summary>
        public bool RecordPersisted { get; set; } = false;
    }
}
