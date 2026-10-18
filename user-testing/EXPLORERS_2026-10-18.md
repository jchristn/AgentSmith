# Developer explorer follow-up verification

Date: 2026-10-18. Implementation source: `3af1e56`. Isolated Playwright
browser sessions used synthetic administrator/card/tool-response fixtures at
`http://127.0.0.1:4173`. No production agent or credential was used.

MCP initialization instructions start collapsed and can be expanded. Calling a
tool whose response includes both text compatibility JSON and structuredContent
shows the structured object once in the primary result. The raw response remains
available in a separate collapsed disclosure; non-structured text tools still work.

A2A card verification covers provider, formats, capabilities, interfaces,
authentication, skill descriptions/tags, example prompts and collapsed raw JSON.
Selecting a skill or example prepares a draft without sending a request. Invalid
JSON parameters produce a local error without a network call. A valid edited
request preserves custom metadata and executes the selected SendMessage method.
An advertised lack of streaming disables streaming selection.

Desktop and 390-pixel checks capture the card, assert no document horizontal
overflow, and wait for navigation transitions before capture. Local ignored visual
evidence is under `user-testing/evidence/explorers/`. The browser tests are
reproducible with `npx playwright test tests/e2e/explorers.spec.js tests/e2e/flows.spec.js`
from `dashboard/`: 17 cases passed. Both dedicated explorer cases pass again after
adding the phone-width assertions. Dashboard lint, all 362 unit tests, all 12 locale
catalogs and production build pass.

Document API verification uses actual isolated HTTP servers and both SQLite and
PostgreSQL: all 8 route cases pass. Create/edit/revision reads preserve synthetic
credential-shaped examples without secret-pattern rejection. Body history
redaction, CAS, access and size checks remain covered. The solution builds for
both target frameworks with zero warnings and errors.

## Published-image verification

Explorer and document-save follow-up published from source `3af1e56` on
2026-10-18. Dashboard: 362 unit tests, 17 browser workflows and 2 repeated focused
explorer checks pass. Document routes: 8/8 on SQLite/PostgreSQL. Solution builds
with zero warnings/errors. The published stack passed startup/login, dashboard
health, credential-shaped document creation, Reference/Inline previews and cleanup.
Both tags `v0.1.0` and `latest` are registry-verified for AMD64/ARM64:
server `sha256:f76988acc9b71024bd505047bc59770a52eb38089a86e16e48464fc2703c1242`;
dashboard `sha256:1119d32231455213646f2406b0c7cda8e23978029a3ccb18c4bbbbb9116737ec`.
