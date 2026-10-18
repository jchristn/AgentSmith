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

## Checkbox layout correction

The earlier workflow checks inspected confirmation dialogs but did not measure
conversation-row layout. A new geometry regression reproduced the reported bug:
the global `input { width: 100% }` rule made a non-shrinking selection checkbox
206 pixels wide and squeezed the title out of its row. The fix scopes checkbox
width/height and flex basis to 1rem inside the chat list and aligns its select-shown
label without changing text input sizing.

The regression now checks actual checkbox and title bounding boxes with a long
conversation name at 1280 and 390 pixels in light and dark themes. The checkbox
is 16 pixels wide and titles have more than 120 pixels available. Lint, production
build and eight browser cases (six deletion/layout plus two background-chat cases)
pass. All six deletion/layout cases also pass against the published dashboard
container. Screenshot evidence is retained locally under the ignored evidence
folder, including `selection-light-1280.png` and `selection-dark-390.png`.

Source `f5fc84d` is pushed to main. Dashboard `v0.1.0` and `latest` both resolve to
`sha256:0bab6bf906e1176241f7376a22bbee082a929cf47d4190532ed553afcd43f977`,
verified for AMD64 and ARM64. The isolated container was healthy and removed after
verification. Pull and recreate the dashboard container to apply the CSS fix.
