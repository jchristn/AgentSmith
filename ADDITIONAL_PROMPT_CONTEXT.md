# Additional agent prompt context: implementation plan

Status: complete. Created 2026-10-18. Product version remains `0.1.0-alpha`.

An operator must be able to attach several named Markdown documents to an agent and choose how each reaches the model. Inline attachments contribute their complete content to the system instructions. Reference attachments contribute a short catalog entry; the model retrieves their content through profile-scoped tools when needed. A filename in a prompt is not retrieval infrastructure. This feature supplies both the catalog and the retrieval path.

`LOG_MESSAGE_STRUCTURE.md` is the main acceptance example. An administrator uploads the file, describes when it is useful, attaches it to an agent, and chooses Reference. The model sees the filename and description without receiving the document body. Before interpreting logs, it calls `agent_read_document`, receives the document, and applies the documented structure. Switching the attachment to Inline places the whole body in the compiled system prompt instead.

## 1. Plan conventions and completion gates

Use stable `APC-` task IDs below in commits, tests and implementation notes. Mark work `[~]` while in progress, `[x]` only after its acceptance criteria pass, and `[!]` with a concrete blocker. Append `| implementer YYYY-MM-DD evidence` to completed items. Never mark a phase complete because its code was written without executing the required checks. Changes to decisions, contracts or limits must update this document before dependent work proceeds.

Current progress is **36 of 36 implementation tasks complete**. Migration 12, persistence, runtime tools, protocols, dashboard, all 12 locales, three SDKs and documentation are implemented and verified. Final regression and publication gates pass. The chat-navigation fix is tracked separately as P6.25 in the archived roadmap. Record implementation progress here and link the feature from `archive/HUGO_PLAN.md`; do not move the archived plan back to the repository root.

| Phase | Tasks | State | Completion gate |
|---|---|---|---|
| Contracts and persistence | APC-01 through APC-06 | Complete | SQLite and PostgreSQL migrations, persistence and concurrency tests pass |
| Authorization and management API | APC-07 through APC-12 | Complete | Scope map, role/credential tests, complete API contracts |
| Compilation, retrieval and protocols | APC-13 through APC-18 | Complete | Both modes work through every supported agent entry point |
| Dashboard and localization | APC-19 through APC-23 | Complete | Complete UI, all 12 languages, accessibility and responsive verification |
| SDKs and documentation | APC-24 through APC-29 | Complete | Three SDKs, API references and executable examples agree |
| Verification and release | APC-30 through APC-36 | Complete | Provider tests, live harnesses, simulated journeys and deployment evidence |

## 2. Requirements and repository constraints

The implementation must follow `~/Code/Agents/requirements` and the repository's `CLAUDE.md`. The table identifies the affected requirements and the evidence needed. Generic architecture examples do not authorize changing Hugo's established database support, routes, deployment model or HTTP framework.

| Requirement source | Application to this feature | Evidence |
|---|---|---|
| `CODE_STYLE.md` | Namespace first with usings inside; explicit types; one class/enum per file; public XML documentation; `_PascalCase` private fields; cancellation and `ConfigureAwait(false)`; validated properties; specific exceptions; no tuples or JSON DOM for fixed contracts | Reviewed diff and zero-warning builds for supported target frameworks |
| `BACKEND_ARCHITECTURE.md` | Typed services, validated DTOs, provider-neutral persistence, transaction boundaries, concurrency, no database calls from dashboard or protocol adapters | Service/persistence tests and architecture review |
| `AUTHENTICATION.md` | Existing authentication, role grants, credential scopes, tenant boundaries, denial precedence and operation map apply to every new route/tool | Authorization matrix and protocol integration tests |
| `BACKEND_TEST_ARCHITECTURE.md` | Shared Touchstone cases, reusable fixtures, real provider behavior, cleanup and assertions through existing runners | Registered suites on both application database providers |
| `FRONTEND_ARCHITECTURE.md` | Existing React architecture and shared API transport; typed server error mapping, state handling and URL-addressable tabs | Component and browser tests |
| `DASHBOARD_STYLE_AND_USABILITY.md` | Existing controls, dialogs, tables, copy controls, tooltips, keyboard support and responsive themes | Browser/accessibility and visual evidence |
| `I18N.md` | All interface text, errors, labels, hints, plurals and accessibility text translated in all 12 catalogs | Localization checker and representative-language browser checks |
| `TELEMETRY_REQUIREMENTS.md` | Correlated spans, bounded metrics, useful audit records, no document bodies or secrets in telemetry | Redaction and metric-label tests; telemetry documentation |
| `REPOSITORY_REQUIREMENTS.md` | Full REST/MCP references, documented Postman requests, SDK harnesses and READMEs, loopback `127.0.0.1` | Documentation/contract checks and SDK runs |
| `SIMULATED_USER_TESTING.md` | Isolated administrator, operator, SDK developer and restricted-user journeys; retain reproducible evidence | Ignored evidence directory and written findings |
| `BENCHMARKING.md` | Measure relevant compilation, retrieval and bounded-context costs on a documented fixture | Repeatable measurements with environment and fixture sizes |
| `EXAMPLE_APPLICATIONS.md` | Extend Hugo's existing examples/factory fixtures with a runnable document workflow | Synthetic example and cleanup instructions |
| `INSTALLERS.md` | Existing installation/deployment paths migrate existing databases and preserve data | Upgrade and restart checks; no new installation framework |
| `VERSIONING.md` | Do not change product/package/image version names without explicit instruction | Version mirror checks; approved existing tags at publication |
| `WRITING_DOCUMENTS.md` | Readable operator documentation, reviewed examples, no em dashes anywhere | Final prose review and repository punctuation check |

Preserve Hugo's explicit architecture decisions: Watson 7 is the HTTP host; management routes start with `/v1.0/api`; SQLite and PostgreSQL are the only application database providers; Docker Compose, Kubernetes and minikube are the deployment paths. The generic requirements' alternative route examples and additional database providers are not a mandate to expand Hugo. Keep existing manually parameterized SQL, `DatabaseDriverBase`, `TableCatalog`, `SchemaBuilder` and `SqlDialect`. Do not add a second ORM, web framework or vector service.

## 3. Product decisions and boundaries

### 3.1 Document library and attachments

Create a reusable, tenant-owned **Prompt document** library. A document has immutable identity, a human filename, a short description and versioned Markdown content. An agent attachment points to a document and supplies delivery mode, order, enabled state, selected revision policy and public-chat eligibility. One document can serve several agents without duplicating its body. Editing that document can affect every attachment following its current revision, so the editor must show its referrers before saving.

Attachment mode is exactly `Inline` or `Reference`. Reference is the default when attaching a document. There is no automatic mode switch and no silent fallback from a too-large Inline document to Reference. The preview must show the effective mode and cost. An attachment can follow `Latest` or pin an immutable revision; default `Latest`. Pinning prevents unrelated agents from changing behavior when a shared document is edited.

An attachment's filename must be unique within its agent, using case-insensitive normalized comparison. Use the document name rather than a second editable alias in this release. Documents have unique normalized names within a tenant. Display names preserve user casing; lookups use stored normalized names. Renaming a shared document updates Latest manifests and invalidates affected previews. Validate resolved attachment-name uniqueness transactionally on attach, rename, pin and repin across affected profiles, including collisions between pinned old names and another document's current name. Return a redacted conflict if an unauthorized referrer prevents the change. Stable IDs remain valid; pinned attachments retain the name and description from their pinned revision.

### 3.2 Meaning of external files

Support externally authored `.md` and `.txt` files through dashboard upload and SDK/REST import. The browser or SDK reads the selected file and submits its UTF-8 text; Hugo stores a managed versioned copy. Reference means the model retrieves that stored file's content on demand, not that the server fetches a path every turn. A user selects the external file on their own machine; Docker does not need access to that path.

The document body remains outside the system prompt in Reference mode, but necessarily enters model context as a tool result when read. Later calls can therefore include the retrieved content in conversation history. Explain that cost honestly in the dashboard and documentation.

Live filesystem mounts, automatic URL synchronization, remote connector reads as document sources, recursive includes and web crawling are out of scope. Never interpret the document filename as a server filesystem path, URL, template include or executable instruction. A future synchronized-source feature needs its own allowlist, authentication, refresh, failure and SSRF design. No implementation task in this plan depends on it.

### 3.3 Relationship to existing instructions and knowledge

Keep `AgentProfile.InstructionsAppend`, `agent_get_instructions`, `agent_update_instructions`, prompt-template layering and resource knowledge unchanged. Documents supplement those features. Agent instructions hold concise working rules; resource context holds learned resource facts; attached documents hold operator-authored detail such as schemas, runbooks and output conventions. Do not migrate one store into another or duplicate entire documents into saved agent instructions.

Document management is an explicit dashboard/API operation in this release. Agent tools list and read attachments only. Agents cannot create, rewrite, attach or delete documents through these tools. Add agent-side authoring later only with an explicit permission and conflict-handling design.

## 4. Domain model, persistence and migration

Use three new structured tables with tenant-qualified relations. Store Markdown in a text column on immutable revision rows, not as JSON, a secret field, a conversation message, or a file on an ephemeral container filesystem. Document content uses the existing prompt-content storage protection model and database backup controls. It is not a vault and must not contain secret material.

| Entity/table | Required fields and rules |
|---|---|
| `PromptDocument` / `prompt_documents` | `Id` (`pdc_` PrettyId), `TenantId`, `OwnerUserId`, `Visibility` using Hugo's existing owner/tenant semantics, `Name`, `NormalizedName`, `Description`, `Active`, `CurrentRevision`, `Version`, `CreatedUtc`, `LastUpdateUtc`. Tenant/name unique. Name 1..128 characters and description 1..1024 characters. Description is required because references must tell the model when to read. |
| `PromptDocumentRevision` / `prompt_document_revisions` | `Id` (`pdv_`), `TenantId`, `DocumentId`, monotonically increasing `Revision`, revision snapshot of `Name`/`Description`, canonical `Markdown`, UTF-8 `ContentBytes`, server-computed SHA-256 `ContentHash`, `CreatedByUserId`, `CreatedUtc`. Unique tenant/document/revision. Immutable after creation. No tool can read a revision outside the attachment's selected revision. |
| `AgentPromptDocument` / `agent_prompt_documents` | `Id` (`pda_`), `TenantId`, `AgentProfileId`, `DocumentId`, `DeliveryMode`, `RevisionPolicy` (`Latest`/`Pinned`), nullable `PinnedRevision`, integer `DisplayOrder`, `Enabled`, `AllowPublicAccess` default false, integer `Version`, timestamps. Unique tenant/profile/document. Pinned requires a valid revision; Latest requires null pin. |

Use Unicode NFC normalization, reject controls/NUL, `/`, `\`, path traversal and reserved dot names in filenames, and allow normal Unicode filenames with `.md` or `.txt`. Normalize name comparison invariantly before enforcing the database unique constraint. Accept UTF-8, strip a leading BOM, normalize CRLF to LF, and reject invalid encodings and binary/NUL input. Do not trim meaningful Markdown whitespace. Compute content hash and size after canonicalization. Document these rules in import errors and SDK examples.

Add `AgentProfile.DocumentSetVersion` as a read-only-in-DTO persisted integer column defaulting to 0. Increment it atomically on every attachment add/update/detach/reorder, including revision-policy or public-eligibility changes. Library document revisions do not mutate each referring profile row; their selected revision IDs participate separately in composition freshness. Attachment list/mutation responses return `documentSetVersion`, and mutations require the list's `expectedDocumentSetVersion` alongside the affected row's version where applicable.

Version tokens in this feature are JSON integers: `version` maps to document `Version` or attachment `Version` according to the endpoint, and `expectedVersion` carries that value. Revision numbers are separate immutable identities, not concurrency tokens. Set versions start at 0; entity versions/revisions start at 1. Increment only on committed effective changes, return the unchanged tokens on no-op saves, and do not wrap counters. Use integers within the JSON safe integer range and validate input explicitly in all SDKs. Restore is GET historical revision then PUT its reviewed name/description/Markdown to the current document with its current expectedVersion; this creates a new revision when content differs.

Add `PromptDocumentDeliveryModeEnum` and `PromptDocumentRevisionPolicyEnum` in the normal Core/SDK enum locations. Extend resource type enums, request DTOs, response DTOs and constants without reordering shipped enum values. Use separate files for each type and public XML documentation describing limits and defaults.

Append the next migration to `src/Hugo.Core/Database/Ado/SchemaBuilder.cs`; the baseline currently ends at migration 11, so use 12 if no other work reserves it first. Never edit an applied migration or hard-code an outdated current version in tests. Register tables in the current table catalog and expose typed database methods through `DatabaseDriverBase` and `Database/Interfaces`, implemented using the shared ADO/dialect pattern. Add indexes for tenant/name, tenant/profile/order, tenant/document referrers, and tenant/document/revision. Use tenant-qualified foreign keys or equivalent enforced service checks plus transactional constraints where existing database abstractions require them.

Creation and revision append are atomic. Metadata/content saves require `expectedVersion`, evaluated by an atomic update predicate inside the transaction; missing tokens return 400 and stale tokens return 409. Revision allocation, current-revision update, visibility checks and referrer/publication invalidation must not race. Attachment updates use their own version. Reorder accepts the complete current ordered attachment ID list and an attachment-set version; validate exact membership and atomically assign order. Never replace unrelated profile fields to save an attachment.

Deletion is explicit and predictable: deleting an attached document returns 409 with authorized referrer metadata and requires detaching first. Do not cascade away operator configuration. Profile deletion cascades its attachment rows but preserves library documents. Document deletion after detach removes its revisions transactionally. Deactivation immediately makes all its attachments unavailable for both modes. Restore requires an authorized metadata update. Profile cloning copies attachments to new attachment IDs with their mode/order/pins, shares document IDs, and resets `AllowPublicAccess` to false. No public publication or visitor credential is cloned.

## 5. Authorization and publication policy

Add `PromptDocument` as a first-class authorization resource. Management CRUD requires matching existing Create/Read/Update/Delete operations, owner/tenant visibility, credential scope restrictions and tenant membership. All attachment management requires profile Update and document Read. Enabling publication of a document additionally requires document Admin. The same Admin requirement applies when repinning or changing the revision policy of a public-eligible attachment, or re-enabling a disabled public-eligible attachment, even when AllowPublicAccess remains true; an ordinary reader must not be able to make a private document available publicly by attaching it to a public profile. Seed no new broad bypass role.

Authenticated execution requires profile Execute plus document Read for each document delivered inline or through the read tool. Do not expand profile Execute into tenant-library access. Authenticated manifests list only readable attachments. An enabled Inline attachment that the caller cannot read must fail compilation with a sanitized authorization/configuration error before inference, rather than silently changing required instructions. An unreadable Reference attachment is omitted from the manifest and cannot be guessed through its name or ID. The preview must explain these caller-specific differences to authorized managers.

Public standalone chat uses the existing publication credential and visitor capability, never the administrator's principal. Include only attachments with `AllowPublicAccess=true` and enabled document/profile/attachment state. Preserve current publication behavior: validate that the selected credential already has explicit PromptDocument Read grants for these document IDs within its owner's effective permissions, alongside the existing profile/conversation grants. An authorized security manager configures those grants through existing credential/role controls. Document/profile editing must never automatically add or remove scope assignments, elevate an owner, or modify a credential shared by another profile. Return an actionable missing-permission validation error before enabling affected public delivery. Inline and Reference share this rule. The public capability clamp independently restricts even a broadly granted credential to currently opted-in attachments of that published profile. Public visitors have no library management, arbitrary revision history, referrer enumeration or mutation rights.

Changing an attachment's public eligibility, document visibility/read policy, deletion/disablement or profile publication state must use Hugo's existing publication revision/revocation machinery. Newly started model calls and tool reads revalidate current rights; do not cache permission decisions globally. Latest content edits update the manifest revision without granting access to new documents. Changes to Markdown, name or description affecting any public-eligible Latest attachment require document Admin in addition to Update, even if that profile is temporarily unpublished. Update-only callers receive an actionable denial and retain their draft. Deactivation can still revoke delivery under Update without requiring Admin. The document editor must disclose public referrers and this permission requirement before saving a new revision. Pinned public attachments retain the approved revision until explicitly repinned. Publish preview shows exactly which document revisions a public visitor can receive and warns that their content can be disclosed.

Every management route gets an entry in `src/Hugo.Server/Routes/OperationScopeMap.cs`. Profile-scoped tools derive tenant/profile from the authenticated invocation context and enforce both the profile capability and document grant. Cross-tenant or invisible IDs return 404; known visible objects with insufficient operation rights use the existing 403 convention. Do not leak names, bodies, counts, referrers or revision existence through errors. Explicit deny, inactive credentials, owner ceilings, locked profiles and standalone visitor restrictions remain effective.

Revocation prevents future delivery; it cannot remove document text already sent to a model, exported conversation, browser or external MCP client. Existing message retention and deletion policies still apply. State that limitation in the public-sharing controls and security documentation.

## 6. Management REST API

Implement Watson feature registrars such as `PromptDocumentRoutes` and profile attachment routes backed by one shared service layer. Use existing authentication, pagination envelopes, validation field codes, request IDs and exception mapping. Do not invent a second error format. The following contracts are the implementation target; update OpenAPI metadata and Postman alongside them.

| Method and path under `/v1.0/api` | Behavior and body |
|---|---|
| `GET /prompt-documents` | Metadata list, filtered by visibility, `active`, name search and existing pagination/sort conventions. Never includes Markdown. |
| `POST /prompt-documents` | `{name,description,markdown,visibility,active}` creates metadata and revision 1. Returns 201 with IDs, size/hash, current revision and version. |
| `GET /prompt-documents/{documentId}` | Authorized metadata with revision/version and caller capabilities; no implicit full history/body. |
| `PUT /prompt-documents/{documentId}` | `{expectedVersion,name?,description?,markdown?,visibility?,active?}`. Omitted fields unchanged. Content/name/description changes create a revision snapshot; unchanged content must not manufacture revisions. Returns current metadata and new concurrency token. |
| `DELETE /prompt-documents/{documentId}` | Requires `expectedVersion` as a query parameter. 204 on deletion; 409 while attached. |
| `GET /prompt-documents/{documentId}/revisions` | Paginated revision metadata, no bodies; requires management Read. |
| `GET /prompt-documents/{documentId}/revisions/{revision}` | Explicit full Markdown read for managers, bounded by configured document maximum. JSON response includes revision metadata and `markdown`. |
| `GET /prompt-documents/{documentId}/referrers` | Only authorized profile IDs/names, mode, pin, enabled/public status; no private profile details. Return a boolean `hasOtherReferrers` where needed without revealing hidden counts. |
| `GET /agent-profiles/{profileId}/documents` | Envelope `{documentSetVersion,items:[...]}` containing attachment metadata and resolved revision information for profile managers; paginate items with the existing cursor envelope if needed. |
| `POST /agent-profiles/{profileId}/documents` | `{expectedDocumentSetVersion,documentId,deliveryMode,revisionPolicy,pinnedRevision?,displayOrder,enabled,allowPublicAccess}`; returns attachment/version and documentSetVersion, 409 on duplicate or stale set. |
| `PUT /agent-profiles/{profileId}/documents/{attachmentId}` | Requires expectedVersion and expectedDocumentSetVersion; returns updated row/version and documentSetVersion. Document identity is immutable; detach and add to change it. |
| `DELETE /agent-profiles/{profileId}/documents/{attachmentId}` | Requires expectedVersion and expectedDocumentSetVersion using query parameters; 204 on detach. Refresh the attachment list for its new documentSetVersion. No document deletion. |
| `PUT /agent-profiles/{profileId}/documents/order` | `{expectedDocumentSetVersion,attachmentIds:[...]}` returns the updated set version and atomically reorders the complete attachment set. Register static path before parameter routes. |
| `POST /agent-profiles/{profileId}/documents/preview` | `{audience:"Authenticated"\|"Public"}` returns compiled text, source map, resolved manifest, estimates, documentSetVersion and structured validation issues. Dry run only; no inference, visitor session, publication or grant mutation. |

Preview requires profile Read and document Read for every included body. Authenticated preview uses only the current principal; it cannot impersonate another user. Public preview additionally requires profile Update, uses the saved publishing credential and owner ceiling, applies the same opt-in capability clamp as real public execution, and intersects body visibility with the requesting manager's own Read permissions. Missing/revoked credential rights yield readiness issues without leaking inaccessible text. Do not accept arbitrary user/credential IDs in the preview request. Use typed responses and expose this method in all SDKs. The existing general compiled-instructions preview remains compatible and shares the same document composition engine.

Uploads use the same create/update JSON contracts: client-side file loading submits decoded `markdown`. No multipart parser or separate path-fetch route is required. Ensure request-byte limits cover JSON escaping expansion of the permitted UTF-8 document size while still bounding allocations. Return 413 for excessive HTTP bodies, 422 for valid requests exceeding configured document/composition limits, 400 for invalid enums/encoding/version syntax, 409 for version/name/pin conflicts, and the existing 401/403/404/429 statuses where applicable. Integration tests must assert actual emitted codes and field keys.

For example, create `LOG_MESSAGE_STRUCTURE.md` with description `Read before analyzing application logs; defines event fields and severity meanings.` and a synthetic Markdown schema. Attach it with `deliveryMode: "Reference"`, `revisionPolicy: "Latest"`, `enabled: true`, and `allowPublicAccess: false`. Verify metadata and compiled preview before running a chat. The REST reference must include complete request/response examples rather than only this abbreviated sequence.

## 7. Compilation and runtime delivery

Extend `src/Hugo.Core/Instructions/InstructionCompiler.cs` and its existing compiled response/source-map types. Preserve persona, top-level safety, resource policy and append instruction behavior. Add a named document segment after the existing agent working instructions and before resource-specific detail. Documents are operator-authored guidance, subordinate to Hugo's safety rules, authorization, approvals and the user's current request. Never execute document Markdown as Hugo template syntax: `{{...}}` is literal document text.

Process enabled, authorized attachments in ascending `DisplayOrder`, then stable attachment ID. Resolve Latest once per composition snapshot and use that immutable revision for hashing, rendering and estimates. Pinned content always resolves the specified immutable revision. Inline source-map entries identify document/attachment/revision/mode and byte/token estimates. Reference source-map entries identify only the manifest entry; the compiled prompt must not contain document-body snippets, headings or previews for Reference attachments.

The reference catalog should read like a small instruction index:

```text
Attached reference documents (content is not included here):
- LOG_MESSAGE_STRUCTURE.md
  ID: pdc_example; revision: 3
  Read before analyzing application logs; defines event fields and severity meanings.
  Use agent_read_document with this ID and revision. Continue reading all chunks
  needed for the task. Do not claim a document was read before a successful result.
```

An entry's authored description is the trigger for retrieval. The filename alone is insufficient. Include tool availability and retrieval instructions in the system-level catalog wrapper, not merely inside the document that the model has not yet read. For Hugo-hosted inference, a selected model endpoint without tool support must fail runtime validation/preview with an actionable configuration error when Reference attachments apply. External MCP callers use their own model and can still obtain the catalog/read tools without a configured Hugo inference endpoint. Never pretend a model can open references when its execution surface cannot call the tools.

For internal agent turns, extend `ServerAgentContextProvider` to reconcile attachment manifests and inline content before each model call. Identify the snapshot version and explicitly supersede previous document snapshots. Avoid adding a duplicate initial and refreshed body in the same model request. Treat compiler caching and per-call freshness as one contract: key caches by tenant, profile, caller authorization/capability, attachment-set version and selected revisions. Never share a compiled public/private document payload across principals.

Metadata/content revisions and attachment mode/order/pin/enable changes invalidate affected profile compositions, including all Latest referrers. Use the current event/cache invalidation patterns; if durable updates precede cache notification, version checks must prevent stale content after a missed event. Recheck rights and state at retrieval, after a tool pause and before the next model invocation. Deletion, revocation and publication rotation must not be bypassed by a cached manifest or continuation cursor.

A document may change between manifest creation and retrieval. A read specifying the advertised revision succeeds only while that revision remains authorized by the current attachment. A Latest attachment that has moved to a new revision returns `document_version_changed` with authorized current metadata; the agent relists and rereads. Do not silently mix chunks from different revisions. A pinned attachment is unaffected by a new current revision.

## 8. Profile-scoped retrieval tools

Add `AgentDocumentTools` and a shared document service patterned after `AgentInstructionTools`, without adding write tools. Register definitions and dispatch through the current internal loop and protocol catalogs. `LoopToolCatalog` currently excludes most meta tools, so explicitly allow these read tools alongside the existing agent instruction tools. A definition in the MCP catalog alone will not make it callable in dashboard chat.

| Tool | Inputs | Result |
|---|---|---|
| `agent_list_documents` | Optional bounded pagination cursor; no tenant/profile override | Enabled, readable attachments of the executing profile: ID, name, description, delivery mode, selected revision, size and hash. No document body. |
| `agent_read_document` | `documentId` or `name` (exactly one), optional advertised `revision`, optional continuation `cursor`, optional bounded `maxBytes` | ID/name, immutable revision/hash, Markdown chunk, `complete`, returned byte count, next cursor if incomplete. First call starts at the beginning. |

The default read returns the whole document if it fits; otherwise it returns a bounded chunk with an explicit continuation cursor. `complete: false` must never be described as the full document. Continuations are opaque, integrity-protected, bound to tenant/profile/document/revision and capability, and expire according to a configurable lifetime. They are not bearer grants; every continuation reauthorizes. Return only valid UTF-8 boundaries and preserve the reconstructed canonical content exactly. The SDK can aggregate management reads; model-facing tool calls must still respect turn and output limits.

Tool schemas explain that the files are already stored by Hugo, that the agent should not request server paths or secret access, and that an absent resource-context entry is unrelated to document availability. Read before applying detailed schema/procedure guidance. Follow every necessary continuation, report a limit/error if reading cannot complete, and never invent unread sections. A read failure must report a structured reason such as not found, inactive, version changed or budget exceeded using existing tool-error conventions.

Expose only attachments of the executing profile, even if the principal can manage other documents. Neither tool accepts arbitrary file paths, URLs, tenant IDs or profile IDs. Inline attachments may also be read through the tool if authorized, for consistent lookup and revision checks. No connector network activity, write approval or secret reveal occurs. Preserve existing tool-call correlation, cancellation, policy checks and auditing; no protocol-specific shortcut may bypass the shared service.

Test delivery through integrated dashboard chat, public/authenticated standalone chat, direct MCP tool use, `ask_agent_smith`, A2A and OpenAI-compatible agent execution. Raw model proxy endpoints without an agent profile remain unchanged and do not gain a tenant-wide document reader.

## 9. Limits and context efficiency

Limits are runtime settings with documented defaults and ranges, not scattered constants. Add them through Hugo's existing settings registration and SDK/settings documentation. Apply byte limits to canonical UTF-8 content and enforce the existing model token budget using the current estimator. Bytes and tokens are different measurements; show estimated tokens as estimates, not exact billing counts.

| Proposed setting | Default | Allowed range and behavior |
|---|---|---|
| `MaxPromptDocumentBytes` | 256 KiB | 1 KiB..1 MiB per revision; reject oversized imports before persistence |
| `MaxPromptDocumentsPerTenant` | 256 | 1..10000 library documents; enforce inside create transactions |
| `MaxPromptDocumentStorageBytesPerTenant` | 64 MiB | 1 MiB..1 GiB aggregate canonical revision bodies, including history; account transactionally before appending a revision |
| `MaxPromptDocumentAttachmentsPerProfile` | 32 | 1..128; includes disabled attachments so metadata storage stays bounded |
| `MaxInlinePromptDocumentBytes` | 64 KiB | 1 KiB..256 KiB aggregate; fail compilation before provider invocation if exceeded |
| `MaxPromptDocumentManifestBytes` | 16 KiB | 1 KiB..64 KiB; bound filenames/descriptions and fail visibly rather than silently dropping entries |
| `PromptDocumentReadBytes` | 16 KiB | 1 KiB..64 KiB default chunk size, clamped to remaining tool output budget |
| `MaxPromptDocumentReadBytes` | 64 KiB | 1 KiB..256 KiB maximum requested chunk; must be at least the default read size |
| `MaxPromptDocumentBytesPerTurn` | 256 KiB | 1 KiB..1 MiB total document tool output per turn; count repeated reads too |
| `PromptDocumentCursorLifetimeSeconds` | 600 | 30..3600; expiry does not bypass current authorization |

Keep revisions until their document is explicitly deleted; there is no silent history pruning, especially for pinned revisions. When the tenant quota is reached, reject a new revision with an actionable limit error. Authorized operators can detach/delete unused documents or increase the quota. Quota checks must serialize competing creates and revisions across both providers; a low count observed outside the transaction is not sufficient.

Reject inconsistent settings atomically. Check inline, manifest, model context, per-read, per-turn, tool-result and transport limits together. Do not truncate an inline instruction, return invalid JSON/UTF-8, silently switch modes, or keep retrying when the remaining tool budget is exhausted. If the complete document cannot fit in the current model context even with chunking, return an actionable limit result and allow the operator to reduce the document or change configured limits. Chunking is not unlimited model memory.

The model should reuse a successfully read, unchanged revision in the current conversation rather than repeatedly fetching it. Do not persist a hidden copy into resource context or agent instructions. Conversation compaction may remove a retrieved body; the next model call must retain the small manifest and may reread the authorized revision. Invalidation must distinguish a revision refresh from a permission revocation. User edits cannot erase content already retained in prior messages, so prefer a new conversation when verifying changed instructions.

## 10. Dashboard workflow

Keep the consolidated navigation. Add **Documents** under the existing **Instructions** hub, using `dashboard/src/config/navConfig.js`. Manage attachments inside the agent profile's existing **Instructions** tab in `ProfileDetailView.jsx`, beside the append editor and compiled preview. Do not add another top-level sidebar entry.

The library table shows name, description, revision, owner/visibility, active state, size, referrer indicator and row actions. Support server-side search/filter/sort/pagination. Create/Edit opens a wide existing-style modal with filename, description, Markdown editor, file chooser/drop zone, sanitized preview and visibility. Reuse `PromptEditor`, `MarkdownView`, `Modal`, `DataTable`, `CopyableId`, shared clipboard controls and API transport rather than bespoke controls. Confirm the actual component APIs before coding.

Uploading a file populates the editor and filename; the operator reviews the content before saving. Reject unsupported/binary/oversized input with a translated field error. Replacing content creates a new immutable revision. Show shared/public referrers and which attachments follow Latest. Editing old revisions creates a new revision through an explicit restore action; never mutate historical content. Display version conflicts with reload/compare and a preserved draft, not a silent overwrite.

An agent's attachment table shows name, concise description, Inline/Reference mode, Latest/Pinned selection, resolved revision, order, enabled status, public eligibility, size and estimated prompt contribution. The chooser allows attaching an existing readable document and creating one if the user has permission. Keyboard-accessible up/down actions must work without dragging. Detach clearly says it leaves the reusable document in the library. A document change affects referrers, while a mode/order/pin change affects only the current attachment.

The compiled preview distinguishes full inline content from metadata-only references and shows source attribution and limit violations. Provide authenticated-caller and public-publication previews through existing rights, with sensitive content hidden from unauthorized callers. Display the exact runtime read-tool name and a copyable example. Reference mode help must explain on-demand token usage and the need for a tool-capable model. Inline help must explain full system-prompt token usage.

Use loading, empty, permission-denied, unavailable, conflict and retry states. Prevent double submission and preserve unsaved drafts during errors. Modals trap focus, scroll their body, restore focus and close on Escape according to existing controls. IDs use a fixed-width font with the shared copy control that works on HTTP, HTTPS and non-localhost deployments. Treat Markdown links and raw HTML through the existing sanitizer; uploading or previewing a document must never run scripts or load unsafe embedded content.

Translate every interface string in all 12 catalogs, including tooltips, enum labels, errors, plural counts, upload help, public-disclosure warnings and accessibility text. Do not translate authored document names or content. Verify 1280, 768 and 390 widths in light/dark, representative English/Romance/CJK/Arabic layouts, long filenames, RTL, text expansion and keyboard/screen-reader names. New fields must have explanatory help and meaningful labels, not JSON-only editors.

## 11. SDK and protocol parity

Add typed document and attachment clients to all supported SDKs. The public methods must match the REST table, including list filters/pagination, version history, explicit content reads, referrers, optimistic concurrency, detach and atomic reorder. Follow each SDK's naming style rather than forcing C# method casing into Python or JavaScript. Serialize enums consistently with the server and preserve omitted-field semantics.

| SDK | Existing extension points | Required result |
|---|---|---|
| C# | `sdk/csharp/src/Hugo.Sdk/HugoClient.cs`, `Clients/`, `Models/`, `Requests/`, `Responses/`, `Enums/` | New typed clients/DTOs, cancellable async methods, documented nullability/defaults, structured conflict errors and all target-framework builds |
| JavaScript/TypeScript | `sdk/js/src/client.ts`, `clients/domain.ts`, `models.ts`, `index.ts` | Exported typed clients and models, abort support via existing transport, correct optional fields and pagination |
| Python | `sdk/python/hugo_sdk/client.py`, `api.py`, `models.py`, `__init__.py` | Idiomatic client methods, typing and existing error/cancellation conventions, correct exported contracts |

The C# and Python SDKs retain their established string wire-value conventions for delivery mode and revision policy; TypeScript uses literal unions. The server validates the exact documented enum values.

Add a helper or documented example in each SDK that reads a local UTF-8 file and submits it as a document. That helper must not imply the server can access the client's path. Each example creates a synthetic log-schema document, attaches it as Reference, lists it, switches to Inline, inspects compiled instructions, then detaches/deletes its own data. Use environment-provided test credentials and `127.0.0.1` for local examples, never hard-coded secrets.

SDK harnesses must test server behavior, not only DTO serialization: CAS conflict, mixed-case duplicate name, version pinning, public eligibility denial, deletion while attached, reorder, clone, content fidelity, cleanup and expected error types. Reuse the existing HTTP transport/authentication/signing/retry layers. Do not retry non-idempotent creates or stale mutations automatically. Retrying a read must preserve its selected revision.

## 12. Telemetry, audit and content protection

Document bodies are intentionally sent to authorized models and content readers. They must not also leak into audit descriptions, metric labels, exception text, request-history request bodies, exported traces or application logs. Add explicit content redaction for create/update/read routes and tool previews using the current policies; do not rely on a secret scrubber alone to hide arbitrary confidential Markdown. The authorized conversation tool result necessarily contains retrieved content under existing conversation access/retention rules.

Use the existing secret/content guard on writes and before model delivery. Reject known secret-bearing writes with a redacted validation message rather than silently changing saved content and invalidating its hash. Filenames, descriptions and cursor input need validation too. A scrubber is defense in depth, not a reason to store credentials in a document. Escape delimiter-looking names/descriptions in manifest rendering and preserve Markdown as data rather than template directives.

Audit create/update/restore/deactivate/delete, attach/detach/order/mode/pin/public changes and authorization denials using IDs, actor, tenant and request/trace correlation. Record a document-read event or existing tool audit entry containing revision, outcome and byte count, never body content. Do not duplicate existing request/tool audit records unnecessarily. Public traffic must remain rate-limited using the existing visitor controls.

Extend `HugoMetricCatalog`, `HugoMetrics` and `HugoTracing` with fixed-cardinality operation/mode/outcome labels. Measure composition duration, reference-read duration/bytes, cache outcomes, rejected limits and version conflicts. Never label by filename, document/profile/user ID, Markdown, cursor or error text. Add telemetry tests, update `docs/TELEMETRY.md` and mirror any changed Grafana panels using existing conventions.

## 13. Test cases and evidence

Tests must use synthetic document bodies and locally generated identities, clean up created data, and run against real SQLite and PostgreSQL behavior. A short Markdown fixture must include Unicode, CRLF, braces and a schema example. A large fixture exercises exact byte boundaries and continuations. Never reuse production resource names, plaintext passwords, credentials or copied private log messages in fixtures or documentation.

| Case group | Required cases and expected results |
|---|---|
| Migration/persistence | Fresh and version-11 upgrade; repeat startup; additive migration rollback/failure; restart persistence; tenant/name uniqueness; invalid pin; foreign relations; ordered list; profile cascade and detached document deletion; concurrent SQLite writes |
| Canonicalization | UTF-8 and BOM, CRLF to LF, Unicode NFC names, invalid bytes/NUL/binary rejection, control/path traversal rejection, meaningful Markdown whitespace preserved, size/hash fidelity, duplicate normalization collisions |
| Concurrency/history | Missing/stale versions; simultaneous revision writes yield one winner; metadata/content transaction; no-op update; immutable historical revisions; atomic reorder exact-membership validation; deleted/stale pin; clone preserves pin/mode but resets public eligibility |
| Authorization | Cross-tenant IDs/names/cursors; private owner visibility; create/read/update/delete matrix; profile Update plus document Read; explicit deny and credential ceiling; locked profile; disabled principal/document/profile/attachment; library rights do not allow reading an unattached document through tools |
| Public/authenticated chat | Public opt-in required; exact document grants; no management/history/referrer access; publication rotation; no automatic credential grant mutation; shared credential unaffected; Update-only public-Latest edit, repin, policy change or re-enable denied; public preview cannot impersonate or exceed manager Read; detach/revoke between chunks; no private manifest or inline content; public editor disclosure; authenticated caller-specific preview and missing Inline Read fail before inference |
| Compilation | Empty attachment list unchanged; stable order; mixed Inline/Reference; no Reference body in prompt; whole Inline body present; source maps/costs; literal template braces; duplicate-delimiter names rejected/escaped; Latest invalidation across agents; pinned revision stability |
| Retrieval | List metadata only; ID/name selection; forbidden profile/tenant/path/URL inputs; full small-file result; exact reconstruction of chunked Unicode document; cursor tamper/expiry/reuse on other profile; version-changed result; no unauthorized historical revision read |
| Limits/freshness | One byte below/at/above document, inline, manifest, read, per-turn and context budgets; HTTP JSON expansion; no partial Inline prompt/provider call; repeated reads counted; cancellation during database/read/provider preparation; revocation after approval/tool pause; compaction allows reread |
| Model/tool integration | Both tools actually dispatch inside agent loop and MCP; dashboard/standalone/A2A/OpenAI/ask-agent routes share policy; non-tool model validation; missing document reports actual error without inventing body; no requirement to retrieve resource context first |
| Content safety | Scripted Markdown, dangerous links/images, prompt-looking document content, template injection, known secret input, raw error/body/trace capture, cursor/referrer leakage, private data in public cache; safety/approval/resource policy cannot be overridden by a document |
| Dashboard | CRUD/import/version history/restore; attach/edit modes/pin/reorder/detach; chooser duplicates; server errors/CAS; restricted controls; referrer/deletion warning; copy on insecure HTTP; responsive themes; accessibility/focus/keyboard/RTL and localized labels |
| SDKs/docs | Typed contract parity, enum values, nullable/omitted fields, end-to-end harnesses, example cleanup, valid Postman/OpenAPI/tool schemas, accurate REST/MCP errors, no stale route names or phantom endpoints |

Register shared Touchstone descriptors under `src/Test.Shared` and the existing suite aggregator. Provider-specific persistence cases run under both providers; provider-independent compiler/tool cases need not be duplicated unnecessarily. Keep xUnit/NUnit wrappers compatible with the existing shared suite architecture. Add component tests for meaningful state transitions and Playwright journeys for real document management and agent invocation.

A deterministic scripted model adapter should assert the agent receives only the Reference manifest before reading, can call the tool, then receives the body, and sees Inline content without a read. That proves orchestration, not the judgment of every model. Run a live smoke test with the locally available supported harness/model configuration using synthetic data. Paid-provider tests are opt-in with an explicit call/token budget and timeout. Record whether the model read `LOG_MESSAGE_STRUCTURE.md` before applying its fields. Do not hard-code a model name or private inference-host address into committed examples. Document models that fail to consult references rather than claiming prompting guarantees model compliance.

Simulated journeys cover an administrator configuring the library, an operator asking about a schema, a developer using each SDK, and a restricted/public user attempting an unauthorized read. Repeat selected journeys after restart, a content revision, a pin change and revocation. Store screenshots/traces under ignored `user-testing/evidence/` following the requirements, with a sanitized findings summary. A critical unresolved leak, migration failure or nonfunctional protocol blocks completion.

Benchmark a fixed 1/16/32-attachment fixture and small/near-limit bodies. Report hardware, database provider, cold/warm cache, byte and estimated token totals, p50/p95 composition/read latency and sample count. Compare with the same profile without documents. Verify Reference prompt size depends on manifest metadata rather than body length, and repeated calls do not accumulate duplicate inline bodies or unbounded reads. Use a documented local benchmark baseline to set regression thresholds before release; do not invent universal latency promises.

## 14. Documentation and rollout

Create `docs/AGENT_DOCUMENTS.md` as the operator guide and link it from `README.md`, `docs/PROMPTS.md`, `docs/RESOURCE_CONTEXT.md`, `docs/ARCHITECTURE.md` and the dashboard help. Explain managed uploads versus live external sources, Inline versus Reference token costs, public disclosure, permissions, revisions, pins, source updates, limits and troubleshooting. Include a complete generic log-schema example and the expected tool call. State that a reference does not guarantee model consultation and content becomes model-visible when retrieved.

Update `REST_API.md` with every route, parameters, bodies, errors, permissions and version tokens; `MCP_API.md` and `docs/MCP_CONNECTOR_TOOLS.md` with tool schemas, result shapes, chunk continuation and context scoping; `assets/postman/` with documented requests and variables; OpenAPI schema generation with the real DTOs. Update all SDK READMEs/client inventories and examples. Update configuration, security, telemetry, standalone chat, upgrade and troubleshooting guides where their behavior changes, plus `CHANGELOG.md` and the feature progress entries. Keep `DOCKERHUB_README.md` accurate if its feature list changes.

Deployment adds no service, remote filesystem dependency, vector database or new inbound port. Existing database volumes contain document/revision data. Verify migration and backup/restore with Compose and the current Kubernetes/minikube image paths. Add the synthetic document example to existing factory/demo setup without altering real users' agents or turning public sharing on by default.

Before publishing, take the existing database backup and test the upgrade against a restored copy. The migration is additive; an old binary must not attempt to downgrade schema or discard documents. Document the supported rollback behavior of the current migration runner, including whether an old server refuses a newer schema. If binary rollback is unsupported, rollback means restoring the pre-upgrade backup with the old image. Never promise compatibility without testing it.

Build/push images only under existing authorized tags and verify the remote AMD64/ARM64 manifests and matching version/latest tags. Record source commit and digests after publication. Never bump product/SDK/package versions as a side effect. Fresh profiles start with no documents; existing agents keep their current prompts until attachments are explicitly created. Publishing this plan alone requires no image build or database operation.

## 15. Actionable implementation checklist

The acceptance statements below are work boundaries, not suggestions. Finish prerequisites before merging dependent behavior. Every task should point to its validation evidence in this file; a green unit suite alone does not satisfy migration, authorization or live-protocol acceptance.

### Contracts and persistence

- [x] APC-01 Add domain enums, entities, request/response contracts and configurable limits from sections 4/9. **Accept:** serialized names, defaults, nullability and validation rules agree across types; document the final authorization/public decisions before dependent work. | codex 2026-10-18 Shared contracts and 18 SQLite/PostgreSQL persistence cases verified; final integration remains tracked below.
- [x] APC-02 Add table registrations, tenant-safe indexes/relations and the next additive migration. **Accept:** fresh and upgrade initialization pass on SQLite/PostgreSQL without modifying historical migrations. | codex 2026-10-18 Shared contracts and 18 SQLite/PostgreSQL persistence cases verified; final integration remains tracked below.
- [x] APC-03 Implement typed ADO document/revision database methods. **Accept:** canonical body fidelity, name uniqueness, immutable revision reads and tenant filters pass against both providers. | codex 2026-10-18 Shared contracts and 18 SQLite/PostgreSQL persistence cases verified; final integration remains tracked below.
- [x] APC-04 Implement attachment database methods and atomic reorder/set versions. **Accept:** duplicate, invalid pin, concurrent reorder and cross-tenant cases fail correctly; stable ordering and documentSetVersion persist across restart. | codex 2026-10-18 Shared contracts and 18 SQLite/PostgreSQL persistence cases verified; final integration remains tracked below.
- [x] APC-05 Implement CAS document/revision transactions, no-op detection, deactivate/delete/referrer rules. **Accept:** simultaneous writes have one winner; in-use delete is 409; no-op tokens are stable; failures leave no orphaned revisions. | codex 2026-10-18 SQLite/PostgreSQL persistence, HTTP contract and authorization suites pass.
- [x] APC-06 Extend profile delete/clone and backup fixtures. **Accept:** clone shares documents with new attachment IDs and private defaults; deleting profiles preserves reusable documents; restored database reproduces content/pins/order. | codex 2026-10-18 SQLite/PostgreSQL persistence, HTTP contract and authorization suites pass.

### Authorization and management API

- [x] APC-07 Add PromptDocument resource type, permission labels and deliberate role defaults. **Accept:** permission map tests cover CRUD, owner/tenant visibility, explicit deny and credential scopes without broad bypass grants. | codex 2026-10-18 SQLite/PostgreSQL persistence, HTTP contract and authorization suites pass.
- [x] APC-08 Implement shared management/execution services with separate authorized projections. **Accept:** execution cannot enumerate unattached library documents; direct API and tool reads enforce current permissions consistently. | codex 2026-10-18 SQLite/PostgreSQL persistence, HTTP contract and authorization suites pass.
- [x] APC-09 Implement document CRUD, revision and referrer Watson routes and operation scopes. **Accept:** all section 6 contracts emit documented validation/status/version behavior; hidden referrers and bodies stay hidden. | codex 2026-10-18 SQLite/PostgreSQL persistence, HTTP contract and authorization suites pass.
- [x] APC-10 Implement attachment CRUD/reorder routes and operation scopes. **Accept:** profile Update/document Read and public Admin requirements (including repin/policy/re-enable) enforced; attachment changes do not overwrite unrelated profile properties. | codex 2026-10-18 SQLite/PostgreSQL persistence, HTTP contract and authorization suites pass.
- [x] APC-11 Integrate public publication grants, revision revocation and public preview. **Accept:** only opted-in document IDs/revisions with existing grants are delivered; detach removes capability access without changing shared credential grants; Update-only public-Latest edits are denied; old visitor capabilities cannot bypass revocation. | codex 2026-10-18 SQLite/PostgreSQL persistence, HTTP contract and authorization suites pass.
- [x] APC-12 Add audit, request-history redaction, telemetry and configurable HTTP/body/rate limits. **Accept:** correlated events are useful without content leakage or unbounded label values. | codex 2026-10-18 SQLite/PostgreSQL persistence, HTTP contract and authorization suites pass.

### Compiler, tools and protocols

- [x] APC-13 Extend compiler composition, source maps, cost preview and deterministic order. **Accept:** Inline includes complete selected body; Reference includes metadata only; over-budget/unauthorized Inline fails before inference. | codex 2026-10-18 Sixteen delivery cases pass on both providers, including hosted/protocol dispatch and scoped UTF-8 retrieval.
- [x] APC-14 Integrate current-turn freshness and versioned cache invalidation. **Accept:** shared Latest edits and detach/revoke affect the next call; pins remain stable; no duplicate initial/refreshed payload or cross-principal cache reuse. | codex 2026-10-18 Sixteen delivery cases pass on both providers, including hosted/protocol dispatch and scoped UTF-8 retrieval.
- [x] APC-15 Implement profile-scoped list/read tools with UTF-8 chunking and authorized cursors. **Accept:** small full read, large exact reconstruction, revocation/expiry/tamper/version-change and per-turn budget cases pass. | codex 2026-10-18 Sixteen delivery cases pass on both providers, including hosted/protocol dispatch and scoped UTF-8 retrieval.
- [x] APC-16 Register actual tool definitions, loop allowlist and protocol dispatch. **Accept:** both tools execute in dashboard, standalone, MCP, ask-agent, A2A and OpenAI-compatible agent paths; raw proxy behavior is unchanged. | codex 2026-10-18 Sixteen delivery cases pass on both providers, including hosted/protocol dispatch and scoped UTF-8 retrieval.
- [x] APC-17 Add model-facing guidance for reference consultation, continuation and actual-error reporting. **Accept:** synthetic scripted-model test observes manifest first, body only after read, no guessed file access or automatic persistence into memory. | codex 2026-10-18 Sixteen delivery cases pass on both providers, including hosted/protocol dispatch and scoped UTF-8 retrieval.
- [x] APC-18 Verify malformed-document safety, cancellation and existing chat streaming/tool budgets. **Accept:** documents cannot execute templates or bypass approvals; streaming/feedback/deferred approvals regressions remain green. | codex 2026-10-18 Sixteen delivery cases pass on both providers, including hosted/protocol dispatch and scoped UTF-8 retrieval.

### Dashboard and localization

- [x] APC-19 Add Documents library tab and shared API methods within existing navigation. **Accept:** server-backed pagination/filtering, restricted access and URL-addressable tab behavior work without a new sidebar hub. | codex 2026-10-18 362 unit tests, 101 browser tests, all 12 catalogs and actual dashboard journey; see user-testing/APC_UI_2026-10-18.md.
- [x] APC-20 Build create/edit/upload/preview/history/restore/referrer workflows. **Accept:** UTF-8 imports, shared/public warnings, version conflicts and in-use deletion are actionable; drafts survive errors. | codex 2026-10-18 362 unit tests, 101 browser tests, all 12 catalogs and actual dashboard journey; see user-testing/APC_UI_2026-10-18.md.
- [x] APC-21 Add attachment management inside agent Instructions. **Accept:** mode, order, enabled, Latest/Pinned, public eligibility, chooser, detach and clone behavior work end to end and are keyboard accessible. | codex 2026-10-18 362 unit tests, 101 browser tests, all 12 catalogs and actual dashboard journey; see user-testing/APC_UI_2026-10-18.md.
- [x] APC-22 Extend compiled/public previews, limits and copyable source details. **Accept:** previews accurately match effective caller-specific delivery and never expose unauthorized bodies or hidden references. | codex 2026-10-18 362 unit tests, 101 browser tests, all 12 catalogs and actual dashboard journey; see user-testing/APC_UI_2026-10-18.md.
- [x] APC-23 Complete all 12 translations and responsive/accessibility verification. **Accept:** localization/lint checks, light/dark 1280/768/390 evidence, representative CJK/RTL/text-expansion and focus/copy behavior pass. | codex 2026-10-18 362 unit tests, 101 browser tests, all 12 catalogs and actual dashboard journey; see user-testing/APC_UI_2026-10-18.md.

### SDKs and documentation

- [x] APC-24 Implement C# clients/DTOs/wire mode values and live harness scenario. **Accept:** all targets build without warnings; CRUD, CAS, attach/pin/reorder/referrers/cleanup run against the server. | codex 2026-10-18 Three SDK live harnesses and executable examples pass; REST/MCP/OpenAPI/Postman/operator contracts updated.
- [x] APC-25 Implement TypeScript exports/clients/types and harness scenario. **Accept:** type build and transport/contract/live scenarios pass with correct abort, omission and conflict behavior. | codex 2026-10-18 Three SDK live harnesses and executable examples pass; REST/MCP/OpenAPI/Postman/operator contracts updated.
- [x] APC-26 Implement Python clients/types/exports and harness scenario. **Accept:** existing Python quality checks and server round trips pass with clean test data removal. | codex 2026-10-18 Three SDK live harnesses and executable examples pass; REST/MCP/OpenAPI/Postman/operator contracts updated.
- [x] APC-27 Update OpenAPI, REST/MCP references and Postman. **Accept:** every new route/tool, error, permission, pagination and version contract is documented with valid executable examples. | codex 2026-10-18 Three SDK live harnesses and executable examples pass; REST/MCP/OpenAPI/Postman/operator contracts updated.
- [x] APC-28 Write operator/security/configuration/upgrade/troubleshooting documentation. **Accept:** a reader can upload an external Markdown file, choose either mode, configure rights and troubleshoot retrieval without source-code access. | codex 2026-10-18 Three SDK live harnesses and executable examples pass; REST/MCP/OpenAPI/Postman/operator contracts updated.
- [x] APC-29 Update README/SDK examples/factory fixture/changelog and cross-links. **Accept:** generic runnable log-schema examples use no private endpoints, credentials, customer names or version bumps. | codex 2026-10-18 Three SDK live harnesses and executable examples pass; REST/MCP/OpenAPI/Postman/operator contracts updated.

### Verification and release

- [x] APC-30 Register/run the provider test matrix and migration/rollback failure cases. **Accept:** SQLite/PostgreSQL results cover section 13; real failures are resolved rather than skipped. | codex 2026-10-18 Provider integration and live SDK/UI/model evidence recorded; see final verification below.
- [x] APC-31 Run protocol and standalone/public authorization integration tests. **Accept:** all entry points preserve scope isolation, locked-profile rights, visitor revocation and version continuity between chunks. | codex 2026-10-18 Provider integration and live SDK/UI/model evidence recorded; see final verification below.
- [x] APC-32 Run dashboard component/browser/visual tests and SDK harnesses. **Accept:** meaningful workflows, existing streaming/feedback and new translations pass; attach sanitized evidence/results. | codex 2026-10-18 Provider integration and live SDK/UI/model evidence recorded; see final verification below.
- [x] APC-33 Run isolated simulated user journeys and live model/harness smoke tests. **Accept:** record actual retrieval behavior and limitations; resolve critical findings; repeat affected cases after fixes. | codex 2026-10-18 Provider integration and live SDK/UI/model evidence recorded; see final verification below.
- [x] APC-34 Measure compilation/retrieval/context costs with documented fixtures. **Accept:** cold/warm results and a reviewable regression threshold are recorded; no unbounded prompt/body accumulation. | codex 2026-10-18 Ten scenarios with 30 sample pairs each; benchmarks/RESULTS.md records costs and limitations.
- [x] APC-35 Complete zero-warning solution/SDK/frontend builds, docs/style/package/version checks and deployment upgrade verification. **Accept:** validated changes and remaining noncritical limitations are recorded without unsupported success claims. | codex 2026-10-18 Zero-warning builds and checks pass; complete SQLite/PostgreSQL regression: 2116 passed, 309 expected testbed skips, zero failures. Published-image startup and live document workflow pass.
- [x] APC-36 Commit/push implementation and publish only as authorized; update this plan and archived roadmap with final evidence. **Accept:** source commit, migration version, real test totals, deployment result and verified image digests recorded; operator can reproduce Inline and Reference acceptance examples. | codex 2026-10-18 Implementation e61da60 pushed to main; both images published and registry-verified for AMD64/ARM64 with unchanged v0.1.0/latest tags. Digests below.

## 16. Verification commands and handoff

Use the repository's existing runners and confirm newly assigned suite names when registering them. The examples below show the intended sequence, not a claim that proposed suites already exist. PostgreSQL fixtures and SDK live server credentials must be prepared through the existing test harness. Do not run a harness against a production database or skip its prerequisites and report success.

```bash
dotnet build src/Hugo.sln
dotnet run --project src/Test.Automated -f net10.0 -- --providers sqlite,postgresql --suites agent-documents-persistence,agent-document-route-contract,agent-document-delivery,standalone-background
dotnet test src/Test.Xunit -f net10.0
dotnet test src/Test.Nunit -f net10.0
npm --prefix dashboard run lint
npm --prefix dashboard run i18n:check
npm --prefix dashboard test
npm --prefix dashboard run build
```

Run Playwright from `dashboard/` using its existing configuration and documented command, then execute each SDK's documented build/test/live harness. Use the repository package-pin and version-mirror checks discovered during implementation; do not invent command names. Finish with `git diff --check` and `scripts/check-em-dash.sh`. Read all generated API and operator examples as a human before committing.

The final handoff must distinguish implementation tests from live model behavior and built images from published images. Include any custom prompt overrides that prevent the default reference guidance from reaching a model. The operator acceptance demonstration ends with a new chat reading `LOG_MESSAGE_STRUCTURE.md` through the tool in Reference mode, another new chat receiving it inline, and a restricted caller failing to obtain an unattached or private document.

## 15. Implementation verification, 2026-10-18

Migration 12 is additive. Fresh installs, version 11 upgrades, restart preservation,
failed-migration rollback, compare-and-swap races and tenant quotas were exercised
on SQLite and PostgreSQL. A copied SQLite migration-12 database was also opened
by the previous published server image, authenticated successfully, then stopped;
the schema version and synthetic document body/hash remained intact. This is a
limited smoke check, not a promise that every older binary can use new features.
For production rollback retain the pre-upgrade database backup and master key.

The dashboard passes 362 component/unit tests and 101 browser tests. All 12 locale
catalogs contain the same 3379 keys. Live C#, JavaScript and synchronous/asynchronous
Python document harnesses pass, as do their executable examples. Postman executed
23 requests and 26 assertions without failures. The solution and both C# SDK target
frameworks build without warnings; JavaScript, Python and dashboard checks pass.

Actual model evidence, including one low-output-budget Reference attempt that did
not retrieve the document, is recorded in [the user verification report](user-testing/APC_UI_2026-10-18.md).
A subsequent bounded non-reasoning model called `agent_read_document` and answered
with the distinctive fact stored only in its attached Reference document. Inline
also answered correctly. These observations demonstrate the feature; they do not
guarantee every model will follow guidance or avoid redundant reads.

The background-chat change is separately complete: disconnecting a dashboard or
standalone viewer detaches the stream; the accepted turn continues. Returning
polls persisted messages while the server reports `turnInProgress`; explicit Stop
still cancels. Process restart does not durably resume an interrupted model turn.

Final aggregate regression: **2425 total, 2116 passed, zero failures, 309 expected
external-testbed skips**, on SQLite and PostgreSQL. Targeted version/migration
checks pass 48/48; golden/plugin checks pass 6/6. JavaScript and Python live SDK
checks also pass after restarting the final server build.

Implementation source `e61da60` is pushed to `main`. Both published tags
`v0.1.0` and `latest` resolve to the same per-image manifest and contain AMD64
and ARM64 builds:

| Image | Verified manifest digest |
|---|---|
| `jchristn77/hugo-server` | `sha256:a9b80c1c2ff88f861229a34db29834f237172a0b6ac5ecd5e3a02b93e6a604d5` |
| `jchristn77/hugo-dashboard` | `sha256:86b956736eab278c01479b0484f6976821698a159ecc9eabd3b6e0aecd2d2663` |

An isolated stack using these published images passed login, dashboard health
and HTML, SDK file import, Reference preview without body, Inline preview with
body, and record cleanup. No production deployment was modified. Pull and
recreate the server and dashboard containers to apply the release. Migration 12
runs automatically; back up the database and master key before upgrading.
