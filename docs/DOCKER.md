# Running Hugo with Docker

This guide covers Docker Compose. Kubernetes and minikube use the same images and configuration model; see [`KUBERNETES.md`](KUBERNETES.md). Everything here happens in the `docker/` directory.

## What runs

| Service | Image | Host port | Purpose |
|---|---|---|---|
| `hugo-server` | `jchristn77/hugo-server` (built from `src/Hugo.Server/Dockerfile`) | 8800 | REST, MCP (`/mcp`), A2A (`/v1.0/a2a`), OpenAI-compatible API |
| `hugo-dashboard` | `jchristn77/hugo-dashboard` (built from `dashboard/`) | 8801 | The web dashboard; proxies API calls to the server |
| `hugo-prometheus` | `prom/prometheus` | 9090 | Metrics |
| `hugo-tempo` | `grafana/tempo` | 3200 | Traces |
| `hugo-loki` | `grafana/loki` | 3100 | Logs |
| `hugo-alloy` | `grafana/alloy` | none | Receives traces and ships container logs |
| `hugo-grafana` | `grafana/grafana-oss` | 3000 | Dashboards in the Hugo folder |
| `hugo-postgres` | `postgres` (profile `postgres`) | none | Optional PostgreSQL instead of SQLite |

Every host port binds to `127.0.0.1` and can be changed in `docker/.env`. Every third-party image is pinned to a version.

## First start

```bash
cd docker
./start.sh            # Windows: start.bat
```

On first run, `start.sh` copies `.env.example` to `.env` and generates two keys:

- `HUGO_MASTER_KEY` encrypts every secret Hugo stores. Back it up somewhere safe. If you lose it, the secrets in the database cannot be decrypted, and the server refuses to start rather than guess.
- `HUGO_AUTH_SIGNING_KEY` signs session tokens. Changing it signs everyone out.

Plain `docker compose up -d` also works, but without `.env` it falls back to development keys labeled `not-for-production`. The server logs a loud warning when it sees them.

The first boot creates the `default` tenant, the system administrator `admin@hugo.local`, and the tenant administrator `anderson@hugo.local` (Thomas Anderson). Both start with the password `password`. The dashboard prompts you to change it at first login; you can skip the prompt, but change it before anyone else can reach the stack.

## Configuration

The server reads `docker/hugo/hugo.json`, mounted at `/app/config`, and then applies `HUGO_*` environment variables from `compose.yaml` and `.env`. Environment values win. Most settings can also be changed in the dashboard under Settings. Settings marked "restart required" take effect after Restart Server, which exits the process so Docker restarts it.

To use PostgreSQL, set `HUGO_DB_TYPE=Postgresql` (and a real `HUGO_DB_PASSWORD`) in `.env`, then start with the profile:

```bash
docker compose --profile postgres up -d
```

## Reaching your own systems

Hugo connects to your servers, databases, and services from inside its container. Three situations come up often:

- **Services on the Docker host.** Use `host.docker.internal` as the host name. It works on Docker Desktop. On Linux, add `extra_hosts: ["host.docker.internal:host-gateway"]` to the `hugo-server` service in a `compose.override.yaml`.
- **The Docker connector.** To manage the Docker host itself, mount its socket into the server with an override file, then point a Docker resource at `unix:///var/run/docker.sock`:

  ```yaml
  # docker/compose.override.yaml
  services:
    hugo-server:
      volumes:
        - /var/run/docker.sock:/var/run/docker.sock
  ```

  Anyone who can use that resource with write access controls the host, so bind it ReadOnly unless you mean otherwise.
- **Private networks.** Hugo reaches whatever the Docker host can reach. If your databases sit behind a VPN, run Hugo where the VPN is, or attach the container to the right Docker network.

## Data

| Volume | Holds |
|---|---|
| `hugo-data` | SQLite database, development master key file (if used), plugins |
| `hugo-postgres-data` | PostgreSQL data (profile `postgres`) |
| `hugo-prometheus-data`, `hugo-tempo-data`, `hugo-loki-data`, `hugo-grafana-data`, `hugo-alloy-data` | Observability state |

Back up `hugo-data` (or the PostgreSQL database) together with `docker/.env`. One without the other is not a usable backup, because the database's secrets are encrypted with the key in `.env`.

## Updating and resetting

```bash
./update.sh            # pull the latest images, recreate the stack, keep all data
./factory/reset.sh     # delete every volume (data, secrets, metrics, logs) and start fresh
```

`update` is safe to run at any time; database migrations run automatically on startup. `reset` asks you to type RESET before it deletes anything. Both have `.bat` versions for Windows.

## Health

Every Hugo service has a healthcheck, and services start in dependency order. `docker ps` shows the state. The server's health endpoint is `http://127.0.0.1:8800/v1.0/api/health`, and the dashboard's home page shows the external-services card with the Grafana and Prometheus links.
