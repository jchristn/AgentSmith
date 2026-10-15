# Telemetry

This file is generated from `HugoMetricCatalog` (`src/Hugo.Core/Telemetry`). Do not edit it by hand: run the test suites with `HUGO_WRITE_DOCS=true` to regenerate it, and the `telemetry-document` test fails while it is out of date.

Hugo records metrics through the `Hugo` meter and spans through the `Hugo` activity source. Watson adds its own HTTP server metrics and one span per request. Radiant exports metrics to Prometheus (the `/metrics` scrape) and traces to OTLP when `Telemetry.OtlpEnable` is true. Label values are bounded by `LabelGuard`; identifiers never appear in labels.

## Metrics

| Metric (Prometheus) | Type | Labels | Category | Meaning |
|---|---|---|---|---|
| `hugo_agent_iterations` | histogram | none | Agent and Chat | Model iterations per agent turn |
| `hugo_agent_stage_duration_seconds` | histogram | `stage` | Agent and Chat | Agent turn stage duration by stage (context_build, compaction, tool_retrieval, inference, tool, approval_wait, persist) |
| `hugo_agent_turn_duration_seconds` | histogram | `surface` | Agent and Chat | Agent turn duration, including tool calls and approval waits |
| `hugo_agent_turns_total` | counter | `surface`, `outcome` | Agent and Chat | Agent loop turns by surface (chat, a2a, openai, mcp, rest) and outcome (completed, max_iterations, tool_call_limit, approval_pending, cancelled, error) |
| `hugo_instructions_compile_duration_seconds` | histogram | `cached` | Agent and Chat | Time to compile a profile's instructions (cached is true when served from the compile cache) |
| `hugo_background_job_duration_seconds` | histogram | `job` | Background | Duration of one background job run |
| `hugo_background_jobs_total` | counter | `job`, `outcome` | Background | Background job runs (retention, master key rotation, and so on) by outcome (ok, error) |
| `hugo_background_duration_seconds` | histogram | `service`, `operation`, `result` | Control plane | Duration of one iteration of a background service (retention, settings refresh, key rotation) |
| `hugo_background_iterations_total` | counter | `service`, `operation`, `result` | Control plane | Background service iterations by result (ok, idle, error) |
| `hugo_build_info` | gauge | `version`, `process` | Control plane | Always 1; carries the Hugo version and process |
| `hugo_db_connections_in_use` | gauge (up/down) | `provider` | Database | Database connections open in this process (Postgresql pooling detail is in the npgsql_* metrics) |
| `hugo_db_errors_total` | counter | `provider`, `operation`, `table`, `error` | Database | Database statements that threw, by exception type |
| `hugo_db_operation_duration_seconds` | histogram | `provider`, `operation`, `table`, `result` | Database | Database statement and transaction duration by provider, operation (select, insert, update, delete, transaction, ...), and table |
| `hugo_db_transaction_retries_total` | counter | `provider` | Database | Transactions retried after a transient conflict (serialization failure, busy database) |
| `hugo_db_write_lock_wait_seconds` | histogram | `provider` | Database | Time waiting for the Sqlite write lock |
| `hugo_request_history_captured_total` | counter | `result` | Database | Request history entries written or dropped |
| `hugo_retention_deleted_total` | counter | `kind` | Database | Rows removed by retention (request history, audit, sessions, nonces) |
| `hugo_http_request_duration_seconds` | histogram | `method`, `route`, `status_class`, `tenant` | HTTP | Time from the request arriving to the response being sent |
| `hugo_http_request_size_bytes` | histogram | `method`, `route` | HTTP | Request body size |
| `hugo_http_requests_total` | counter | `method`, `route`, `status_code`, `status_class`, `tenant` | HTTP | REST requests handled, by route template (never the raw path), method, status, and tenant. Requests the client abandoned (for example a closed browser tab) have status_code 499 and status_class aborted, never 5xx |
| `hugo_http_requests_in_flight` | gauge | none | HTTP | REST requests being handled right now, including long polls and streams |
| `hugo_http_response_size_bytes` | histogram | `method`, `route` | HTTP | Response body size (0 for streamed responses) |
| `hugo_observability_reachable` | gauge | `service` | Integrations | 1 when the server's last probe reached the observability service (Grafana, Prometheus, Loki, Tempo), 0 when not; absent when not configured |
| `hugo_outbound_duration_seconds` | histogram | `service`, `operation`, `result` | Integrations | Outbound call duration |
| `hugo_outbound_requests_total` | counter | `service`, `operation`, `result` | Integrations | Outbound calls (observability probes, metrics relay, and later connectors) by result (ok, error, status class) |
| `hugo_model_connector_requests_total` | counter | `provider`, `purpose`, `outcome` | Models | Model calls made by connector tools through the calling profile's inference endpoint (IConnectorModel), by purpose and outcome (ok, error, budget_exhausted) |
| `hugo_model_connector_tokens_total` | counter | `provider`, `purpose`, `direction` | Models | Tokens used by connector model calls, by purpose and direction (input, output) |
| `hugo_model_health_checks_total` | counter | `provider`, `kind`, `outcome` | Models | Model endpoint validations and health sweep checks by outcome (healthy, unhealthy) |
| `hugo_model_request_duration_seconds` | histogram | `provider`, `kind` | Models | Model request duration, including retries |
| `hugo_model_requests_total` | counter | `provider`, `kind`, `outcome` | Models | Inference and embedding requests by provider, kind (chat, embed), and outcome (ok, error, fallback) |
| `hugo_model_time_to_first_token_seconds` | histogram | `provider` | Models | Time from a streaming model request to its first token |
| `hugo_model_tokens_total` | counter | `provider`, `direction` | Models | Model tokens by provider and direction (input, output) |
| `hugo_a2a_requests_total` | counter | `method`, `outcome` | Protocols | A2A JSON-RPC requests by canonical method (SendMessage, SendStreamingMessage, GetTask, ListTasks, CancelTask, SubscribeToTask, GetExtendedAgentCard, unknown) and outcome (ok, error) |
| `hugo_mcp_requests_total` | counter | `method`, `outcome` | Protocols | MCP requests on /mcp by JSON-RPC method (initialize, tools/list, tools/call, ...; other for anything else) and outcome (ok, tool_error, error, stream, unauthenticated, forbidden, not_found, bad_request) |
| `hugo_sse_events_total` | counter | `stream` | Protocols | Server-sent events written |
| `hugo_sse_streams_active` | gauge | `stream` | Protocols | Server-sent event streams open on this server (events, chat, A2A) |
| `hugo_audit_events_total` | counter | `type` | Security | Audit records written, by event type |
| `hugo_auth_cache_lookups_total` | counter | `entity`, `result` | Security | Authentication lookups by record (session, tenant, user, credential) and result: hit (answered from the in-process cache, Auth.CacheSeconds) or miss (read from the database) |
| `hugo_auth_failures_total` | counter | `scheme`, `reason` | Security | Requests rejected by authentication, by scheme (BearerToken, AccessKeySignature, ...) and reason (Invalid, NotFound, Inactive, Missing) |
| `hugo_auth_rate_limited_total` | counter | `scope` | Security | Requests refused by the authentication rate limiter, by limiter scope |
| `hugo_auth_requests_total` | counter | `scheme`, `result` | Security | Authentication attempts by scheme and result (Success, Invalid, NotFound, Inactive, Missing) |
| `hugo_auth_tokens_issued_total` | counter | `scheme`, `principal` | Security | Session tokens issued (sign-ins and token exchanges) by scheme and principal type (User, Credential) |
| `hugo_authz_denials_total` | counter | `resource`, `operation`, `result` | Security | Authorization denials by resource type, operation, and result (Forbidden, NotFound) |
| `hugo_authz_requests_total` | counter | `result` | Security | Authorization decisions by result (Permitted, Bypassed, DeniedExplicit, DeniedImplicit, NotFound) |
| `hugo_secret_operations_total` | counter | `operation`, `outcome` | Security | Secret vault operations by operation (decrypt, reveal, rotate) and outcome (ok, error) |
| `hugo_secret_redactions_total` | counter | `kind` | Security | Values replaced by the secret scrubber, by kind (known, private_key, aws_access_key, bearer, connection_string, password_pair, generic) |
| `hugo_telemetry_diagnostics_total` | counter | `severity` | Telemetry | Diagnostics reported by the telemetry pipeline (export failures and the like) |
| `hugo_telemetry_labels_folded_total` | counter | `label` | Telemetry | Measurements whose label value was replaced by other because the label already had Telemetry.MaxLabelValues distinct values |
| `hugo_approval_wait_seconds` | histogram | `effect` | Tools and Connectors | Time from an approval request to its decision or expiry |
| `hugo_approvals_total` | counter | `outcome` | Tools and Connectors | Approvals by outcome (requested, approved, rejected, expired, cancelled) |
| `hugo_tool_duration_seconds` | histogram | `connector`, `tool` | Tools and Connectors | Tool execution time, excluding approval waits |
| `hugo_tool_invocations_total` | counter | `connector`, `tool`, `effect`, `decision`, `status`, `surface` | Tools and Connectors | Tool calls through the invocation pipeline by connector, tool, classified effect, decision (Allowed, Denied, ApprovalRequired, Approved, Rejected, Expired, Cancelled), execution status, and surface |
| `hugo_tool_output_bytes_bytes` | histogram | `connector` | Tools and Connectors | Tool output returned to the caller, after scrubbing and limits |
