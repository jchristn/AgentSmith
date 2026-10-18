# Prompts and Instructions

Every caller that reaches an agent profile, whether it is Claude Code over MCP, another agent over A2A, an OpenAI-compatible client, or you in dashboard chat, is handed one block of text before it does anything: the profile's compiled instructions. That text tells the model who it is, what it is allowed to do, and how to use each resource attached to the profile. This document explains where that text comes from, how to change it without losing the shipped defaults, and how to get back to those defaults when an edit goes wrong.

The short version: Hugo ships every piece of instruction text as a Built-in template, copies it into an editable Global layer, and lets each tenant override any key. Resources, bindings, and profiles add their own text on top. A small, safe template language stitches the pieces together per caller.

## Where the text comes from

Instruction text is layered. For each template key, Hugo uses the most specific layer that has an active row.

| Layer | Who edits it | Where it lives | Reset returns to |
|---|---|---|---|
| Built-in | Nobody. Ships in the server image. | Embedded resources in `Hugo.Core` and each connector assembly | Not applicable |
| Global | System administrators | `prompt_templates` rows with a null tenant | The Built-in content |
| Tenant | Tenant administrators, and roles with `Update` on `PromptTemplate` | `prompt_templates` rows with the tenant's ID | The Global layer |

Three more sources contribute text without being templates:

- **Resource notes.** The `notes` field of a resource appears in its section as "Operator notes". Anyone who can edit the resource can change it.
- **Binding overrides.** Each binding has an `instructionMode` and an `instructionText`. `Inherit` (the default) uses the connector's text and ignores `instructionText`. `Append` adds the binding's text after the connector's text. `Replace` uses only the binding's text, and falls back to the connector text when `instructionText` is empty, so a blank Replace never leaves a resource without guidance.
- **Profile append.** A profile's `instructionsAppend` is rendered at the very end under "Additional instructions from the operator".

The resource's saved context entries are included in its section, capped at `Instructions.MaxKnowledgeCharsPerResource` characters (default 2000; 0 turns it off). Hugo-hosted model calls receive a fresh authorized snapshot before each completion instead of retaining a stale compiled snapshot. See [resource context](RESOURCE_CONTEXT.md).

Some resources front a third party with its own guidance: an `mcp` resource has the external server's instructions, and an `a2a-agent` resource has the remote agent's card description and skills. That text is appended to the binding's section, after the connector instructions and any binding override, inside an `<untrusted-content source="...">` fence that tells the model to treat it as data, never as instructions. Before fencing, Hugo scrubs the resource's secret values out of the text, clips it to 4000 characters, and neutralizes any fence tag inside it, so a remote cannot close the fence early. The text comes from a per-resource cache. A compile waits at most two seconds for a first fetch. A remote that is slow or unreachable leaves the fence out rather than failing the compile, and a fetch that lands later refreshes the cached instructions. A binding in `Replace` mode with its own text omits the third-party text. The source map labels this segment `binding.thirdPartyInstructions`.

The Global layer is what makes the defaults editable. On every startup Hugo compares each Global row against its Built-in by SHA-256 hash. A Global row nobody has edited is quietly upgraded when a new release changes the Built-in, and the change is recorded as a new version. A Global row someone has edited is left alone, and its view reports `defaultUpdatedSinceCustomized: true` so you can look at the diff and decide whether to adopt the new default.

## Shipped template keys

The `templates` Touchstone suite verifies the shipped personas, safety and tool guidance, surface-specific
preambles, A2A skill descriptions, and resource sections for every approval mode. Run it with
`dotnet run --project src/Test.Automated -f net10.0 -- --suites templates`. The configuration integration
suite also checks layer edits, resets, compiled output, and cache invalidation against each database provider.

A fresh server lists the keys below at `GET /v1.0/api/prompt-templates`. Connector keys come in pairs, one full and one compact, for each of the twenty connector types.

| Key | Purpose |
|---|---|
| `instructions.root` | The composition root. Includes the persona, safety rules, tool usage, the surface preamble, one `section.resource` per binding, and the profile append. |
| `persona.agent-smith` | Default persona: Agent Smith, addressing the operator as Mr. Anderson. |
| `persona.neutral` | A plain persona for teams that would rather not have the theme. |
| `system.safety` | Effects, access modes, approvals, truncation. |
| `system.tool-usage` | How to use the `resource` argument, limits, and error codes. |
| `system.mcp-preamble` | Framing used when the caller's own model drives over MCP. |
| `system.a2a-preamble` | Framing for A2A tasks. |
| `section.resource` | Rendered once per binding. |
| `a2a.skill-description` | Agent card skill text. |
| `connector.<type>.instructions` | Full guidance for one connector type. |
| `connector.<type>.compact` | Short guidance used when a profile has many resources. |

The connector types are `a2a-agent`, `aws`, `azure-blob`, `cryptography`, `docker`, `elasticsearch`, `gcs`, `http`, `kubernetes`, `loki`, `mcp`, `mysql`, `postgresql`, `prometheus`, `redis`, `s3`, `solarwinds-observability`, `sqlite`, `sqlserver`, and `ssh`. A plugin connector registers its own pair the same way. You can also create a key that has no Built-in (any lowercase dotted name up to 128 characters) and include it from another template.

A profile chooses its persona through `personaTemplateKey`. Inside `instructions.root`, `{{> persona}}` resolves to that key, or to `persona.agent-smith` when the profile names none.

## How a profile's instructions are composed

`InstructionCompiler` renders `instructions.root` for one profile, one caller, and one surface. With the shipped root, the result reads in this order:

1. The persona.
2. `system.safety`, then `system.tool-usage`.
3. `system.mcp-preamble` on the MCP surface, or `system.a2a-preamble` on A2A. Chat and the OpenAI-compatible surface get no preamble.
4. "You have access to N resources", followed by `section.resource` for each binding the caller can actually use, ordered by connector family and then alias. Each section carries the alias, connector, environment, effective access mode, endpoint, description, the tools the caller can use on it, the connector instructions with the binding's Append or Replace applied, the operator notes, and the saved resource context.
5. The profile's `instructionsAppend`.

Connector instructions are written as standalone Markdown. Inside a resource section their headings are shifted down so the highest one becomes a level 3 heading, which keeps the compiled document's outline readable.

The compiled result carries a token estimate and a source map: for every segment of the text, the template key, the layer it came from, its version, and the binding alias where relevant. The dashboard's profile preview colors the text by source using that map. Results are cached per profile, caller, and surface, and any change to a template, profile, binding, resource, knowledge entry, or tenant name invalidates the cache for that tenant.

Rendering never fails a caller. If a customized template breaks the composition, Hugo renders every key from the Built-in layer instead and adds a warning to the result (and the server log). If one connector template fails to render, its text is used as written and a warning names the binding.

You can see exactly what any surface receives:

```bash
curl -s "http://127.0.0.1:8800/v1.0/api/agent-profiles/$PROFILE_ID/compiled-instructions?surface=Mcp" \
  -H "Authorization: Bearer $TOKEN"
```

`surface` is `Chat`, `Mcp`, `A2A`, or `OpenAI` (`Rest` compiles as `Chat`). Add `&text=true` to get the text alone as `text/plain`. The JSON form looks like this (text and segments abridged):

```json
{
  "profileId": "agp_mv927keb_YuOf8qpccRHoxCYgG7V",
  "profileName": "Hugo",
  "surface": "Mcp",
  "text": "You are Agent Smith, an operations agent working for Mr. Anderson ...",
  "tokenEstimate": 3059,
  "bindingCount": 1,
  "segments": [
    { "key": "instructions.root", "layer": "Global", "version": 1, "start": 0, "length": 12234, "depth": 0 },
    { "key": "persona.agent-smith", "layer": "Global", "version": 1, "start": 0, "length": 1688, "depth": 1 }
  ],
  "warnings": [],
  "fromCache": true,
  "compiledUtc": "2026-10-15T04:54:12.71986Z"
}
```

The same text reaches MCP callers in the `initialize` result, the `hugo_get_instructions` tool, the `hugo_briefing` prompt, and the `hugo://profile/instructions` resource. See [`MCP_API.md`](../MCP_API.md).

## Compact instructions

A profile with eight resources and full connector guidance runs to about 50 KB of instructions, most of which the model never needs on a given turn. Compact instructions (D-043) keep large profiles near 10 KB.

Each profile has an `instructionDetail` of `Auto` (the default), `Full`, or `Compact`. `Full` always uses `connector.<type>.instructions`. `Compact` always uses `connector.<type>.compact`. `Auto` uses compact text when the profile has more bindings than `Instructions.CompactBindingThreshold`, which defaults to 3 (0 means always compact). When compact text is in use, the root template tells the model that the guidance is abbreviated and that it should call `hugo_describe_resource` before anything beyond simple reads.

`hugo_describe_resource` and the MCP resource `hugo://resources/{alias}` always return the full connector instructions for that one binding, rendered through the same layers and with the binding's Append or Replace applied, whatever the profile's detail setting. Editing a compact template works exactly like editing a full one.

## The template language

Templates are rendered by a small renderer in `src/Hugo.Core/Templates`. It has no code execution and no filesystem access. A missing value renders as empty text.

| Syntax | Meaning |
|---|---|
| `{{profile.userAddress}}` | Insert a value by dotted path. |
| `{{path \| filter}}`, `{{path \| filter: arg}}` | Apply filters left to right. |
| `{{#if path}} ... {{else}} ... {{/if}}` | Render when the value is truthy. |
| `{{#unless path}} ... {{/unless}}` | Render when the value is falsy. |
| `{{#each path}} ... {{/each}}` | Repeat for each item of a list, or each entry of a map. |
| `{{this}}`, `{{this.alias}}`, `{{@index}}`, `{{@first}}`, `{{@last}}` | The current item and its position, inside `each`. |
| `{{> key}}` | Include another template, resolved through the layers. |
| `{{! comment }}` | Removed from the output. |
| `{{{{raw}}}} ... {{{{/raw}}}}` | Emit the enclosed text literally, braces and all. |

The include syntax is `{{> key}}`. Writing `{{> include key}}` looks for a template literally named `include key` and fails. Includes are limited in depth, cycles are rejected, and an include that does not exist is an error rather than an empty string.

A block tag that sits alone on its line removes that line from the output, so `{{#if ...}}` and `{{/if}}` on their own lines do not leave blank lines behind. Truthy means present and not empty, not `false`, and not zero.

Filters:

| Filter | Effect |
|---|---|
| `default: "text"` | Use the literal (or another path, unquoted) when the value is empty. |
| `upper`, `lower`, `trim` | Case and whitespace. |
| `json`, `toml` | Serialize the value. |
| `indent: n` | Indent every line after the first by `n` spaces (default 2, at most 64). |
| `truncate: n` | Keep the first `n` characters (default 200). |
| `join: ", "` | Join a list with a separator (default `", "`). |

An unknown filter is an error that lists the supported ones.

### Variables

The renderer rejects a path whose first segment is not a known root. The roots that hold values when instructions are compiled are these:

| Root | Fields |
|---|---|
| `profile` | `name`, `displayName`, `personaName`, `userAddress`, `description`, `instructionsAppend`, `hasBindings`, `bindingCount`, `singleBinding`, `familySummary` |
| `tenant` | `id`, `name` |
| `user` | `firstName`, `lastName`, `email` of the caller. A credential exposes its owner's names. Empty for internal callers. |
| `now` | `utc`, the current date as `yyyy-MM-dd` |
| `limits` | `maxIterations`, `maxToolCallsPerTurn`, `toolTimeoutSeconds` |
| `surface` | A map: `name` (`Chat`, `Mcp`, `A2A`, `OpenAI`), and the booleans `isChat`, `isMcp`, `isA2A`, `isOpenAI` |
| `hugo` | `version` |
| `bindings` | The list of usable bindings (fields below) |
| `bindingsByFamily` | A map from family (`sql`, `ssh`, ...) to that family's bindings |

Each item of `bindings` has `alias`, `connectorType`, `connectorDisplayName`, `family`, `accessMode`, `requireApprovalForDestructive`, `readOnly`, `approvalRequired`, `destructiveApproval`, `environment`, `endpoint`, `description`, `notes`, `tools` (each with `name`, `effect`, and `allowed`; `effect` is `Classified` for tools whose effect is decided per call), `toolList` (the usable tool names, comma separated), `knowledge`, `compact`, `instructions` (the rendered connector text, followed by any fenced third-party text), and `thirdPartyInstructions` (the fenced third-party text alone, or empty). `section.resource` and the connector templates are rendered with the current binding as `this`, so they use `{{this.alias}}` and friends.

The analyzer also accepts the roots `persona`, `binding`, `connector`, `resources`, `resource`, `tool`, `input`, `conversation`, and `approval`, but the instruction compiler does not populate them today, so they render empty. `secret` is a known root that is always refused in prompt templates. The render preview fills `input` from its `context` body, which makes it useful for trying parameterized text.

## Previewing a template

`POST /v1.0/api/prompt-templates/render` renders any content against a sample context (one `prod-db` PostgreSQL binding, Agent Smith, Mr. Anderson) and resolves includes through your tenant's layers. It needs `Read` on `PromptTemplate`, stores nothing, and always answers 200: check `success` and `errors`.

```bash
curl -s -X POST http://127.0.0.1:8800/v1.0/api/prompt-templates/render \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"content":"Hello {{profile.userAddress | upper}}, you have {{#each bindings}}{{this.alias}} ({{this.accessMode | lower}}){{#unless @last}}, {{/unless}}{{/each}}."}'
```

```json
{"rendered":"Hello MR. ANDERSON, you have prod-db (readonly).","success":true,"variables":["profile.userAddress","bindings"],"includes":[],"errors":[]}
```

Values in `context` are merged over the sample:

```bash
curl -s -X POST http://127.0.0.1:8800/v1.0/api/prompt-templates/render \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"content":"Investigate {{input.symptom}}.","context":{"input":{"symptom":"slow checkout"}}}'
```

A mistake comes back as an error with a line and column:

```json
{"rendered":"","success":false,"variables":[],"includes":["include system.safety"],"errors":["template 'preview' at line 1, column 1: Included template 'include system.safety' was not found."]}
```

Two differences from real compilation are worth knowing. The preview's `surface` is the plain string `Chat`, so `{{surface.name}}` and `{{#if surface.isMcp}}` behave differently there than in a compiled profile, and its `now.utc` is a full timestamp rather than a date. Use the compiled-instructions route above when the surface matters.

## Editing and resetting

All of these routes live under `/v1.0/api/prompt-templates` and act on your tenant. Keys are path segments, for example `connector.ssh.instructions`. The `layer` query parameter is `Tenant` (the default) or `Global`; changing the Global layer requires a system administrator and otherwise returns 403 `SystemAdminRequired`. `BuiltIn` is never writable.

| Method and path | What it does |
|---|---|
| `GET /v1.0/api/prompt-templates` | Every key with its Built-in content and hash, the Global and Tenant layer states, the layer in effect, and the effective content. |
| `GET /v1.0/api/prompt-templates/{key}` | The same view for one key. |
| `PUT /v1.0/api/prompt-templates/{key}?layer=Tenant` | Save content (`{"content": "...", "changeNote": "..."}`) and create a version. |
| `POST /v1.0/api/prompt-templates/{key}/reset?layer=Tenant` | Reset a layer. |
| `GET /v1.0/api/prompt-templates/{key}/versions?layer=Tenant` | Version history of a layer, newest first. |
| `GET /v1.0/api/prompt-templates/{key}/builtin` | The shipped content, hash, and description. |
| `GET /v1.0/api/prompt-templates/{key}/diff?layer=Tenant` | Line diff of a layer against the layer below it (`op` and `text` per line). |
| `POST /v1.0/api/prompt-templates/render` | Preview, described above. |

Saving validates the template first and rejects it with 400 `ValidationFailed` and a message naming the line and column. Content must be non-empty and at most 262,144 characters. Every save and reset is audited as `PromptTemplateChanged` and invalidates compiled instructions immediately.

A tenant override of the persona:

```bash
curl -s -X PUT "http://127.0.0.1:8800/v1.0/api/prompt-templates/persona.agent-smith?layer=Tenant" \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"content":"You are {{profile.personaName}}. Address the operator as {{profile.userAddress}}. Be brief.","changeNote":"Shorter persona for the on-call team"}'
```

The response is the key's view with `activeLayer: "Tenant"`.

Reset behaves differently per layer, and the difference matters:

- **Tenant reset** deactivates the tenant's override. The key falls back to the Global layer at once. The row and its version history stay, and the reset itself is recorded as a version, so nothing you wrote is lost. Saving to the Tenant layer again reactivates the row, and its customization base moves to whatever the Global layer holds at that moment.
- **Global reset** replaces the Global content with the current Built-in and records a version. If a release changed the Built-in since you customized it, this is how you adopt the new default. For a key with no Built-in, Global reset deactivates the Global row instead.

```bash
curl -s -X POST "http://127.0.0.1:8800/v1.0/api/prompt-templates/persona.agent-smith/reset?layer=Tenant" \
  -H "Authorization: Bearer $TOKEN"
```

### Known limits of editing

The save-time validator checks templates as if they were top-level text, which causes two problems with the shipped templates today. `section.resource` (and any connector template that uses `{{this.alias}}`) is rejected because `this` is only accepted inside `{{#each}}`, even though the compiler renders those templates with the binding as `this`. `instructions.root` is rejected because its `compactInstructions` flag is not on the analyzer's list of known roots. Until the validator learns about both, customize those two keys through the binding and profile fields above, or leave them at their defaults. Every other shipped key, including the personas, the safety and tool usage rules, the preambles, and the connector instructions that do not use `this`, saves normally.

## The tenant template library

A second set of routes, `/v1.0/api/tenants/{tenantId}/prompt-templates`, comes from the foundation Hugo was forked from. It manages templates by ID rather than by key and supports create, update, delete, versions, restore of an old version, clone, reset of a clone to its Built-in, and render. The dashboard's layered editor and the instruction compiler use the key-based routes above; reach for the ID-based ones only when you need restore-by-version or clone semantics. Both sets are listed in [`REST_API.md`](../REST_API.md).

## Practical advice

Start with the profile and binding fields before touching templates. A note on the resource ("orders is partitioned by month") or an Append on one binding ("this replica lags by up to a minute") is usually what the model was missing, and it stays scoped to the place it applies. Change a connector template when the guidance is wrong for every resource of that type in your tenant. Change the Global layer only when every tenant should see the change, and expect to compare it against new Built-ins at each upgrade.

Whatever you edit, check the compiled output for the surface you care about afterwards. The source map tells you which layer each paragraph came from, which is the fastest way to answer "why is it still saying that".

Related reading: [`docs/SAFETY_MODEL.md`](SAFETY_MODEL.md) for what the safety text describes, [`docs/CONFIGURATION.md`](CONFIGURATION.md) for the `Instructions.*` settings, and [`docs/UPGRADING.md`](UPGRADING.md) for what happens to customized templates when you upgrade.

## Teaching a profile through chat

An authorized profile editor can say, "When finding a user GUID, search the usermaster table by email." The agent uses `agent_get_instructions` and `agent_update_instructions` to reconcile that rule with the current profile's **Extra instructions** (`InstructionsAppend`). The same text is editable in the profile dashboard and persists across conversations. The model reads the concurrency version itself, consolidates related rules, preserves unrelated instructions, and retries conflicts after rereading. The user does not need to provide keys, Markdown, or a version.

Reading requires Read on the current profile; saving requires Update. The tools cannot select another profile, edit global safety or persona templates, grant permissions, or bypass approvals. Total profile instructions are limited to 65,536 characters. Secret-shaped content and known bound credentials are refused. Resource facts remain in resource context rather than becoming agent policy.

Hugo-hosted loops inject the current profile instructions freshly before each model call, including after same-turn updates. The static compiled prompt omits that field for these loops to avoid stale duplicate instructions. External MCP clients can read the current instructions using the tool; Hugo cannot control when an external model refreshes its prompt.

## Additional prompt documents

Agents can attach several named Markdown documents using Inline or Reference delivery. Inline includes the full selected revision; Reference advertises metadata and supplies `agent_list_documents` and `agent_read_document` for authorized retrieval. Working instructions, template layers and resource knowledge keep their existing roles. See [Agent prompt documents](AGENT_DOCUMENTS.md) for setup, permissions, revision pins and context limits.
