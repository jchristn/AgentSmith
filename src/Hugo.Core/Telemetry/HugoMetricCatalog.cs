namespace Hugo.Core.Telemetry
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Radiant;

    /// <summary>
    /// Every Hugo metric (plan section 21): name, kind, unit, labels, emitting process, coverage category, and
    /// Grafana dashboard. <see cref="HugoMetrics"/> creates its instruments from these descriptors, the telemetry
    /// pipeline applies their histogram buckets, docs/TELEMETRY.md lists them, and the coverage tests check that each one
    /// is documented and charted. Label keys are the constants on <see cref="HugoMetrics"/>. Thread-safe (immutable
    /// after static initialization; do not modify the descriptors).
    /// </summary>
    public static class HugoMetricCatalog
    {
        #region Public-Members

        /// <summary>REST requests handled.</summary>
        public static readonly MetricDescriptor HttpRequests = new MetricDescriptor("hugo.http.requests", MetricKindEnum.Counter, "{request}", "method, route, status_code, status_class, tenant", "REST requests handled, by route template (never the raw path), method, status, and tenant. Requests the client abandoned (for example a closed browser tab) have status_code 499 and status_class aborted, never 5xx", "server", "HTTP", "hugo-http");

        /// <summary>REST request duration.</summary>
        public static readonly MetricDescriptor HttpRequestDuration = new MetricDescriptor("hugo.http.request.duration", MetricKindEnum.Histogram, "s", "method, route, status_class, tenant", "Time from the request arriving to the response being sent", "server", "HTTP", "hugo-http", TelemetryBuckets.Request);

        /// <summary>REST request body size.</summary>
        public static readonly MetricDescriptor HttpRequestSize = new MetricDescriptor("hugo.http.request.size", MetricKindEnum.Histogram, "By", "method, route", "Request body size", "server", "HTTP", "hugo-http", TelemetryBuckets.Bytes);

        /// <summary>REST response body size.</summary>
        public static readonly MetricDescriptor HttpResponseSize = new MetricDescriptor("hugo.http.response.size", MetricKindEnum.Histogram, "By", "method, route", "Response body size (0 for streamed responses)", "server", "HTTP", "hugo-http", TelemetryBuckets.Bytes);

        /// <summary>REST requests in flight.</summary>
        public static readonly MetricDescriptor HttpInFlight = new MetricDescriptor("hugo.http.requests.in_flight", MetricKindEnum.ObservableGauge, "{request}", "", "REST requests being handled right now, including long polls and streams", "server", "HTTP", "hugo-http");

        /// <summary>Authentication failures.</summary>
        public static readonly MetricDescriptor AuthFailures = new MetricDescriptor("hugo.auth.failures", MetricKindEnum.Counter, "{request}", "scheme, reason", "Requests rejected by authentication, by scheme (BearerToken, AccessKeySignature, ...) and reason (Invalid, NotFound, Inactive, Missing)", "server", "Security", "hugo-security");

        /// <summary>Rate-limit rejections.</summary>
        public static readonly MetricDescriptor AuthRateLimited = new MetricDescriptor("hugo.auth.rate_limited", MetricKindEnum.Counter, "{request}", "scope", "Requests refused by the authentication rate limiter, by limiter scope", "server", "Security", "hugo-security");

        /// <summary>Authorization denials.</summary>
        public static readonly MetricDescriptor AuthzDenials = new MetricDescriptor("hugo.authz.denials", MetricKindEnum.Counter, "{request}", "resource, operation, result", "Authorization denials by resource type, operation, and result (Forbidden, NotFound)", "server", "Security", "hugo-security");

        /// <summary>Authentication cache lookups.</summary>
        public static readonly MetricDescriptor AuthCacheLookups = new MetricDescriptor("hugo.auth.cache_lookups", MetricKindEnum.Counter, "{lookup}", "entity, result", "Authentication lookups by record (session, tenant, user, credential) and result: hit (answered from the in-process cache, Auth.CacheSeconds) or miss (read from the database)", "server", "Security", "hugo-security");

        /// <summary>Tokens issued.</summary>
        public static readonly MetricDescriptor AuthTokensIssued = new MetricDescriptor("hugo.auth.tokens_issued", MetricKindEnum.Counter, "{token}", "scheme, principal", "Session tokens issued (sign-ins and token exchanges) by scheme and principal type (User, Credential)", "server", "Security", "hugo-security");

        /// <summary>Audit records.</summary>
        public static readonly MetricDescriptor AuditEvents = new MetricDescriptor("hugo.audit.events", MetricKindEnum.Counter, "{event}", "type", "Audit records written, by event type", "server", "Security", "hugo-security");

        /// <summary>SSE streams active.</summary>
        public static readonly MetricDescriptor SseStreamsActive = new MetricDescriptor("hugo.sse.streams.active", MetricKindEnum.ObservableGauge, "{stream}", "stream", "Server-sent event streams open on this server (events, chat, A2A)", "server", "Protocols", "hugo-protocols");

        /// <summary>MCP requests.</summary>
        public static readonly MetricDescriptor McpRequests = new MetricDescriptor("hugo.mcp.requests", MetricKindEnum.Counter, "{request}", "method, outcome", "MCP requests on /mcp by JSON-RPC method (initialize, tools/list, tools/call, ...; other for anything else) and outcome (ok, tool_error, error, stream, unauthenticated, forbidden, not_found, bad_request)", "server", "Protocols", "hugo-protocols");

        /// <summary>SSE events sent.</summary>
        public static readonly MetricDescriptor SseEvents = new MetricDescriptor("hugo.sse.events", MetricKindEnum.Counter, "{event}", "stream", "Server-sent events written", "server", "Protocols", "hugo-protocols");

        /// <summary>Background iteration duration.</summary>
        public static readonly MetricDescriptor BackgroundDuration = new MetricDescriptor("hugo.background.duration", MetricKindEnum.Histogram, "s", "service, operation, result", "Duration of one iteration of a background service (retention, settings refresh, key rotation)", "server, worker", "Control plane", "hugo-control-plane", TelemetryBuckets.Request);

        /// <summary>Background iterations.</summary>
        public static readonly MetricDescriptor BackgroundIterations = new MetricDescriptor("hugo.background.iterations", MetricKindEnum.Counter, "{iteration}", "service, operation, result", "Background service iterations by result (ok, idle, error)", "server, worker", "Control plane", "hugo-control-plane");

        /// <summary>Build information.</summary>
        public static readonly MetricDescriptor BuildInfo = new MetricDescriptor("hugo.build.info", MetricKindEnum.ObservableGauge, "{process}", "version, process", "Always 1; carries the Hugo version and process", "server, worker", "Control plane", "hugo-overview");

        /// <summary>Database statement duration.</summary>
        public static readonly MetricDescriptor DbDuration = new MetricDescriptor("hugo.db.operation.duration", MetricKindEnum.Histogram, "s", "provider, operation, table, result", "Database statement and transaction duration by provider, operation (select, insert, update, delete, transaction, ...), and table", "server", "Database", "hugo-database", TelemetryBuckets.Fast);

        /// <summary>Database errors.</summary>
        public static readonly MetricDescriptor DbErrors = new MetricDescriptor("hugo.db.errors", MetricKindEnum.Counter, "{error}", "provider, operation, table, error", "Database statements that threw, by exception type", "server", "Database", "hugo-database");

        /// <summary>Connections in use.</summary>
        public static readonly MetricDescriptor DbConnections = new MetricDescriptor("hugo.db.connections.in_use", MetricKindEnum.UpDownCounter, "{connection}", "provider", "Database connections open in this process (Postgresql pooling detail is in the npgsql_* metrics)", "server", "Database", "hugo-database");

        /// <summary>Write lock wait.</summary>
        public static readonly MetricDescriptor DbWriteLockWait = new MetricDescriptor("hugo.db.write_lock.wait", MetricKindEnum.Histogram, "s", "provider", "Time waiting for the Sqlite write lock", "server", "Database", "hugo-database", TelemetryBuckets.Fast);

        /// <summary>Transaction retries.</summary>
        public static readonly MetricDescriptor DbRetries = new MetricDescriptor("hugo.db.transaction.retries", MetricKindEnum.Counter, "{retry}", "provider", "Transactions retried after a transient conflict (serialization failure, busy database)", "server", "Database", "hugo-database");

        /// <summary>Retention deletions.</summary>
        public static readonly MetricDescriptor RetentionDeleted = new MetricDescriptor("hugo.retention.deleted", MetricKindEnum.Counter, "{row}", "kind", "Rows removed by retention (request history, audit, sessions, nonces)", "server", "Database", "hugo-database");

        /// <summary>Request history captures.</summary>
        public static readonly MetricDescriptor RequestHistoryCaptured = new MetricDescriptor("hugo.request_history.captured", MetricKindEnum.Counter, "{request}", "result", "Request history entries written or dropped", "server", "Database", "hugo-database");

        /// <summary>Outbound requests.</summary>
        public static readonly MetricDescriptor OutboundRequests = new MetricDescriptor("hugo.outbound.requests", MetricKindEnum.Counter, "{request}", "service, operation, result", "Outbound calls (observability probes, metrics relay, and later connectors) by result (ok, error, status class)", "server, worker", "Integrations", "hugo-integrations");

        /// <summary>Outbound duration.</summary>
        public static readonly MetricDescriptor OutboundDuration = new MetricDescriptor("hugo.outbound.duration", MetricKindEnum.Histogram, "s", "service, operation, result", "Outbound call duration", "server, worker", "Integrations", "hugo-integrations", TelemetryBuckets.Request);

        /// <summary>Observability service reachability.</summary>
        public static readonly MetricDescriptor ObservabilityReachable = new MetricDescriptor("hugo.observability.reachable", MetricKindEnum.ObservableGauge, "{flag}", "service", "1 when the server's last probe reached the observability service (Grafana, Prometheus, Loki, Tempo), 0 when not; absent when not configured", "server", "Integrations", "hugo-integrations");

        /// <summary>Folded label values.</summary>
        public static readonly MetricDescriptor LabelsFolded = new MetricDescriptor("hugo.telemetry.labels_folded", MetricKindEnum.Counter, "{value}", "label", "Measurements whose label value was replaced by other because the label already had Telemetry.MaxLabelValues distinct values", "server, worker", "Telemetry", "hugo-overview");

        /// <summary>Telemetry diagnostics.</summary>
        public static readonly MetricDescriptor TelemetryDiagnostics = new MetricDescriptor("hugo.telemetry.diagnostics", MetricKindEnum.Counter, "{message}", "severity", "Diagnostics reported by the telemetry pipeline (export failures and the like)", "server, worker", "Telemetry", "hugo-logs");

        /// <summary>Authentication results.</summary>
        public static readonly MetricDescriptor AuthRequests = new MetricDescriptor("hugo.auth.requests", MetricKindEnum.Counter, "{request}", "scheme, result", "Authentication attempts by scheme and result (Success, Invalid, NotFound, Inactive, Missing)", "server", "Security", "hugo-security");

        /// <summary>Authorization decisions.</summary>
        public static readonly MetricDescriptor AuthzRequests = new MetricDescriptor("hugo.authz.requests", MetricKindEnum.Counter, "{request}", "result", "Authorization decisions by result (Permitted, Bypassed, DeniedExplicit, DeniedImplicit, NotFound)", "server", "Security", "hugo-security");

        /// <summary>Secret vault operations.</summary>
        public static readonly MetricDescriptor SecretOperations = new MetricDescriptor("hugo.secret.operations", MetricKindEnum.Counter, "{operation}", "operation, outcome", "Secret vault operations by operation (decrypt, reveal, rotate) and outcome (ok, error)", "server", "Security", "hugo-security");

        /// <summary>Secret scrubber redactions.</summary>
        public static readonly MetricDescriptor SecretRedactions = new MetricDescriptor("hugo.secret.redactions", MetricKindEnum.Counter, "{redaction}", "kind", "Values replaced by the secret scrubber, by kind (known, private_key, aws_access_key, bearer, connection_string, password_pair, generic)", "server", "Security", "hugo-security");

        /// <summary>Background job runs.</summary>
        public static readonly MetricDescriptor BackgroundJobs = new MetricDescriptor("hugo.background.jobs", MetricKindEnum.Counter, "{run}", "job, outcome", "Background job runs (retention, master key rotation, and so on) by outcome (ok, error)", "server", "Background", "hugo-overview");

        /// <summary>Background job duration.</summary>
        public static readonly MetricDescriptor BackgroundJobDuration = new MetricDescriptor("hugo.background.job.duration", MetricKindEnum.Histogram, "s", "job", "Duration of one background job run", "server", "Background", "hugo-overview", TelemetryBuckets.Request);

        /// <summary>Tool invocations.</summary>
        public static readonly MetricDescriptor ToolInvocations = new MetricDescriptor("hugo.tool.invocations", MetricKindEnum.Counter, "{call}", "connector, tool, effect, decision, status, surface", "Tool calls through the invocation pipeline by connector, tool, classified effect, decision (Allowed, Denied, ApprovalRequired, Approved, Rejected, Expired, Cancelled), execution status, and surface", "server", "Tools and Connectors", "hugo-tools");

        /// <summary>Tool execution duration.</summary>
        public static readonly MetricDescriptor ToolDuration = new MetricDescriptor("hugo.tool.duration", MetricKindEnum.Histogram, "s", "connector, tool", "Tool execution time, excluding approval waits", "server", "Tools and Connectors", "hugo-tools", TelemetryBuckets.Request);

        /// <summary>Tool output size.</summary>
        public static readonly MetricDescriptor ToolOutputBytes = new MetricDescriptor("hugo.tool.output.bytes", MetricKindEnum.Histogram, "By", "connector", "Tool output returned to the caller, after scrubbing and limits", "server", "Tools and Connectors", "hugo-tools", TelemetryBuckets.Bytes);

        /// <summary>Approvals decided.</summary>
        public static readonly MetricDescriptor Approvals = new MetricDescriptor("hugo.approvals", MetricKindEnum.Counter, "{approval}", "outcome", "Approvals by outcome (requested, approved, rejected, expired, cancelled)", "server", "Tools and Connectors", "hugo-tools");

        /// <summary>Approval wait.</summary>
        public static readonly MetricDescriptor ApprovalWait = new MetricDescriptor("hugo.approval.wait", MetricKindEnum.Histogram, "s", "effect", "Time from an approval request to its decision or expiry", "server", "Tools and Connectors", "hugo-tools", TelemetryBuckets.Run);

        /// <summary>Instruction compile duration.</summary>
        public static readonly MetricDescriptor InstructionsCompileDuration = new MetricDescriptor("hugo.instructions.compile.duration", MetricKindEnum.Histogram, "s", "cached", "Time to compile a profile's instructions (cached is true when served from the compile cache)", "server", "Agent and Chat", "hugo-agent", TelemetryBuckets.Fast);

        /// <summary>Connector model requests.</summary>
        public static readonly MetricDescriptor ConnectorModelRequests = new MetricDescriptor("hugo.model.connector.requests", MetricKindEnum.Counter, "{request}", "provider, purpose, outcome", "Model calls made by connector tools through the calling profile's inference endpoint (IConnectorModel), by purpose and outcome (ok, error, budget_exhausted)", "server", "Models", "hugo-models");

        /// <summary>Connector model tokens.</summary>
        public static readonly MetricDescriptor ConnectorModelTokens = new MetricDescriptor("hugo.model.connector.tokens", MetricKindEnum.Counter, "{token}", "provider, purpose, direction", "Tokens used by connector model calls, by purpose and direction (input, output)", "server", "Models", "hugo-models");

        /// <summary>Model endpoint health checks.</summary>
        public static readonly MetricDescriptor ModelHealthChecks = new MetricDescriptor("hugo.model.health.checks", MetricKindEnum.Counter, "{check}", "provider, kind, outcome", "Model endpoint validations and health sweep checks by outcome (healthy, unhealthy)", "server", "Models", "hugo-models");

        /// <summary>Agent turns.</summary>
        public static readonly MetricDescriptor AgentTurns = new MetricDescriptor("hugo.agent.turns", MetricKindEnum.Counter, "{turn}", "surface, outcome", "Agent loop turns by surface (chat, a2a, openai, mcp, rest) and outcome (completed, max_iterations, tool_call_limit, approval_pending, cancelled, error)", "server", "Agent and Chat", "hugo-agent");

        /// <summary>Agent turn duration.</summary>
        public static readonly MetricDescriptor AgentTurnDuration = new MetricDescriptor("hugo.agent.turn_duration", MetricKindEnum.Histogram, "s", "surface", "Agent turn duration, including tool calls and approval waits", "server", "Agent and Chat", "hugo-agent", TelemetryBuckets.Run);

        /// <summary>Agent stage duration.</summary>
        public static readonly MetricDescriptor AgentStageDuration = new MetricDescriptor("hugo.agent.stage_duration", MetricKindEnum.Histogram, "s", "stage", "Agent turn stage duration by stage (context_build, compaction, tool_retrieval, inference, tool, approval_wait, persist)", "server", "Agent and Chat", "hugo-agent", TelemetryBuckets.Request);

        /// <summary>Agent iterations per turn.</summary>
        public static readonly MetricDescriptor AgentIterations = new MetricDescriptor("hugo.agent.iterations", MetricKindEnum.Histogram, null, "", "Model iterations per agent turn", "server", "Agent and Chat", "hugo-agent", new double[] { 1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 64 });

        /// <summary>Model requests.</summary>
        public static readonly MetricDescriptor ModelRequests = new MetricDescriptor("hugo.model.requests", MetricKindEnum.Counter, "{request}", "provider, kind, outcome", "Inference and embedding requests by provider, kind (chat, embed), and outcome (ok, error, fallback)", "server", "Models", "hugo-models");

        /// <summary>Model request duration.</summary>
        public static readonly MetricDescriptor ModelRequestDuration = new MetricDescriptor("hugo.model.request_duration", MetricKindEnum.Histogram, "s", "provider, kind", "Model request duration, including retries", "server", "Models", "hugo-models", TelemetryBuckets.Run);

        /// <summary>Model time to first token.</summary>
        public static readonly MetricDescriptor ModelTimeToFirstToken = new MetricDescriptor("hugo.model.time_to_first_token", MetricKindEnum.Histogram, "s", "provider", "Time from a streaming model request to its first token", "server", "Models", "hugo-models", TelemetryBuckets.Request);

        /// <summary>Model tokens.</summary>
        public static readonly MetricDescriptor ModelTokens = new MetricDescriptor("hugo.model.tokens", MetricKindEnum.Counter, "{token}", "provider, direction", "Model tokens by provider and direction (input, output)", "server", "Models", "hugo-models");

        /// <summary>A2A requests.</summary>
        public static readonly MetricDescriptor A2ARequests = new MetricDescriptor("hugo.a2a.requests", MetricKindEnum.Counter, "{request}", "method, outcome", "A2A JSON-RPC requests by canonical method (SendMessage, SendStreamingMessage, GetTask, ListTasks, CancelTask, SubscribeToTask, GetExtendedAgentCard, unknown) and outcome (ok, error)", "server", "Protocols", "hugo-protocols");

        #endregion

        #region Private-Members

        private static readonly List<MetricDescriptor> _All = new List<MetricDescriptor>
        {
            HttpRequests,
            HttpRequestDuration,
            HttpRequestSize,
            HttpResponseSize,
            HttpInFlight,
            AuthFailures,
            AuthRateLimited,
            AuthzDenials,
            AuthCacheLookups,
            AuthTokensIssued,
            AuditEvents,
            SseStreamsActive,
            SseEvents,
            McpRequests,
            BackgroundDuration,
            BackgroundIterations,
            BuildInfo,
            DbDuration,
            DbErrors,
            DbConnections,
            DbWriteLockWait,
            DbRetries,
            RetentionDeleted,
            RequestHistoryCaptured,
            OutboundRequests,
            OutboundDuration,
            ObservabilityReachable,
            LabelsFolded,
            TelemetryDiagnostics,
            AuthRequests,
            AuthzRequests,
            SecretOperations,
            SecretRedactions,
            BackgroundJobs,
            BackgroundJobDuration,
            ToolInvocations,
            ToolDuration,
            ToolOutputBytes,
            Approvals,
            ApprovalWait,
            InstructionsCompileDuration,
            ConnectorModelRequests,
            ConnectorModelTokens,
            ModelHealthChecks,
            AgentTurns,
            AgentTurnDuration,
            AgentStageDuration,
            AgentIterations,
            ModelRequests,
            ModelRequestDuration,
            ModelTimeToFirstToken,
            ModelTokens,
            A2ARequests
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Every descriptor, in catalog order.
        /// </summary>
        /// <returns>Descriptors (a new list).</returns>
        public static List<MetricDescriptor> All()
        {
            return new List<MetricDescriptor>(_All);
        }

        /// <summary>
        /// Find a descriptor by instrument name.
        /// </summary>
        /// <param name="name">Instrument name.</param>
        /// <returns>Descriptor, or null.</returns>
        public static MetricDescriptor? Find(string name)
        {
            return _All.FirstOrDefault(d => String.Equals(d.Name, name, StringComparison.Ordinal));
        }

        #endregion
    }
}
