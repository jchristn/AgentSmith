# Hugo A2A API

Every Hugo agent profile can be an A2A agent. Another agent (a planner, a ticket bot, a second Hugo) sends it a task in plain language, and Hugo's own loop does the work on that profile's resources with that profile's model, approvals, and limits, then reports back as an A2A task. Where MCP lends your model Hugo's tools, A2A hands Hugo the whole job.

Hugo implements A2A 1.0 over the JSON-RPC binding, as Watson routes on the REST port rather than through Voltaic's A2A server, because tasks here are owned by tenants and principals and must stay that way (decision D-006). The older slash-style method names are accepted too. Every example below was recorded against a running server whose profile used a scripted test model, so the agent's wording is canned; the task lifecycle around it is real.

## Routes

| Method | Path | Authentication | Purpose |
|---|---|---|---|
| GET | `/v1.0/a2a/{tenantId}/{profileName}/.well-known/agent-card.json` | None | The agent card of one profile |
| GET | `/v1.0/a2a/{tenantId}` | Read on AgentProfile | Cards of every exposed profile in the tenant that the caller can see |
| POST | `/v1.0/a2a/{tenantId}/{profileName}` | Any REST scheme | JSON-RPC methods |

A profile is reachable when it is active, has `exposeA2A` on (the default), and, for the public card, has Tenant visibility. Anything else answers 404, the same as a profile that does not exist. `A2A.Enable` set to false turns the whole surface off.

Callers authenticate with the REST schemes: a bearer session token, credential keys (`x-access-key` and `x-secret-key`), or a signed request. For an agent that calls Hugo regularly, create a credential bound to the profile and locked to it; a locked credential calling another profile is refused with -32007 `ProfileLocked`. Give the credential a role (`roleName`, for example Operator) when you create it: credentials do not inherit their owner's administrator bypass, so one without a scope assignment is refused with -32007 `AuthorizationDenied`. A SessionExchange credential exchanges its keys at `POST /v1.0/token` first; its keys alone answer 401 here.

## The agent card

```bash
curl -s "$HUGO/v1.0/a2a/default/Hugo/.well-known/agent-card.json"
```

```json
{
  "name": "Hugo",
  "description": "Default operations agent. Attach resources to give Agent Smith something to work with.",
  "version": "0.1.0-alpha",
  "supportedInterfaces": [
    { "url": "http://127.0.0.1:8800/v1.0/a2a/default/Hugo", "protocolBinding": "JSONRPC", "protocolVersion": "1.0" }
  ],
  "capabilities": { "streaming": true, "pushNotifications": false, "stateTransitionHistory": false },
  "provider": { "organization": "Hugo", "url": "https://github.com/jchristn/AgentSmith" },
  "skills": [
    {
      "id": "agent-smith",
      "name": "Agent Smith",
      "description": "Hugo (Agent Smith) investigates and operates the systems attached to it: SQLite (demo-db). Send a plain-language request; changes may require human approval.",
      "tags": ["operations", "investigation"]
    },
    {
      "id": "sql",
      "name": "SQLite",
      "description": "Investigate and operate SQLite resources: demo-db (ReadWriteWithApproval).",
      "tags": ["sql"]
    }
  ],
  "defaultInputModes": ["text/plain", "application/json"],
  "defaultOutputModes": ["text/plain"],
  "securitySchemes": {
    "hugo": {
      "httpAuthSecurityScheme": {
        "scheme": "bearer",
        "description": "Hugo session token from POST /v1.0/token. Credentials may also send x-access-key and x-secret-key headers."
      }
    }
  },
  "securityRequirements": [ { "schemes": { "hugo": [] } } ]
}
```

The card describes the profile, not its internals. `name` is the display name and `description` the profile description. There is one general skill, `agent-smith`, whose text comes from the `a2a.skill-description` prompt template (editable like any template, see [docs/PROMPTS.md](docs/PROMPTS.md)), plus one skill per bound connector family listing the aliases and access modes. Credentials, settings, and the compiled instructions never appear. The interface URL is built from `Rest.ExternalBaseUrl` (`HUGO_EXTERNAL_BASE_URL`) when it is set, otherwise from the request's scheme (honoring `X-Forwarded-Proto`) and `Host` header, so set the external URL when Hugo sits behind a proxy. `GET /v1.0/api/agent-profiles/{id}/card` previews the same card from the management API.

## Methods

| Method | Legacy name | Permission | Purpose |
|---|---|---|---|
| `SendMessage` | `message/send` | Execute on AgentProfile | Start a task (or continue a context) and wait for it |
| `SendStreamingMessage` | `message/stream` | Execute on AgentProfile | Start a task and stream it as SSE |
| `GetTask` | `tasks/get` | Read on Conversation | Read a task |
| `ListTasks` | `tasks/list` | Read on Conversation | List tasks |
| `CancelTask` | `tasks/cancel` | Update on Conversation | Cancel a running or waiting task |
| `SubscribeToTask` | `tasks/resubscribe` | Read on Conversation | Stream a task's updates as SSE |
| `GetExtendedAgentCard` | `agent/getAuthenticatedExtendedCard` | Read on AgentProfile | The card, for an authenticated caller |

A method Hugo does not implement (push notification configuration, for instance) answers -32601 "Method not found".

### SendMessage

Send a user message. Hugo creates a task, starts its own agent loop in the background, and by default waits up to 300 seconds for the task to finish or to need input before answering. Set `configuration.returnImmediately` to get the task back at once in `TASK_STATE_WORKING` and poll it with `GetTask`. The task keeps running if the caller disconnects (D-047).

| Field | Required | Description |
|---|---|---|
| `message.messageId` | yes | Caller's ID for the message |
| `message.role` | yes | `ROLE_USER` |
| `message.parts` | yes | Text parts (`{ "text": "..." }`) and data parts (`{ "data": {...} }`, serialized to JSON for the model). At least one must carry content |
| `message.contextId` | no | Continue a conversation: the `contextId` of an earlier task |
| `message.taskId` | no | Only for tasks that are still waiting; Hugo answers -32007 because approvals are decided by humans, not over A2A |
| `configuration.returnImmediately` | no | `true` returns the task without waiting |

```bash
curl -s -X POST "$HUGO/v1.0/a2a/default/Hugo" -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"SendMessage","params":{"message":{"messageId":"m1","role":"ROLE_USER","parts":[{"text":"Agent Smith, list the tables in demo-db."}]}}}'
```

```json
{
  "jsonrpc": "2.0",
  "result": {
    "task": {
      "id": "a2t_mv92g7xj_2f91mIMpZbIozjpck9I",
      "contextId": "cnv_mv92g7xj_d4mgpqupKzLdBGHKbUg",
      "status": {
        "state": "TASK_STATE_COMPLETED",
        "message": {
          "role": "ROLE_AGENT",
          "parts": [ { "text": "Mr. Anderson, demo-db answered. It is an in-memory SQLite database, so it holds no tables until something creates them. Nothing needs your attention." } ],
          "messageId": "a2t_mv92g7xj_2f91mIMpZbIozjpck9I-completed",
          "contextId": "cnv_mv92g7xj_d4mgpqupKzLdBGHKbUg",
          "taskId": "a2t_mv92g7xj_2f91mIMpZbIozjpck9I"
        },
        "timestamp": "2026-10-15T04:56:14.20115+00:00"
      },
      "artifacts": [
        {
          "artifactId": "a2t_mv92g7xj_2f91mIMpZbIozjpck9I-result",
          "name": "result",
          "parts": [ { "text": "Mr. Anderson, demo-db answered. ...", "mediaType": "text/plain" } ]
        }
      ],
      "metadata": {
        "hugo.conversationId": "cnv_mv92g7xj_d4mgpqupKzLdBGHKbUg",
        "hugo.agentProfileId": "agp_mv927keb_YuOf8qpccRHoxCYgG7V",
        "hugo.inputTokens": 6283,
        "hugo.outputTokens": 46,
        "hugo.toolCallCount": 1
      }
    }
  },
  "id": 1
}
```

The `contextId` is the ID of the Hugo conversation behind the task, so the whole exchange, including every tool call the loop made, is visible in the dashboard and through `GET /v1.0/api/conversations/{id}/messages`. Sending a new message with the same `contextId` continues that conversation. The final answer arrives twice on purpose: as the status message, and as the `result` artifact that A2A clients collect.

The profile needs an inference endpoint. Without one, SendMessage fails fast:

```json
{"jsonrpc":"2.0","error":{"code":-32602,"message":"Agent profile Hugo has no inference endpoint. Create a model endpoint and set it on the profile before chatting.","data":{"code":"ModelEndpointRequired"}},"id":1}
```

### SendStreamingMessage

The same request, answered as Server-Sent Events. The first event is the task, then artifact updates and status updates follow until a final state, and the stream closes:

```text
data: {"jsonrpc":"2.0","result":{"task":{"id":"a2t_mv92hbvv_3ckpvk1nwNltGPBkomv","contextId":"cnv_mv92hbvv_MjRE5ZRcIjl0YQd1ppG","status":{"state":"TASK_STATE_WORKING","timestamp":"2026-10-15T04:57:05.899702+00:00"},"metadata":{...}}},"id":2}

data: {"jsonrpc":"2.0","result":{"artifactUpdate":{"taskId":"a2t_mv92hbvv_3ckpvk1nwNltGPBkomv","contextId":"cnv_mv92hbvv_MjRE5ZRcIjl0YQd1ppG","artifact":{"artifactId":"a2t_mv92hbvv_3ckpvk1nwNltGPBkomv-result","name":"result","parts":[{"text":"Good evening, Mr. Anderson. I am Agent Smith. Ask me about the resources bound to this profile.","mediaType":"text/plain"}]},"append":false,"lastChunk":true}},"id":2}

data: {"jsonrpc":"2.0","result":{"statusUpdate":{"taskId":"a2t_mv92hbvv_3ckpvk1nwNltGPBkomv","contextId":"cnv_mv92hbvv_MjRE5ZRcIjl0YQd1ppG","status":{"state":"TASK_STATE_COMPLETED","message":{...},"timestamp":"2026-10-15T04:57:05.902259+00:00"}}},"id":2}
```

A status update is sent whenever the state or the status message changes, which includes the move to input-required when an approval is pending. The stream also ends when the server stops; reconnect with `SubscribeToTask`.

### GetTask

| Field | Required | Description |
|---|---|---|
| `id` | yes | Task ID |
| `historyLength` | no | Messages of history to include |

The result is the task object shown above. Tasks belong to the principal that created them; tenant administrators can read every task in the tenant. Anyone else, and any task of another profile, gets -32001 "Task ... not found", so task IDs reveal nothing.

### ListTasks

| Field | Description |
|---|---|
| `pageSize` | 1 to 100, default 50 |
| `pageToken` | `nextPageToken` from the previous page |
| `contextId` | Only tasks in this context |
| `status` | Only tasks in this state, for example `TASK_STATE_INPUT_REQUIRED` |
| `includeArtifacts` | Include artifacts (default false) |

The result is `{ "tasks": [...], "pageSize": 50, "totalSize": 7, "nextPageToken": "" }`, newest first. Callers see their own tasks; tenant administrators see all of the profile's tasks.

### CancelTask

`{ "id": "a2t_..." }` cancels a task that is working or waiting for input. The result is the task in `TASK_STATE_CANCELED` with the status message "The task was cancelled." A task that already finished answers -32002:

```json
{"jsonrpc":"2.0","error":{"code":-32002,"message":"Task a2t_mv92g7xj_2f91mIMpZbIozjpck9I already finished (Completed).","data":{"code":"TaskNotCancelable"}},"id":2}
```

### SubscribeToTask

`{ "id": "a2t_..." }` streams the task the way `SendStreamingMessage` does, starting from its current state. For a task that already finished, the stream carries the final state and closes.

### GetExtendedAgentCard

Returns the agent card to an authenticated caller. In this release it is the same card as the public one.

## Task states

| Hugo state | Wire value | When |
|---|---|---|
| Submitted | `TASK_STATE_SUBMITTED` | Created, loop not started |
| Working | `TASK_STATE_WORKING` | The agent loop is running |
| InputRequired | `TASK_STATE_INPUT_REQUIRED` | A tool call waits for human approval |
| Completed | `TASK_STATE_COMPLETED` | The loop answered |
| Failed | `TASK_STATE_FAILED` | The loop failed (model error, iteration limit); the status message says why |
| Canceled | `TASK_STATE_CANCELED` | CancelTask |
| Rejected | `TASK_STATE_REJECTED` | Hugo refused the task |

Tasks are stored in the database with their tenant and owner, so their state and results survive a restart.

## Approvals

Hugo's safety model applies to A2A exactly as to every other surface: when the loop wants to run a Write or Destructive call that the binding gates, the pipeline creates an approval and the task moves to `TASK_STATE_INPUT_REQUIRED`. A blocking SendMessage returns at that point:

```json
{
  "task": {
    "id": "a2t_mv92grdc_pv3dQeKjBe832dLdjLu",
    "contextId": "cnv_mv92grdc_MT91tdVe96gIdLazQ6l",
    "status": {
      "state": "TASK_STATE_INPUT_REQUIRED",
      "message": {
        "role": "ROLE_AGENT",
        "parts": [ { "text": "Waiting for approval apr_mv92grdg_TKgrdFLr9gYhqmtSGQS: Run SQL on demo-db: CREATE TABLE audit_note (id INTEGER PRIMARY KEY, note TEXT). A person with approval rights must approve or reject it in the Hugo dashboard or over REST (POST /v1.0/api/approvals/apr_mv92grdg_TKgrdFLr9gYhqmtSGQS/approve); the task resumes automatically. An A2A caller cannot approve its own request." } ],
        "metadata": { "hugo.approvalId": "apr_mv92grdg_TKgrdFLr9gYhqmtSGQS" }
      }
    },
    "metadata": { "hugo.pendingApprovalId": "apr_mv92grdg_TKgrdFLr9gYhqmtSGQS" }
  }
}
```

Unlike a normal A2A input-required state, the input is not something the calling agent can provide. A person decides in the dashboard or over REST, and the task resumes on its own and finishes; poll `GetTask` or hold a `SubscribeToTask` stream to see it complete. Sending another message to the task answers -32007 `ApprovalDecidedByHuman`. The calling principal cannot approve its own A2A request even through the REST API and even when `Approvals.AllowSelfApproval` is true (the REST call answers 409 `SelfApprovalNotAllowed`), so an agent cannot approve its own writes. If the approval expires or is rejected, the loop learns that the call did not run and answers accordingly.

## Errors

JSON-RPC errors come back with HTTP 200, as the A2A JSON-RPC binding expects, except for an unknown agent (HTTP 404) and authentication failures (HTTP 401 with the REST error body). The `data.code` field carries a stable Hugo code.

| Code | `data.code` | Meaning |
|---|---|---|
| -32700 | | The body is not JSON |
| -32601 | | Unknown method |
| -32602 | `InvalidParams`, `ModelEndpointRequired`, validation codes | Bad parameters, empty message, or a profile without a model |
| -32001 | `NotFound`, `TaskNotFound` | No such agent or task (or not yours) |
| -32002 | `TaskNotCancelable` | Cancel of a finished task |
| -32007 | `AuthorizationDenied`, `ProfileLocked`, `ApprovalDecidedByHuman`, `TurnInProgress`, `TaskFinished` | Refused: permissions, a locked credential, or a message to a task that cannot take one |
| -32603 | `DatabaseUnavailable` | Database outage (HTTP 503 with `Retry-After`) |

## Calling Hugo from another agent

Any A2A 1.0 client works. Fetch the card, take the interface URL, authenticate with a credential, and send. Request bodies are never captured in Hugo's request history; the conversation, its messages, and the tool invocations are, which is where to look when a remote agent claims Hugo did something. For the other direction (Hugo calling remote A2A agents as resources) see the `a2a_*` tools in [MCP_API.md](MCP_API.md#remote-a2a-agents-a2a_) and [docs/CONNECTORS.md](docs/CONNECTORS.md).
