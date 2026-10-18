# Attached document retrieval regression

Date: 2026-10-18. Implementation: `92b6276`. All fixtures are synthetic.

The reported call supplied matching document ID and filename, revision 1, an
empty cursor and maxBytes 65536. Previously the redundant selector failed before
reading. Retrying with only the ID then failed because a document-specific guard
called the general log scrubber. That scrubber detects patterns in documentation,
including password or API-key field assignments; the error did not establish that
the document was classified as secret or required a separate clearance.

The guard is removed from Reference and Inline delivery. Fresh hosted context
also preserves authorized document text separately from the redaction used for
ordinary saved context. Matching ID/name selectors are accepted; conflicting
selectors still fail. Missing selectors, unreadable/unattached documents, invalid
revisions, tampered cursors and output/context budget violations remain errors.

`dotnet build src/Hugo.sln --no-restore` passes for both target frameworks with
zero warnings/errors. Fifty shared cases pass on SQLite and PostgreSQL using:

```sh
dotnet run --no-build --project src/Test.Automated -f net10.0 -- \
  --providers sqlite,postgresql \
  --suites agent-document-delivery,agent-document-routes,resource-context,integration-snapshot
```

The delivery fixture includes literal credential-field examples that the old
scrubber modifies. Tests verify exact Reference content with matching ID/name,
empty cursor and revision, name-only reads, conflicting selectors, missing
selectors and exact Inline composition. A scripted model over actual HTTP
receives the Reference catalog first, makes the redundant-selector call, receives
the complete literal result in model history, and answers successfully through
hosted chat, OpenAI-compatible and A2A paths. A fresh Inline conversation receives
the same literal text and answers without retrieval. This verifies transport and
runtime behavior; it is not a claim that every model will follow guidance.

Resource-context redaction, authorization boundaries, public grants, scope locks,
version freshness, Unicode chunk integrity, budgets and compiler snapshots remain
covered. No database migration, document re-upload or attachment recreation is
necessary. Restart the server with the updated image, then retry in a fresh chat
to avoid carrying forward the previous failed-tool conclusions.

## Published-image acceptance

Published server `v0.1.0` and `latest` are registry-verified for AMD64/ARM64 at
`sha256:9044dc9f32d95cba37f7f2ebd279cb49767625a68fab352f64c3c198614a1a7c`.
An isolated stack started from the published image authenticated successfully,
created/attached a synthetic Reference document with scrubber-triggering field
examples, and read its exact content over MCP with matching ID/name, revision 1,
empty cursor and maxBytes 65536. A conflicting filename still failed. The stack
also passed document import, Reference/Inline previews, dashboard health and
cleanup. Implementation `92b6276` is pushed to main. Pull and recreate the server
container to apply; no data migration or re-upload is needed.
