# Resource context

Hugo gives every registered connector, including plugins, two shared tools: `resource_get_context` and `resource_update_context`. Agents can record durable discoveries about SSH hosts, databases, APIs, files, and other connection points. Notes live in Hugo's `resource_knowledge` database and version history, rather than on the connected system.

Context belongs to a tenant and resource ID. Profiles and aliases bound to the same resource share it; separate resources have separate context. It survives server restarts and new conversations. Operators can inspect and curate it in the resource's **Knowledge** dashboard tab.

## Reading and recording discoveries

The normal tool invocation envelope selects the resource by its binding alias. For example:

```json
{"resource":"web-01"}
```

Call `resource_get_context` with no scope or key to list the resource's saved entries. Supply `scope` to filter, or `scope` and `key` to read one entry. A key without a scope uses `context`. `offset` skips entries in list output so later entries remain accessible; `contentOffset` with an explicit `key` skips characters within that entry, allowing long notes to be read in chunks. Offsets refer to the scrubbed content. Binding output limits also apply.

Call `resource_update_context` to save a concise, confirmed discovery:

```json
{
  "resource":"web-01",
  "scope":"host",
  "key":"nginx",
  "content":"Nginx configuration is in /etc/nginx; the service is managed with systemctl. Application logs are in /srv/shop/logs/worker.log.",
  "expectedVersion":0
}
```

`scope` defaults to `context`, `key` to `_root`, and `mode` to `append`. Read an existing entry first and supply its version. Version zero means the entry must be absent. Concurrent changes return `version_conflict` without changing the entry; reread before retrying. Even when an explicit version is omitted, Hugo checks the version it read before committing. Successful writes retain provenance and create version history and an audit event.

Agents may append to operator-authored entries, but cannot replace them. Other entries support `mode: "replace"`. Each contribution is limited to 4,000 characters and the merged entry to 16,000. Scope names allow up to 128 characters and keys up to 512. Credential material, obvious raw row dumps, and excessive personal data are refused. Store stable paths, capabilities, schema relationships, and verified operating guidance; avoid credentials, raw query results, transient metrics, and speculative facts presented as confirmed.

Existing SQL context tools use the same store. Their database summary uses `database` / `_root`; table entries use `table` / the qualified table name. Generic tools can read those entries, and SQL updates use optimistic version checks as well.

## Consultation during operations

For Hugo-hosted agent conversations, Hugo reloads authorized resource context before every model call, including planning, final responses, and compaction. The snapshot is transient rather than accumulated in conversation history. Connector-internal model calls, such as SQL question answering, also receive freshly loaded context before each completion.

Context is explicitly fenced as untrusted data. It cannot grant permissions or override approvals. The model decides how to apply the facts to the requested operation; Hugo does not rewrite SSH commands or SQL arguments behind the caller's back.

External MCP clients receive context in compiled resource instructions and guidance to call `resource_get_context` before resource work. The client controls its own model loop, so Hugo cannot force that external model to consult a tool. Long context can be fetched explicitly. `Instructions.MaxKnowledgeCharsPerResource` bounds automatic context per resource (default 2,000 characters, maximum 100,000; zero disables automatic inclusion).

## Permissions and limits

Both tools run through the normal resource binding and tool authorization pipeline. Updating notes requires `AllowKnowledgeUpdates`; tenant lockdown also blocks updates. Hugo rechecks write permission after an approval wait. A target binding may remain `ReadOnly` while allowing Hugo knowledge updates, because these writes affect Hugo rather than the target. MCP correctly advertises the update tool as a writer.

Output is bounded by resource limits and the tool's 12,000-character ceiling. Large inventories should be organized into scoped entries and fetched explicitly rather than relying on the automatic prompt budget. No embeddings, semantic search, or automatic saving of every tool result is implied: the agent explicitly records reusable discoveries. Embedded custom knowledge stores must implement `IVersionedResourceKnowledgeStore` to support the generic update tool safely.
