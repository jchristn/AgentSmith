namespace Hugo.Connectors.Agents.Mcp
{
    /// <summary>
    /// Setting keys, secret slot names, and secret field names of the <c>mcp</c> connector. These are part of the resource
    /// contract and are not configurable. Thread-safe.
    /// </summary>
    public static class McpSettingKeys
    {
        /// <summary>Absolute http or https URL of the Streamable HTTP endpoint, including its path (for example https://mcp.example.com/mcp).</summary>
        public const string Url = "url";

        /// <summary>Transport: <c>StreamableHttp</c> or <c>Stdio</c>.</summary>
        public const string Transport = "transport";

        /// <summary>Executable started for the Stdio transport.</summary>
        public const string Command = "command";

        /// <summary>Arguments of the Stdio executable, one per line.</summary>
        public const string Arguments = "arguments";

        /// <summary>Resource-level opt-in for the Stdio transport. The server must also allow Stdio.</summary>
        public const string AllowStdio = "allowStdio";

        /// <summary>Remote tool names or <c>*</c> patterns to expose. Empty exposes every tool.</summary>
        public const string IncludeTools = "includeTools";

        /// <summary>Remote tool names or <c>*</c> patterns to hide. Wins over includeTools.</summary>
        public const string ExcludeTools = "excludeTools";

        /// <summary>Minutes a discovered tool list is reused before it is fetched again.</summary>
        public const string RefreshIntervalMinutes = "refreshIntervalMinutes";

        /// <summary>Longest time one remote call may take, in seconds.</summary>
        public const string TimeoutSeconds = "timeoutSeconds";

        /// <summary>Secret slot whose token is sent in the Authorization header.</summary>
        public const string AuthorizationHeaderSlot = "authorizationHeader";

        /// <summary>Secret slot whose fields are sent as extra headers (field name is the header name).</summary>
        public const string ExtraHeadersSlot = "extraHeaders";

        /// <summary>Field of the extraHeaders secret listing the header names to send (comma or newline separated); each name is also a field holding that header's value.</summary>
        public const string ExtraHeaderNamesField = "names";

        /// <summary>Secret field holding a token (ApiToken secrets).</summary>
        public const string TokenField = "token";
    }
}
