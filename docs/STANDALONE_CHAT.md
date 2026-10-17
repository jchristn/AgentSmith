# Standalone agent chat

Hugo can publish an agent's chat at `/chat/{linkId}`, outside the dashboard navigation. This follows AssistantHub's separate chat-page pattern. Publication is disabled by default and is managed by a user who can update the agent profile.

## Manage a link

Open the agent profile's chat-link controls in the dashboard. Enable the link, choose **Authenticated** or **Public**, and save. Copy the generated URL or open it in a new tab. Disable the publication to stop access. Rotate the link to invalidate the old URL and its conversations. Changing the access mode or public execution credential invalidates existing visitor sessions too.

Authenticated links require a Hugo login. Visitors use their own profile execution permissions and resource visibility. Signing in returns to the standalone page; it does not require entering the dashboard.

Public links require an explicitly selected active credential owned by the publisher. Visitors do not receive that credential's access key, secret, or a normal Hugo login token. Hugo runs the conversation under its scoped grants, capped by its owner's permissions, and the agent's normal bindings, access modes, deny rules, and approvals. Owner administrator bypass flags are not inherited. Choose a credential whose scope matches the access intended for public visitors. It needs Execute on the published AgentProfile and Create, Read, and Update on Conversation for its visitor sessions. A profile lock must match the published agent. Operations requiring approval remain approval-gated. The approval worker restores the publication scope from durable conversation metadata and rechecks its current revision before execution, so a queued action cannot run after its link is disabled or rotated.

Public chat cannot edit agent instructions or saved resource context and does not offer management tools. Profile instructions, private model details, raw tool arguments, raw tool output, and provider thinking are not returned by the standalone API. The final assistant answer can contain resource information the selected credential and bindings permit it to retrieve.

The existing profile create/update API accepts these publication fields:

| Field | Meaning |
|---|---|
| `chatEnabled` | Enable or disable publication; defaults to false |
| `chatAccessMode` | `Authenticated` (default) or `Public` |
| `chatCredentialId` | Owned public execution credential; an empty string clears the selection |
| `rotateChatLink` | Request a new URL; update-only action, not stored as a profile setting |

Profile responses include the opaque `chatLinkId` for constructing the standalone URL. Saving publication settings invalidates existing visitor sessions. Concurrent publication edits are rejected rather than silently restoring a disabled or rotated link. Unrelated profile saves preserve the current publication settings. Duplicating a profile does not copy its publication or visitor sessions.

## Visitor sessions

Each new conversation receives a separate random session token. The browser keeps that token in session storage for that link; it is not part of the URL. Every conversation read, message, or cancellation requires the token. Authenticated links additionally require the signed-in visitor's bearer token. A conversation identifier alone cannot access another visitor's messages.

Hugo persists only the token hash in the conversation's external key, bound to the link and publication revision. Every request checks the current publication. Public operations also recheck the active profile, selected credential, credential expiry, active owner, and active tenant before normal authorization. Disabling a link, rotating it, changing its mode or execution credential, or revoking its credential stops access to existing public sessions.

The page provides a chat transcript, message composer, new-conversation action, and cancellation. Responses use safe JSON rather than forwarding the dashboard's internal tool and reasoning event stream. POST session creation and message requests are rate limited.

## API

All paths start with `/v1.0/chat/{linkId}`. Requests use JSON. Conversation operations require `x-hugo-chat-session`; Authenticated mode also requires `Authorization: Bearer ...`.

| Method | Path suffix | Purpose |
|---|---|---|
| GET | none | Read safe publication metadata and whether login is required |
| POST | `/conversations` | Start an isolated conversation; returns its ID and visitor session token |
| GET | `/conversations/{id}/messages` | Read that visitor's user/assistant transcript |
| POST | `/conversations/{id}/messages` | Send `{ "message": "..." }` and return the final answer/status and usage |
| POST | `/conversations/{id}/cancel` | Cancel that conversation's active turn |

Session-token headers and session-creation response bodies are redacted or suppressed in request history. No endpoint lists other visitors' conversations. Schema migration 10 adds the profile publication settings; it preserves existing profiles as unpublished.
