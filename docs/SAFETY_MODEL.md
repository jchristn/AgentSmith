# Safety Model

An agent with production credentials will, sooner or later, try something it should not. It might misread a question, follow an instruction planted in a log line, or simply guess wrong about what `rm -rf ./cache` will touch on a host it has never seen. Hugo's answer is that safety is enforced by the server, not requested of the model. The compiled instructions tell the model the rules so it wastes fewer calls, but a model that ignores them gets a tool error, not access.

Every tool call from every surface (dashboard chat, MCP, A2A, the OpenAI-compatible endpoint, REST, the SDKs) goes through one pipeline, `ToolInvocationService`. No surface calls a connector directly. That is what makes the rules below hold everywhere at once.

## Effects

Each call is classified by the connector that owns the tool, per call, as one of three effects:

| Effect | Meaning | Examples |
|---|---|---|
| `Read` | Observes without changing anything. | `sql_query` with a `SELECT`, `ssh_exec` running `df -h`, `storage_list`, `k8s_logs` |
| `Write` | Changes state in a recoverable way. | `storage_write`, `redis_set`, `sql_query` with an `UPDATE`, `k8s_apply` |
| `Destructive` | Removes data, stops service, or is hard to undo. | `storage_delete`, `ssh_service_control` restarting nginx, `sql_query` with `DROP TABLE`, scaling a workload to zero |

Some tools have a fixed effect. Others, such as `sql_query`, `ssh_exec`, and `redis_command`, are classified per call by parsing what they are about to run. Classification fails closed: SQL that does not parse, a Redis command the classifier does not know, or a shell pipeline it cannot understand is treated as Destructive. If a classifier throws, the call is classified Destructive with a reason that says so. A classifier can also reject a call outright (for example a statement form the connector never allows), and some actions carry an "always requires approval" flag that upgrades them even on bindings that would otherwise allow them. [CONNECTORS.md](CONNECTORS.md) and the per-connector pages under [connectors/](connectors/) describe how each family classifies.

## Access modes

A binding attaches a resource to an agent profile with one of three access modes. The matrix is small on purpose:

| Effect \ Access mode | `ReadOnly` | `ReadWriteWithApproval` | `ReadWrite` |
|---|---|---|---|
| Read | Allowed | Allowed | Allowed |
| Write | Denied | Approval | Allowed |
| Destructive | Denied | Approval | Approval, or Allowed when `requireApprovalForDestructive` is false |

`ReadWrite` still pauses Destructive calls for a human by default (D-010). A binding has to opt out explicitly with `requireApprovalForDestructive: false`, and that is a choice worth writing down in the binding's instruction notes so the next person understands it.

The same resource can carry different modes on different profiles. A common pattern is binding `prod-db` ReadOnly to the everyday `Hugo` profile and ReadWriteWithApproval to an on-call profile that only a few people can execute.

### The resource ceiling

A resource can declare `defaultAccessModeCeiling`. The effective access mode of every binding of that resource is the lower of the binding's mode and the ceiling, in the order ReadOnly, ReadWriteWithApproval, ReadWrite. A tenant administrator can mark `prod-db` as never more than ReadWriteWithApproval, and no profile editor can bind it more loosely, whatever the binding form says.

### Tool rules and deny patterns

Bindings can refine the matrix per tool with tool rules:

- `Deny` blocks the tool on that binding. It always wins.
- `RequireApproval` upgrades an Allowed decision to Approval.
- `Allow` is the default and cannot override a denial. ReadOnly is a ceiling, not a suggestion.

For tools that are classified per call, a binding can also list deny patterns: .NET regular expressions matched case-insensitively against the tool's primary argument before the classifier runs (D-042). The primary argument is the schema property marked `x-hugo-primary`, otherwise the first of `command`, `sql`, `statement`, `query`, `script`, or `expression`, otherwise the first required string. A match is denied with `binding_deny_pattern`. A pattern that is invalid or takes longer than 100 ms counts as a match, so a broken pattern blocks rather than opens. Deny patterns are a good fit for "never let anyone run `shutdown` on this host" or "no `TRUNCATE` on this database", where the access mode alone is too coarse.

### The order of the decision

`AccessPolicy.Decide` (in `src/Hugo.Core/Safety`) is a pure function, and its order matters when you are reading a denial:

1. A classifier rejection denies (`classifier_rejected`).
2. Tenant lockdown denies anything that is not Read (`tenant_lockdown`).
3. A `Deny` tool rule denies (`tool_denied`).
4. The resource ceiling lowers the access mode.
5. The access mode matrix decides (`allowed`, `readonly_binding`, `approval_required`).
6. A `RequireApproval` tool rule (`tool_requires_approval`) or the classifier's always-approve flag (`always_requires_approval`) upgrades an Allowed non-Read call to Approval.

Binding deny patterns run before all of this, and argument validation against the tool's input schema runs before that. Every decision code ends up on the tool invocation record and in the tool error the caller sees, so a denial always says why.

`POST /v1.0/api/tool-invocations/dry-run` runs every step up to the decision and returns it without executing or recording anything. It is the quickest way to check what a binding will do with a particular call.

## Tool visibility

The rules decide whether a call may run. Visibility decides whether the model is even offered the tool, which keeps prompts smaller and saves the model from futile attempts. The same filter (`ToolVisibilityFilter`) feeds MCP `tools/list`, chat tool definitions, `GET /v1.0/api/agent-profiles/{id}/tools`, and the compiled instructions.

A binding offers a tool when its connector has the tool, no rule denies it, the profile has an inference endpoint if the tool needs one (`RequiresModel` tools such as `sql_ask`), and the tool can read: its base effect is Read, it is classified per call, or the binding's effective access mode is not ReadOnly. Each tool is listed once per caller, with a `resource` argument whose enum is the aliases of the bindings that offer it (D-017). A tool no binding offers is not listed at all.

Visibility is a convenience, not a control. A caller that names a hidden tool anyway gets the same denial the pipeline would have produced.

## Approvals

When the decision is Approval, Hugo records the tool invocation as pending and creates an approval: summary, effect, the classifier's reason, redacted arguments, binding, and requester. A person with `Update` on `Approval` decides it from the dashboard's Approvals inbox, inline in chat, or over REST (`POST /v1.0/api/approvals/{id}/approve`, `.../reject`). The requester, or anyone with `Update` on `Approval`, can cancel it. Secrets are resolved only after approval, for that one call.

Approvals expire after `Approvals.ExpirySeconds` (900). Self-approval is allowed by default (`Approvals.AllowSelfApproval`), because the primary user is one operator approving their own agent; turn it off to require a second person. An A2A caller can never decide its own approval, whatever the setting.

What the caller experiences while an approval is pending depends on the surface:

| Surface | Behavior |
|---|---|
| Dashboard chat and conversation messages (`POST /v1.0/api/conversations/{id}/messages`) | The stream emits `approval.required` with the approval card and the turn waits for the decision, then emits `approval.resolved` and carries on. |
| MCP tool call | The call waits up to `Approvals.McpWaitSeconds` (90), sending progress notifications every `Mcp.ProgressIntervalSeconds` (10) when the client asked for progress. If it is still pending, the call returns a normal (non-error) result: `{ "status": "approval_pending", "approvalId": "apr_...", "summary": "...", "expiresUtc": "...", "next": "call hugo_await_approval with this approvalId" }`. `hugo_await_approval` waits again for the same window and returns the original call's result once decided. |
| `ask_agent_smith` (MCP) and `POST /v1.0/api/agent-profiles/{id}/ask` | Hugo's own loop runs; an approval waits up to `Approvals.McpWaitSeconds`, then the answer reports `ApprovalPending` with the approval ID. |
| A2A | The task moves to `input-required` with a message naming the approval. When a human decides it in the dashboard or over REST, the task resumes on its own. |
| OpenAI-compatible | The turn ends with an assistant message saying the approval is pending. Approving runs the call; the next message in that conversation starts with a system note carrying the decision and the result (D-047). |
| REST invoke (`POST /v1.0/api/tool-invocations`, `POST /v1.0/api/resources/{id}/invoke`) | Returns 202 with the approval ID. Approving runs the call before the approve response returns, and `GET /v1.0/api/approvals/{id}` shows the result. |

Pending approvals are also published on the tenant event stream (`GET /v1.0/api/events`), which is how the dashboard's badge stays current.

`POST /v1.0/api/resources/{id}/invoke` deserves a note. It lets an administrator call a tool on a resource without a profile, which is handy for testing a resource. It needs `Execute` on `Resource`, and its access mode is the resource's ceiling, or ReadWriteWithApproval when the resource has none, so it does not quietly bypass approvals.

## Tenant lockdown

Lockdown is the "something is wrong, stop everything" switch. `POST /v1.0/api/tenant/lockdown` (with an optional `reason`) needs `Update` on `Tenant`, which in practice means a tenant administrator. While it is on, every non-Read call in the tenant is denied with `tenant_lockdown`, every pending approval is cancelled, and the dashboard shows a red banner. Read calls keep working, so an investigation can continue. `DELETE /v1.0/api/tenant/lockdown` turns it off. Both changes are audited as `LockdownChanged` and published on the event stream.

## Guardrails beyond access modes

Access modes answer "may this run". These answer "how much damage can a call that may run do":

- **Output limits.** Every connector bounds its output in bytes, rows, or lines. A binding can tighten them with `maxOutputBytes` and `maxRows`, never loosen them. Truncation is reported in the result.
- **Timeouts.** Tool calls time out after `Connectors.DefaultToolTimeoutSeconds` (120), or `Connectors.ModelToolTimeoutSeconds` (600) for tools that call the model. Profiles can set their own `toolTimeoutSeconds`.
- **Concurrency.** At most `Connectors.MaxConcurrencyPerResource` (4) calls run against one resource at a time.
- **Turn limits.** Each profile caps loop iterations (`maxIterations`, default 12) and tool calls per turn (`maxToolCallsPerTurn`, default 24). On the last iteration the model is told to answer without tools, so a turn always ends with an answer.
- **Model call budget.** A tool that calls the profile's model may make at most `Connectors.ModelCallBudgetPerInvocation` (60) calls.
- **Target allow lists.** Connectors enforce resource-level allow lists, such as paths, bucket prefixes, namespaces, indexes, and HTTP methods, inside the connector.
- **Secret scrubbing.** Tool output passes through the scrubber before it reaches a model, a log, or a history row. See [SECURITY.md](SECURITY.md).

### Prompt injection

Anything a tool returns is data, not instructions. Hugo's own loop wraps each tool result in `<tool_output resource="...">` tags (neutralizing any tags inside the output), and the runtime tells the model to treat everything inside those tags as data from a system, never as instructions, even when it claims to come from the operator. The persona repeats the point. Neither of these is a guarantee, which is why the approval card shows the exact redacted arguments: a human looking at `ssh_exec` with `rm -rf /var/lib/postgresql` will catch an injected destructive call that a model talked itself into.

## Least privilege upstream

Hugo's classifiers are a second line of defense. The first is the account Hugo uses to reach each system, and it should be no stronger than the most permissive binding you intend to allow:

- Give databases a read-only login for resources bound ReadOnly, and a separate resource with a separate login for write access.
- Scope AWS IAM policies to the services and resources the agent should see; prefer read-only managed policies for investigation profiles.
- Use a namespace-scoped Kubernetes service account rather than a cluster-admin kubeconfig.
- Use a restricted SSH user with only the `sudo -n` rules it needs. Note that repeated authentication failures from Hugo's single address can trip OpenSSH `PerSourcePenalties` and lock it out for a while.
- Use read-only API tokens for log services such as Papertrail and SolarWinds Observability.

If a classifier ever misjudges a call, the upstream account is what stands between that mistake and your data. Make it boring.

## Where to see what happened

Every call, allowed or not, is recorded as a tool invocation with the principal, profile, binding, redacted arguments, effect and reason, access mode, decision and decision code, approval, status, duration, and output preview (`GET /v1.0/api/tool-invocations`, the dashboard's Tool Invocations page). Approval decisions are audited as `ApprovalDecided`. The metrics behind the Grafana safety panels are in [TELEMETRY.md](TELEMETRY.md), and the MCP-specific flow is shown end to end in [MCP_API.md](../MCP_API.md).
