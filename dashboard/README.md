# Hugo dashboard

The operator console for Hugo: chat with Agent Smith, connect your systems, compose agent profiles, decide approvals,
and see everything that happened. It is a React 19, Vite 6, and React Router 7 app in plain JavaScript and JSX,
forked from the Garrison dashboard (plan decision D-032) and kept to the requirements in
`~/Code/Agents/requirements/FRONTEND_ARCHITECTURE.md`, `DASHBOARD_STYLE_AND_USABILITY.md`, and `I18N.md`.

## Run it

```bash
npm ci
npm run dev            # development only: http://127.0.0.1:3000, proxies /v1.0, /mcp, /openapi.json to HUGO_API_URL (default http://127.0.0.1:8800)
npm run lint
npm test               # Vitest unit and component suites
npm run i18n:check     # missing, orphaned, and hard-coded strings; locale parity; placeholders; no dashes
npm run build
npm run test:e2e       # Playwright against mocked APIs (no server needed)
npm run test:live      # Playwright against a running stack (HUGO_DASHBOARD_URL, HUGO_ADMIN_PASSWORD)
npm run format         # Prettier
```

The container image serves the build with nginx and proxies `/v1.0`, `/mcp`, and `/openapi.json` to
`HUGO_API_UPSTREAM` (default `http://hugo-server:8800`) with `proxy_buffering off`, so chat streams and the event
stream arrive as they are produced. `/health` answers `OK` for the compose healthcheck.

```bash
docker build -t jchristn/hugo-dashboard:0.1.0-alpha dashboard
docker run -p 8801:80 -e HUGO_API_UPSTREAM=http://hugo-server:8800 jchristn/hugo-dashboard:0.1.0-alpha
```

The login form's server URL defaults to the page origin (the nginx proxy); for local development it is
`http://127.0.0.1:8800`. `HUGO_API_URL` in the container sets a different browser-facing API URL.

## Layout

| Path | Contents |
|---|---|
| `src/config/navConfig.js` | The single source of navigation: plan section 18.2 groups, routes, and role gating. |
| `src/utils/api.js` | The only REST client: every endpoint is a named method; normalized pages and errors; fetch-based SSE. |
| `src/utils/eventStream.js` | The tenant event stream (`GET /v1.0/api/events`) with polling fallback. |
| `src/utils/chatStream.js` | The chat transcript reducer for the D-022 stream events. |
| `src/views/resourceConfigs.jsx` | Record pages for the Garrison-forked administration routes. |
| `src/views/hugoConfigs.jsx` | Record pages for Hugo's own routes (resources, secrets, profiles, approvals, and more). |
| `src/components/` | Shell and shared components (below). |
| `src/i18n/` | i18next bootstrap, locale registry, lazy catalogs, pseudo-locales, formatters. |
| `scripts/i18n-check.mjs` | The i18n CI check. |
| `tests/e2e/` | Playwright suites and the Hugo API mocks. |

Shared components built for later phases: `DataTable`, `TableFrame`, `TablePagination`, `ActionMenu`, `RowActions`,
`Modal` (focus trap, Escape, backdrop close, scroll lock, pauses auto-refresh), `ConfirmModal`, `JsonViewerModal`,
`CopyButton`, `CopyableId`, `StatusBadge` with `EffectBadge` (icon and text for Read, Write, Destructive) and
`EnumBadge`, `FilterBar`, `ActivityChart`, `TimeSeriesChart`, toasts with a progress panel, `SchemaForm` (connector
settings schemas), `SecretField` (slot picker with inline create, kind-specific value forms, file import),
`PromptEditor` (monospace, variables palette, preview, diff), `ChatPanel`, `ToolActivityRow`, `ApprovalCard`,
`ErrorPresenter`, and `UnavailableState`.

## Route inventory (plan section 18.3)

| Route | User job | Primary roles | Backend resources | Tables and filters | Actions and modals | Empty and error states |
|---|---|---|---|---|---|---|
| `/` | Log in | All | `POST /v1.0/tenants/lookup`, `POST /v1.0/token`, `GET /v1.0/token/details` | | Server URL, email, tenant discovery (picker when several), password reveal, local default credentials, language and theme | Bad credentials, server unreachable, forced password change |
| `/dashboard/home` | See what Hugo is doing | All | request-history summary, tool-invocations summary and list, resources, model-endpoints, analytics overview, approvals | KPI tiles, activity chart (hour, day, week, month, refresh, bucket click) | CTA cards, external-services card low on the page | Setup callout on a fresh tenant; per-panel partial failure banner |
| `/dashboard/chat`, `/dashboard/chat/:id` | Talk to Agent Smith | Operator, ProfileUser | conversations, messages (SSE), approvals, agent-profiles, bindings, model-endpoints | Conversation list with search | Profile picker, model chip, Stop, tool rows, inline approval cards, context drawer, export, drafts per conversation | No profiles, no inference model (link to Model Endpoints), stream error, route missing |
| `/dashboard/profiles` | Manage agent profiles | Builder | agent-profiles | Visibility filter, search | Add, edit, duplicate (server), delete, View JSON, chat | Explainer with diagram; route missing |
| `/dashboard/profiles/:id` | Compose a profile | Builder | profile, bindings, tool-rules, compiled-instructions, tools, card | Bindings table | General form, add or edit binding, tool rules, instructions with source map and surface picker, tools, Connect snippets | No bindings (link to Resources); each tab explains a missing route |
| `/dashboard/approvals` | Decide pending actions | Operator, TenantAdmin | approvals | Status (default Pending), effect, profile, from | Approve or reject with a note, view invocation, bulk reject, auto-refresh | Nothing pending; route missing |
| `/dashboard/conversations` | Review conversations | TenantAdmin, Auditor | conversations | Source, profile, user, from | Open in Chat, export Markdown, delete | None yet; route missing |
| `/dashboard/resources` | Manage connections | Builder | resources, connectors, secrets | Connector, environment, health | Add or edit with connector picker, schema form, secret slots with inline create, Test Connection before save; test, try a tool (dry run, then execute), referrers, delete with referrers, View JSON | Connector gallery CTA via empty state; route missing |
| `/dashboard/secrets` | Manage credential material | Builder, TenantAdmin | secrets | Kind | Add with kind-specific fields and file import, replace values, reveal (reason, re-authentication, 30 second auto-hide, copy), referrers, delete with referrers | Expiring soon badge; route missing |
| `/dashboard/connectors`, `/dashboard/connectors/:type` | Browse connector types | Builder, system admin | connectors | Category, search | Overview with secret slots, settings schema preview and reference, tools with effects, default instructions with a link to Prompt Templates | Load errors shown on cards; route missing |
| `/dashboard/models` | Manage model endpoints | Builder | model-endpoints, secrets | Kind, provider, health | Add, edit, validate, list provider models, delete | Explains chat needs one; route missing |
| `/dashboard/prompts`, `/dashboard/prompts/:id` | Edit templates | Builder, system admin | prompt-templates (Garrison routes) | Kind, search | PromptEditor with palette, preview, diff; versions with restore; clone; reset | Built-in read-only notice |
| `/dashboard/knowledge` | Teach Hugo about the stack (M2) | Builder | knowledge | Search | Add, edit, delete | Route missing |
| `/dashboard/tool-invocations` | See every action | Operator, Auditor | tool-invocations | Surface, effect, decision, status, profile, resource, tool, principal, from, to | Detail with redacted arguments, output preview, approval, Tempo trace link | Route missing |
| `/dashboard/requests` | Inspect API traffic | All | request-history | Method, status, path, tenant, from, to; page sizes up to 1000 | Request details modal, bulk delete with plain-English confirm | Three distinct empty states |
| `/dashboard/analytics` | Understand usage | TenantAdmin, Operator | analytics overview, tokens, tools, models | Range, profile | Token, tool, latency, and error charts; per-connector table | Per-panel route missing |
| `/dashboard/audit` | Review security events | Auditor | audit (Garrison routes) | Event type, principal, from, to | Detail, delete | |
| `/dashboard/explorer` | Try the REST API | All | `/openapi.json` | Tag grouping, search | Path, query, header, body editors; SSE streaming with Stop; curl, fetch, C# snippets; history of 12; destructive confirm | Missing OpenAPI error naming `/openapi.json` |
| `/dashboard/mcp-explorer` | Try MCP as a profile | Builder, Operator | `/mcp`, `/mcp/{profile}` | Tools by family | initialize instructions, schema-generated tool forms, raw JSON-RPC log | Route missing |
| `/dashboard/a2a-explorer` | Try A2A | Builder | `/v1.0/a2a/{tenant}/{profile}` | | Fetch card, send or stream a message, list tasks | Route missing |
| `/dashboard/tenants` | Manage tenants | System admin | tenants | Search | CRUD, View JSON, lockdown column | |
| `/dashboard/users` | Manage users | TenantAdmin, SecurityAdmin | users | Search | CRUD, reset password, assign role, effective permissions | |
| `/dashboard/credentials` | Manage API credentials | All (own), SecurityAdmin | credentials, agent-profiles | Search | Create (secret once, MCP snippet), rotate, bind to profile, lock to profile, scope assignments, permissions | |
| `/dashboard/rbac` | Roles, permissions, assignments | SecurityAdmin | roles, permissions, user-role and credential-scope assignments | Tabs per entity | Clone built-in, CRUD custom, assignment forms (tenant or resource scope) | |
| `/dashboard/sessions` | Revoke sessions | SecurityAdmin | sessions | Principal type, principal | Revoke | Route missing |
| `/dashboard/settings` | Configure the server | TenantAdmin (view), system admin (edit) | settings, system restart, info | Sections per settings group | Save with per-field errors, restart badges and banner, Restart Server confirm with health polling, Server Info tab with copyable URLs | Environment-overridden fields read-only |
| `/dashboard/plugins` | Manage plugins | System admin | system/plugins | | Enable, disable, load errors | Route missing |

Every Hugo page shows `UnavailableState` when its route answers 404 (or 405 or 501): what the page is for, that this
server does not expose it yet, and the route the dashboard called. It is not a "coming soon" placeholder: the page
works as soon as the server provides the route.

## Backend contract

Identity and administration routes keep Garrison's paths and JSON shapes (`/v1.0/api/tenants/{tenantId}/users`,
enumeration envelopes with `objects` and `totalRecords`). Hugo's routes follow plan section 14.2 and are
tenant-implicit (`/v1.0/api/resources`), answering `{ items, pageNumber, pageSize, totalCount, totalPages }` and the
error body `{ error, message, requestId, traceId, details: [{ field, code, message }] }`. `GET /v1.0/api/info` and
`POST /v1.0/api/system/restart` fall back to Garrison's `/v1.0/api/server-info` and `/v1.0/api/settings/restart` on 404.
The full list of calls is in `src/utils/api.js`.

## References used

| Reference | Why |
|---|---|
| Garrison dashboard (`~/Code/Garrison/dashboard`) | The fork base (D-032): same stack, already compliant with the three requirement documents; its shell, tables, request history, API Explorer, settings, i18n runtime, and tooltip system are reused. |
| Wilson `ToolActivity` pattern | Tool activity rows in chat (plan section 18.4). |
| Tempo and Hydra (via Garrison) | Above-table pagination, request inspector, and route headers. |

## Visual QA

`tests/e2e/visual.spec.js` captures the shell, Home, Chat, Profile detail, Resources, Request History, the request
details modal, API Explorer, and Settings at 1280, 768, and 390 pixels in light and dark themes into
`test-results/visual/`, and fails on horizontal page scroll or clipped buttons. Reviewed and fixed during the first
pass: the topbar crowding at 1280 pixels (context chips now shrink before utilities, the live chip collapses to its
dot), six KPI tiles fitting one row, tab strips scrolling on phones, 32 pixel topbar tap targets on phones, and
sortable headers meeting the 24 pixel target size.

## Internationalization

See `I18N.md` in this directory.
