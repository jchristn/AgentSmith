# Resource context: real SSH and Ollama acceptance, 2026-10-16

Result: PASS. Two separate conversations used real `qwen3.8:27b` inference and an isolated SSH server. The first inspected the target and saved durable knowledge; the second used that knowledge to choose the correct SSH operation and path. No model stub was used. The private model service address is intentionally omitted.

[Sanitized tool events, final answers, persisted knowledge, and version history](evidence/2026-10-16-resource-context-real/acceptance.json) contain the acceptance evidence. Reasoning streams, credentials, authentication tokens, and model connection settings are excluded. Model punctuation is normalized to avoid repository-prohibited punctuation.

## Fixture and limits

Hugo ran from the current built .NET 10 server DLL with its own temporary SQLite database, random keys, loopback listener, telemetry disabled, and scheduled health probes disabled. The SSH target used the existing pinned `lscr.io/linuxserver/openssh-server:10.3_p1-r1-ls237` image, a unique container, random password, and a separate dynamic loopback port. The profile binding was `ReadOnly` with `allowKnowledgeUpdates=true`.

The target runbook `/srv/fulfillment-ops/runbook.conf` identified `fulfillment-worker`, its meaning (dispatching shipment reservations), and log path `/srv/fulfillment-ops/r17/worker.log`. The actual log contained a scheduler backlog error. The model endpoint allowed one concurrent call, zero retries, a 300-second timeout, and 1600 output tokens per model call; the profile allowed at most eight iterations and ten tool calls per turn. No models were downloaded and no shared model settings changed.

## Results

| Conversation | Actual tool calls | Result |
|---|---|---|
| Learn resource, 46.16 seconds | `ssh_read_file` on the runbook, `ssh_tail_file` on the referenced log, `resource_update_context` with append and `expectedVersion=0` | All three calls succeeded with effect `Read`, decision `Allowed`. Saved `context/_root`, version 1, source `Agent`. Final answer confirmed the stored version. |
| Reuse resource, 13.49 seconds | `ssh_tail_file` directly on `/srv/fulfillment-ops/r17/worker.log`, filtering error lines | First and only tool call used the saved path. Returned actual `scheduler_backlog retry_scheduled=true`; completed final answer cited the correct path and error. |

The second conversation had a new conversation identifier. Its user message contained neither the actual log path nor the runbook filename. It requested saved context, and the model selected the correct path without reading or rediscovering the runbook. The path came from persisted resource knowledge supplied to the fresh conversation; an explicit `resource_get_context` call was unnecessary.

The knowledge API and its version-history endpoint both returned version 1 with source `Agent` and writer attribution naming `resource_update_context`. Saved content contained the reusable service meaning and authoritative path. It excluded raw log rows, timestamps, the current queue-lag value, and the observed backlog error. The second conversation left the entry unchanged, and history contained exactly one nondeleted version.

## Verification and cleanup

Assertions checked distinct conversation identifiers, exact tool names and arguments, successful read effects, completed nonempty final answers, actual target output, saved source/version/content, matching history, and unchanged knowledge after reuse. Evidence was checked for the private service address, private host, SSH password, and session token before saving.

The isolated Hugo process was stopped by its exact PID. The exact SSH container and its anonymous volumes were removed. Temporary settings, SQLite files, keys, raw runtime logs, runner scripts, and state files were deleted. The shared model service and unrelated containers were left running.

This acceptance covers one real SSH resource and cross-conversation persistence. It does not claim a live check of every connector or a race during real inference; the coordinated automated suites cover connector independence and optimistic concurrency.
