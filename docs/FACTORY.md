# The factory stack

The factory stack is a complete Hugo installation that comes up already configured. One command wipes it and builds it again from nothing, so you always start from the same known state. Use it to:

- **evaluate** Hugo without spending an afternoon on setup;
- **demo** the primary journey (resources, a profile with eight bindings, approvals, MCP clients, Grafana);
- **run simulated user testing**, where every session must begin from identical data.

It is not a deployment. Every password and key in it is a published demo value. For a real installation use `docker/compose.yaml` (see [DOCKER.md](DOCKER.md)).

## What runs

`docker/factory/compose.yaml` uses the Compose project name `hugo-factory`. It runs the same images and the same observability wiring as `docker/compose.yaml`, so what you see here is what you get there. It also runs a set of demo targets and a one-shot seed container.

| Service | What it is | Host address |
|---|---|---|
| `hugo-dashboard` | Dashboard (`jchristn77/hugo-dashboard:v0.1.0`) | http://127.0.0.1:8811 |
| `hugo-server` | REST, MCP (`/mcp`), A2A, OpenAI-compatible API (`jchristn77/hugo-server:v0.1.0`) | http://127.0.0.1:8810 |
| `hugo-grafana` | Grafana with the Hugo dashboards (admin / admin) | http://127.0.0.1:3010 |
| `hugo-prometheus` | Prometheus, scraping Hugo | http://127.0.0.1:9091 |
| `hugo-loki` | Loki, holding Hugo's container logs | http://127.0.0.1:3110 |
| `hugo-tempo` | Tempo, holding Hugo's traces | http://127.0.0.1:3210 |
| `hugo-alloy` | Alloy: OTLP traces to Tempo, container logs to Loki | none |
| `ssh-target` | OpenSSH server playing `web-01` | none |
| `postgres`, `mysql` | PostgreSQL 16 and MySQL 8.4, each with the testbed `shop` database | none |
| `minio` | MinIO (S3 API) with the bucket `hugo-testbed` | none |
| `redis` | Redis 7.4 with seeded keys and a read-only ACL user | none |
| `hugo-seed` | One-shot container that configures Hugo through the REST API, then exits | none |

The ports are chosen so the factory stack runs next to the plain stack in `docker/` (8800, 8801, 3000, 9090, 3100, 3200). Change them with `HUGO_FACTORY_REST_PORT`, `HUGO_FACTORY_DASHBOARD_PORT`, `HUGO_FACTORY_GRAFANA_PORT`, `HUGO_FACTORY_PROMETHEUS_PORT`, `HUGO_FACTORY_LOKI_PORT`, and `HUGO_FACTORY_TEMPO_PORT`. The demo targets publish no host ports; Hugo reaches them over the Compose network by service name. They reuse the images and seed data in `docker/testbed`.

Every host port binds to `127.0.0.1` only.

## Start, reset, stop

The first start pulls about a dozen images and initializes two databases, so give it a few minutes. Later starts take well under a minute.

```bash
cd docker/factory
./reset.sh                          # Windows: reset.bat
```

`reset.sh` asks you to type `RESET`, then runs `docker compose down -v`, `docker compose up -d`, waits for the seed container, and prints the container status, the URLs, and the demo credential. It is destructive: it deletes every factory volume, including stored secrets, request history, metrics, logs, and traces. It never touches the plain stack in `docker/` (project `hugo`). For unattended runs, pipe the confirmation in: `echo RESET | ./reset.sh`.

Other everyday commands, run from `docker/factory`:

```bash
docker compose up -d                # start or restart without wiping; the seed runs again (it is idempotent)
docker compose logs hugo-seed       # what the seed did, and the demo credential
docker compose ps -a                # container status
docker compose down                 # stop, keep the data
docker compose down -v              # stop and delete the factory data
```

The seed container exits with code 0 when every step succeeded. When a resource's connection test fails, it still finishes the rest and then exits with code 1, and `reset.sh` says so.

## Sign in

| What | Value |
|---|---|
| Dashboard | http://127.0.0.1:8811 |
| Tenant | `default` |
| User | `anderson@hugo.local` (Thomas Anderson, tenant administrator) |
| Password | `HugoDemo2026!` |

The seed replaces the first-boot password (`password`) with the demo password, so you are not asked to change it. Set `HUGO_FACTORY_DEMO_PASSWORD` before a reset to use a different one. The system administrator `admin@hugo.local` keeps its first-boot password `password` and must change it at first sign-in.

## What is seeded

The seed talks only to the public REST API, the same calls the dashboard makes. Every object is looked up by name and created or updated, so running it twice changes nothing. The code is in `docker/factory/seed/seed.py`.

**Secrets.** Six secrets hold the target logins: `factory-web-01-key` (an ed25519 key generated on each reset by the `ssh-keys` container), `factory-prod-db`, `factory-legacy-db`, `factory-minio`, `factory-redis-reader`, and `factory-grafana`.

**Resources and bindings.** Eight resources, all with environment `demo`, all bound to the default `Hugo` profile. This is the eight-binding profile of plan section 10.5 adapted to targets that run without privileged containers (the AWS and Kubernetes bindings are replaced by Prometheus, Loki, and Grafana).

| Resource | Connector | Target | Access mode |
|---|---|---|---|
| `prod-db` | PostgreSQL | `shop` database on `postgres` | ReadOnly |
| `legacy-db` | MySQL | `shop` database on `mysql` | ReadOnly |
| `web-01` | SSH | `ssh-target:2222`, user `hugo`, key authentication | ReadWriteWithApproval |
| `assets` | Amazon S3 | bucket `hugo-testbed` on MinIO | ReadOnly |
| `cache` | Redis | `redis:6379` as the ACL user `hugo-reader` | ReadWriteWithApproval |
| `metrics` | Prometheus | this stack's Prometheus | ReadOnly |
| `logs` | Grafana Loki | this stack's Loki | ReadOnly |
| `grafana` | HTTP API | this stack's Grafana API, GET and HEAD under `/api/` | ReadOnly |

The mix shows each access mode at work. Reads on every resource run immediately. Write and Destructive calls on `web-01` and `cache` wait for approval in the dashboard (Approvals). Write calls on the ReadOnly resources are refused, and their write tools are not offered at all. `cache` is ReadWriteWithApproval but signs in as a read-only Redis user, so an approved write still fails at Redis: a useful demonstration that Hugo's access mode and the target's own permissions are separate layers.

Because the profile has more than three bindings, its compiled instructions use the compact connector text (decision D-043, about 10 KB). The agent calls `hugo_describe_resource` for the full text of one resource.

**Model endpoint.** When an Ollama answers at `http://host.docker.internal:11434`, the seed creates the endpoint `factory-ollama` with the first chat model it finds (it prefers `llama3.1` and `qwen` models) and makes it the `Hugo` profile's inference endpoint. Without Ollama there is no model endpoint, and everything except Hugo's own chat still works: MCP clients bring their own model. To choose the model or the URL, set `HUGO_FACTORY_OLLAMA_MODEL` or `HUGO_FACTORY_OLLAMA_URL` before a reset, or add an endpoint in the dashboard (Model Endpoints).

**Demo credential.** The credential `access_hugo_demo_not_for_production` belongs to Thomas Anderson and is bound and locked to the `Hugo` profile, so it always acts as that profile. Hugo generates its access key and secret key; the seed prints them at the end:

```
Demo credential access_hugo_demo_not_for_production (bound and locked to Hugo; demo only, never for production)
  HUGO_ACCESS_KEY=access_...
  HUGO_SECRET_KEY=secret_...
```

The pair stays the same across `docker compose up -d` runs (the seed keeps a copy in the `seed-state` volume) and changes on every reset. If the saved copy is lost, the seed rotates the credential and prints the new pair. To see the pair again at any time, run `docker compose logs hugo-seed`.

## Connect Claude Code

Take the two keys from the seed output and add Hugo as an HTTP MCP server:

```bash
export HUGO_ACCESS_KEY=access_...
export HUGO_SECRET_KEY=secret_...
claude mcp add --transport http hugo-factory http://127.0.0.1:8810/mcp \
  --header "x-access-key: $HUGO_ACCESS_KEY" \
  --header "x-secret-key: $HUGO_SECRET_KEY"
```

The seed prints this command with the keys filled in. In Claude Code, `/mcp` shows the server and its tools; the `initialize` answer carries the compiled instructions (Agent Smith, the safety rules, and the eight resources). A quick check from the shell:

```bash
claude -p "Use the hugo-factory tools to list the tables in prod-db and count the orders by status." --allowedTools "mcp__hugo-factory"
```

Then try something that needs approval, for example "create the file /tmp/hello on web-01". The call pauses for approval; approve or deny it in the dashboard under Approvals. [MCP_CLIENTS.md](MCP_CLIENTS.md) covers other clients and the stdio relay.

## Try it from the API

Sign in and call the REST API as the demo user:

```bash
TOKEN=$(curl -s -X POST http://127.0.0.1:8810/v1.0/token -H 'Content-Type: application/json' \
  -d '{"tenantId":"default","email":"anderson@hugo.local","password":"HugoDemo2026!"}' | python3 -c 'import json,sys; print(json.load(sys.stdin)["token"])')

curl -s http://127.0.0.1:8810/v1.0/api/resources -H "Authorization: Bearer $TOKEN"

# Dry run: the decision, without executing anything
curl -s -X POST http://127.0.0.1:8810/v1.0/api/tool-invocations/dry-run -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"profile":"Hugo","resource":"prod-db","toolName":"sql_list_tables","arguments":{"resource":"prod-db"}}'

# The same call for real
curl -s -X POST http://127.0.0.1:8810/v1.0/api/tool-invocations -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"profile":"Hugo","resource":"prod-db","toolName":"sql_list_tables","arguments":{"resource":"prod-db"}}'
```

`GET /v1.0/api/agent-profiles/{id}/compiled-instructions?surface=Mcp` returns the text an MCP client receives.

## Grafana

Grafana at http://127.0.0.1:3010 (admin / admin) has the datasources `prometheus`, `tempo`, and `loki` and the Hugo dashboards in the Hugo folder. The factory mounts its own copy of the dashboard JSON from `docker/factory/grafana/`, a mirror of `assets/grafana/` (plan P10.19). After you change a dashboard in `assets/grafana/`, copy it to the mirror; `scripts/check-grafana-mirror.sh` fails while the two differ.

## Files

| Path | Purpose |
|---|---|
| `docker/factory/compose.yaml` | The stack |
| `docker/factory/reset.sh`, `reset.bat` | Destructive reset, seed, and summary |
| `docker/factory/seed/seed.py` | The seed (Python standard library, runs in `python:3.12.15-alpine3.24`) |
| `docker/factory/hugo/hugo.json` | Server settings (a copy of `docker/hugo/hugo.json`) |
| `docker/factory/observability/config.alloy` | Alloy configuration; the same as `docker/observability/config.alloy` except that it reads the `hugo-factory` project's logs |
| `docker/factory/grafana/` | Mirror of `assets/grafana/` |

The remaining observability configuration (Prometheus, Tempo, Loki, Grafana provisioning) is mounted straight from `docker/observability/`, so the two stacks cannot drift apart.
