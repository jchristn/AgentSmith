<p align="center"><img src="assets/logo.png" alt="Hugo" width="160" /></p>

# Hugo

Hugo is a multi-tenant super agent server. You give it access to the systems that run your production stack (SSH hosts, object storage, databases, log services, caches, cloud control planes, clusters, HTTP APIs), group that access into agent profiles, and reach those profiles from any agent or harness over MCP or A2A, from the REST API and SDKs, or by chatting in the dashboard.

The default profile is named Hugo. It calls itself Agent Smith, and it calls you Mr. Anderson.

> Hugo is under active development (`0.1.0-alpha`). The build plan and progress tracker is [`HUGO_PLAN.md`](HUGO_PLAN.md).

## Deploy with Docker

Docker is how Hugo is deployed. Kubernetes and minikube assets are planned in `k8s/` and `minikube/` (Phase 17 of the plan); until they land, Docker Compose is the only supported deployment.

```bash
git clone https://github.com/jchristn/AgentSmith.git
cd AgentSmith/docker
./start.sh            # Windows: start.bat
```

`start.sh` creates `docker/.env` on first run, generates the master key and the session signing key, and starts the stack. Back up `docker/.env`: losing `HUGO_MASTER_KEY` makes every stored secret unreadable. The stack is Hugo, its dashboard, and an observability stack (Prometheus, Tempo, Loki, Alloy, Grafana). It runs on SQLite by default; set `HUGO_DB_TYPE=Postgresql` in `docker/.env` and start with `docker compose --profile postgres up -d` to use PostgreSQL instead.

| Service | URL |
|---|---|
| Dashboard | http://127.0.0.1:8801 |
| REST, MCP (`/mcp`), and A2A (`/v1.0/a2a`) | http://127.0.0.1:8800 |
| Grafana (admin / admin, change it) | http://127.0.0.1:3000 |
| Prometheus | http://127.0.0.1:9090 |

Log in to the dashboard as `anderson@hugo.local` with the password `password` (tenant `default`). Hugo makes you choose a new password before anything else. A system administrator, `admin@hugo.local`, is seeded the same way.

Keeping it current and starting over:

```bash
./update.sh               # pull new images and recreate the stack; data is kept
./factory/reset.sh        # delete everything, including stored secrets, and start fresh
```

Every host port binds to `127.0.0.1` and can be changed in `docker/.env`. See [`docs/DOCKER.md`](docs/DOCKER.md) for networking to your own systems (`host.docker.internal`, the Docker socket for the Docker connector), PostgreSQL, and upgrades.

MCP clients such as Claude Code connect to `http://127.0.0.1:8800/mcp` (or `/mcp/<profile>`) with an access key or a bearer token; see [`docs/MCP_CLIENTS.md`](docs/MCP_CLIENTS.md) for HTTP and stdio configuration.

## Developing Hugo

Building, testing, and the connector testbed are covered in [`docs/DEVELOPMENT.md`](docs/DEVELOPMENT.md). Those instructions are for working on Hugo's source; they are not a way to deploy it.

## License

MIT. See [`LICENSE.md`](LICENSE.md).
