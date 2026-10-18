# Agent prompt documents

Attach named Markdown documents to an agent when detailed instructions, schemas or procedures should be reusable. Each attachment chooses **Inline** or **Reference** delivery. Documents complement the agent's short working instructions and resource context; they do not replace them.

## Create and attach a document

Open **Instructions > Documents**, choose **Create**, and give the document a filename and a short description explaining when the agent should read it. Enter Markdown or upload a UTF-8 `.md` or `.txt` file, review the preview, select visibility, and save. For example, create `LOG_MESSAGE_STRUCTURE.md` with description `Read before interpreting application logs; defines fields and severity meanings.`

Open **Agents**, select the agent, and use its **Instructions** tab to attach that document. Select the delivery mode, Latest or Pinned revision, order and enabled state. The compiled preview shows what the current caller can deliver to the model. Attachments have their own versions; saving them does not rewrite the agent's other instructions.

An uploaded file becomes a versioned copy in Hugo's database. Its name is not a server filesystem path. Hugo does not reopen the source file, fetch a URL or synchronize a mounted directory automatically. To change an externally authored file, upload its new contents as a new revision. File bytes must be valid UTF-8 text; a leading BOM is removed and line endings become LF. Meaningful Markdown whitespace is preserved.

## Choose a delivery mode

| Mode | What the system prompt contains | When the body reaches the model |
|---|---|---|
| Inline | The complete selected document, with source attribution | Every model call using that compiled document snapshot |
| Reference | Filename, description, document ID, selected revision and retrieval instructions | After the model calls `agent_read_document` |

Use Inline for short rules that must always be present. Reference is useful for longer field definitions, examples and procedures. The description is the retrieval trigger: `Read before interpreting application logs` is more useful than `Log information`. A filename alone cannot make a model read a file.

Reference mode saves initial system-prompt tokens. Reading the document consumes tool-result tokens, and its body may remain in subsequent conversation history. It is not free context or a guarantee that every model will consult it. Hugo-hosted execution requires a tool-capable model for references. External MCP callers use their own model and receive the same profile-scoped catalog and read tools.

## Read a referenced document

The model can discover available attachments with `agent_list_documents`. It can read a named document with:

```json
{
  "name": "LOG_MESSAGE_STRUCTURE.md"
}
```

`agent_read_document` returns the selected immutable revision, content hash, Markdown and `complete`. A small document arrives in one response. If `complete` is false, call it again with only the returned `cursor` and an optional `maxBytes` value. Continue until all detail needed for the task has been retrieved. The cursor does not grant access: Hugo checks the executing profile, current attachment, identity and publication on every read.

Tools can read only enabled, authorized documents attached to the current profile. They cannot read arbitrary library entries, local paths, URLs or documents belonging to another profile. They cannot create or modify documents. Missing resource context has no bearing on document availability, and secret-reading permissions are not needed.

## Revisions, sharing and concurrency

A document can be attached to several agents. **Latest** follows the current revision. **Pinned** keeps an explicitly selected immutable revision. Switching a shared document to a new current revision affects Latest attachments, while pins remain stable. The editor shows referrers and warns when publicly eligible attachments follow Latest.

Updates require the latest `version` as `expectedVersion`. Attachment changes additionally require the current `documentSetVersion`. A stale save returns 409; reload and compare before reconciling your draft. A no-op save preserves its existing versions. Restoring a revision copies its reviewed content into a new current revision rather than modifying history.

Names are unique within a tenant and compared using normalized, case-insensitive spelling. Effective filenames must also be unique within an agent, including pinned historical names. Renames, attachment changes and repins that would create a collision return a conflict. Filenames cannot contain path separators, control characters or traversal components.

Detach an attachment to stop using a document on one agent. The library document remains. Deleting an attached document returns 409 until it has been detached everywhere. Deactivating a document stops future delivery. Deleting an agent removes its attachments without deleting shared documents; duplicating an agent copies modes, order and pins but does not copy public-sharing eligibility.

## Permissions and public chat

Library operations require PromptDocument Create, Read, Update or Delete as appropriate, plus owner/tenant visibility. Managing attachments requires agent profile Update and document Read. Authenticated execution requires profile Execute and document Read. An unreadable Reference attachment is not advertised. An unreadable enabled Inline attachment blocks composition rather than silently removing instructions.

Public delivery is opt-in on each attachment. It uses the saved publication credential, which must already have explicit document Read grants within its owner's permissions. An authorized security manager configures grants through Hugo's existing controls. Document editing never creates or removes credential scopes automatically.

Enabling public delivery, repinning or changing the policy of a publicly eligible attachment, and re-enabling it require document Admin. Editing a document followed by a publicly eligible Latest attachment also requires Admin in addition to Update. Public preview applies the publication credential's rights and the requesting manager's own document Read permissions. It never impersonates another user or changes grants.

Treat publicly eligible documents as disclosable to visitors. A document can guide an agent without exposing its system prompt directly, but the model may quote retrieved information. Never place credentials or secrets in these files. Revocation stops future reads and delivery; it cannot retract content already sent to a model or retained in an authorized conversation.

## Limits and context use

The `PromptDocuments` settings section controls canonical UTF-8 document bytes, tenant storage including revisions, attachment count, aggregate inline/manifest sizes, read chunk size, per-turn output and cursor lifetime. Defaults include 256 KiB per document, 32 attachments, a 64 KiB aggregate inline budget and 16 KiB read chunks. Limits also respect the selected model's context budget.

Hugo rejects oversized imports or composition rather than silently truncating instructions or changing delivery modes. Reference reads split only at valid UTF-8 boundaries and report continuation explicitly. Reading in chunks does not let a model exceed its context window. Repeated reads count toward the turn budget. Once a turn's document budget is exhausted, use the already read portions or start a smaller task.

## Test the two modes

Use a synthetic schema with a distinctive field, such as `event_code`, and ask in a new conversation:

```text
Use LOG_MESSAGE_STRUCTURE.md to explain which fields I need when recording an application error.
Read the document before answering and identify the revision you used.
```

In Reference mode, inspect the tool activity: a successful document read should precede applying its details. In Inline mode, inspect compiled instructions to verify that the complete selected content is present; no read is required. A pinned attachment should keep its old content after the shared document is revised. Test restricted/public callers separately from an administrator.

An unavailable document should produce a real tool or authorization error, not an invented description of its contents. Start a new conversation when testing changed guidance so earlier retrieved revisions do not influence the result.

Document saves accept administrator-authored text literally, including credential-shaped examples. Save validation does not reject or rewrite content based on secret patterns. Existing access controls, content limits and request-history redaction still apply.
