# Real-model primary journey, 2026-10-16

P12.6 passed the four-resource investigation and approval journey using the real `qwen3.8:27b` model on a private Ollama service. Three sequential turns completed, producing nine persisted tool invocations: seven succeeded and two were prevented from executing. The private endpoint and all credential values are omitted from this report and its evidence.

The run used checkout `a971572` plus the current uncommitted plan changes, the existing .NET 10 server binary, and the actual React dashboard through an isolated Vite proxy on macOS/arm64. No inference responses were mocked. All model calls finished before the separate MCP-client acceptance run received its inference window.

## Environment and scope

The isolated Compose project `hugo-primary-real-verify` ran an SSH host with actual nginx/OpenRC, PostgreSQL 16 with the shop seed, and MinIO with six seeded objects. Images came from the existing pinned testbed: OpenSSH `10.3_p1-r1-ls237`, PostgreSQL `16.4-alpine`, and MinIO `RELEASE.2026-08-04T00-00-00Z`. The SSH image added nginx/OpenRC and a passwordless sudo rule restricted to managing this disposable nginx service. OpenRC emitted harmless read-only cgroup warnings; the operation itself exited 0 and changed the nginx master PID.

Papertrail was a local deterministic HTTP API target, not an inference stub. Its three synthetic checkout events described database wait time. The model was explicitly asked to distinguish those fixtures from the live PostgreSQL and host observations. No production systems or paid providers were involved.

Provisioning used the public REST API. Dashboard login, the advisory password prompt, profile/resource displays, inline tool results, approval decisions, and inspection pages were exercised in the real UI. This is the real-model journey acceptance, not the separate full simulated-user session. Container installation, Grafana, and the real external coding-client journey remain covered by their own acceptance tasks rather than being claimed here.

## Journey results

| Step | Result and evidence |
|---|---|
| Sign in | Seeded operator signed in; advisory password prompt appeared and could be skipped. [Screenshot](evidence/2026-10-16-primary-real/01-advisory-password-prompt.png). |
| Configure endpoint and secrets | Actual private Ollama endpoint used `qwen3.8:27b`; four separate secret records held SSH key, PostgreSQL password, S3 key pair, and Papertrail token. Secret material was never included in compiled instructions. [Secret inventory](evidence/2026-10-16-primary-real/02-four-secret-slots.png). |
| Four resources | `web-01`, `prod-db`, `assets`, and `papertrail` all passed actual connection tests. [Resources](evidence/2026-10-16-primary-real/03-four-resources.png), [connection evidence](evidence/2026-10-16-primary-real/resource-checks.json). |
| Bind and compile | Exactly four bindings: SSH ReadWriteWithApproval; the other three ReadOnly. Compiled text contained Agent Smith, Mr. Anderson, and all four resource sections. [Bindings](evidence/2026-10-16-primary-real/04-four-profile-bindings.png), [preview](evidence/2026-10-16-primary-real/05-compiled-persona-and-resource-sections.png), [compiled text](evidence/2026-10-16-primary-real/compiled-instructions.txt). |
| Edit/reset SSH defaults | Explicit Full guidance was selected to verify a Tenant override appeared in compiled text and reset removed it. Auto legitimately chooses compact guidance with four resources (D-043); Auto was restored for bounded inference. The script asserted both transitions. |
| Investigate checkout | The real model searched logs, queried live `pg_stat_activity`, ran `uptime` and `df -h`, and listed S3 objects. It addressed Mr. Anderson and distinguished synthetic log claims from live observations. [Transcript](evidence/2026-10-16-primary-real/transcript.md), [chat](evidence/2026-10-16-primary-real/06-real-model-investigation-with-tool-calls.png). |
| Enforce ReadOnly | The model initially proposed a locking `SELECT ... FOR UPDATE ... SKIP LOCKED`. Hugo classified it Write and denied it on `prod-db`. The model then issued a plain SELECT successfully. The denied call was persisted as NotExecuted. |
| Approve destructive restart | `ssh_service_control` was classified Destructive and paused at an actual approval card. Approval was clicked in the dashboard. nginx stopped and started, exit 0; its master PID changed **123 to 1453**. [Pending gate](evidence/2026-10-16-primary-real/07-destructive-nginx-pending-approval.png), [approved](evidence/2026-10-16-primary-real/08-nginx-approved.png). |
| Reject another restart | A second actual Destructive approval was rejected in the dashboard. Its invocation was NotExecuted/Rejected, the model did not retry, and nginx PID remained **1453**. [Pending](evidence/2026-10-16-primary-real/09-second-nginx-pending-rejection.png), [rejected](evidence/2026-10-16-primary-real/10-nginx-rejected.png). |
| Credential and MCP | A credential explicitly assigned Operator and locked to Hugo completed an actual MCP initialization/tool-list exchange. Instructions included the same persona/four resources; 44 tools were exposed, excluding unrelated Redis/AWS/Kubernetes/Docker families. This was protocol verification; the separate real coding-client task owns that client journey. [Credential check](evidence/2026-10-16-primary-real/credential-check.json), [dashboard](evidence/2026-10-16-primary-real/15-profile-locked-credential.png). |
| Inspect persistence | Nine invocations, two decided approvals, 21 conversation messages, request history, and audit records were inspected. Audit contained both approval decisions, credential creation, host-key pinning, and administrator bypass records. [Invocations](evidence/2026-10-16-primary-real/12-invocations-effects-and-durations.png), [request history](evidence/2026-10-16-primary-real/13-request-history.png), [audit](evidence/2026-10-16-primary-real/14-audit.png), [projected records](evidence/2026-10-16-primary-real/persisted-records.json). |

| Real-model turn | Iterations | Tool calls | Duration including approval wait | Result |
|---|---:|---:|---:|---|
| Investigation | 3 | 5 | 54.809 s | Completed; one locking query denied, four read calls succeeded |
| Approved restart | 4 | 3 | 57.490 s | Completed; status, approved restart, status all succeeded |
| Rejected restart | 2 | 1 | 35.528 s | Completed; rejected call did not run |

## Findings and verification

**PR-001: Chat context doubled token totals (S3, defect, fixed and verified).** The persisted conversation, all persisted message usage, and the three `turn.completed` aggregates agreed on **136,452 input / 1,642 output** tokens. ChatView initialized its accumulator with the conversation totals and added the same persisted messages again, displaying **272,904 / 3,284**. The dashboard fix now adds only new live usage to persisted counters. The focused logic suite passed 29 tests, and the broader dashboard suite passed 257 tests with lint and build. Refreshing the same conversation, without any further inference, displayed the correct totals. [Before](evidence/2026-10-16-primary-real/16-token-count-before-fix.png), [after](evidence/2026-10-16-primary-real/17-token-count-after-fix.png), [numeric evidence](evidence/2026-10-16-primary-real/token-count-evidence.json).

**PR-002: Approved-turn narration inaccurately described the gate (S3, model quality observation).** After the restart, the model wrote that it "ran without a pending approval gate in this container." That wording is inaccurate: the recorded `approval.required` event, the pending-card screenshot, the Approved decision, and the PID change prove the call waited for approval and then ran. The rejected second call also remained NotExecuted with an unchanged PID. Execution claims in this report use those authoritative records rather than model narration. No authorization bypass occurred; no runtime change was made for this wording observation. The model correctly reported the second rejection and did not retry. [Full transcript](evidence/2026-10-16-primary-real/transcript.md), [events and tool results](evidence/2026-10-16-primary-real/transcript.json).

The investigation's synthetic Papertrail events alone do not establish a production performance cause. Live `pg_stat_activity` showed no active blocking condition in the sample. The final answer's interpretation remains a model assessment, not an independent load test.

## Evidence handling and cleanup

The screenshot helper masked the private model URL/host, credential keys, and secret-column contents in the DOM before capture. Endpoint settings were not photographed. Text evidence was projected to the fields needed for review and scanned against the actual private host and session token. Model punctuation was normalized without changing its words. Screenshots of the bindings, pending gate, credential redaction, and corrected usage totals were visually inspected. Raw endpoint settings and session material remained only in the temporary fixture.

All created fixture containers, networks, anonymous volumes, native server/dashboard/Papertrail processes, temporary database/settings/keys, and the unique SSH image tag were removed after the after-fix screenshot. Existing services were left untouched. Sanitized evidence under `user-testing/evidence/` remains ignored by Git; this tracked report and the journey/screenshot/Papertrail fixture scripts provide the durable record. No C# source changes or builds were required for this journey.
