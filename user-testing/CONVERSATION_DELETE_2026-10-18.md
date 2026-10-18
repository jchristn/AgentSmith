# Dashboard conversation deletion verification

The Chat sidebar supports per-row and select-shown checkboxes, a selected count
and Delete selected. Both Chat and Agents > Conversations use the shared
confirmation modal with conversation titles, monospace/copyable IDs, explicit
message deletion and irreversible-action text. No browser alert or confirm is used.

Five Playwright workflows use isolated synthetic HTTP fixtures: cancel without
requests, confirm multiple deletions, retain failed/running conversations for
retry without calling cancel, prevent dismissal/double-submit while deleting,
return to new chat when deleting the open conversation, clear hidden selections
on search, and the equivalent Agents table confirmation. The existing server
DELETE route continues to enforce ownership/permissions and reject active turns.
No backend or schema change is required.

Dashboard lint, all 12 catalogs, 362 unit tests and 22 targeted browser cases pass.
The browser run also covers background completion/Stop and the standard operator
flows. Playwright's web server builds the production dashboard before the run.
The confirmation was inspected at desktop and 390-pixel widths; it fits both.
Local screenshot artifacts are under ignored `user-testing/evidence/conversation-delete/`.

Reproduce from `dashboard/` with:

```sh
npm run lint
npm run i18n:check
npm test
npx playwright test tests/e2e/conversation-delete.spec.js tests/e2e/background-chat.spec.js tests/e2e/flows.spec.js
```

These browser checks use mocked APIs and make no claim of deleting production
conversations. Only records explicitly confirmed by the user are submitted to
the existing per-conversation DELETE endpoint, sequentially, with progress and
success/failure counts.

## Published-image check

Implementation source `8fc2a3b` is pushed to main. The dashboard image is published
under `v0.1.0` and `latest`, both verified for AMD64/ARM64 at
`sha256:291097508e7e281e3d57f002d81bc167ba61c95ea3e1d3461d7fd7b2e6ede2a7`.
The image started in an isolated nginx container, passed its healthcheck, and all
five conversation-delete Playwright workflows passed again against that served
build using synthetic API fixtures. The test container was removed afterward.
Pull and recreate the dashboard container to use the new controls.
