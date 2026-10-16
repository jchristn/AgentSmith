# Connector testbed

The testbed runs a real target for every connector that has one, so connector suites never need a cloud account (decision D-030 in `HUGO_PLAN.md`). Papertrail and SolarWinds Observability have no self-hostable equivalent; their suites run against in-process stubs in `src/Test.Shared/Connectors/Logging` instead.

```bash
docker compose -f docker/testbed/compose.yaml up -d --wait
HUGO_TESTBED=1 dotnet run --project src/Test.Automated -f net10.0 -- --suites testbed.ssh,testbed.storage,testbed.sql
docker compose -f docker/testbed/compose.yaml down -v
```

The default `compose.yaml` starts only wave 1: SSH, object storage, and SQL. SSH client keys are generated on first start by `ssh-keys` into the ignored `generated/ssh` bind volume, then reused. The initializer runs before either SSH daemon and sets key ownership from the checkout so host tests can read them on Linux. `ssh/generate-keys.sh` remains available for manual generation.

To run every family, including the privileged wave 2 targets, use `docker compose -f docker/testbed/compose.all.yaml up -d --wait`, then `HUGO_TESTBED=1 dotnet run --project src/Test.Automated -f net10.0 -- --suites testbed`. Tear down with the same Compose file and `down -v`.

Every target binds to `127.0.0.1` in the `28xxx` port block. Each family directory has its own `compose.yaml` and can be started alone:

| Directory | Targets | Ports |
|---|---|---|
| `ssh/` | OpenSSH target and bastion | 28022, 28023 |
| `storage/` | MinIO (S3), Azurite, fake-gcs-server | 28900, 28901, 28910, 28920 |
| `sql/` | MySQL 8.4, SQL Server 2022, PostgreSQL 16 | 28306, 28433, 28432 |
| `aws/` | LocalStack 4.14.0 (needs the Docker socket for Lambda) | 28566 |
| `redis/` | Redis 7.4 | 28379 |
| `observability/` | Prometheus, Loki, OpenSearch | 28090, 28100, 28992 |
| `kubernetes/` | k3s (privileged) | 28443 |
| `docker/` | Docker-in-Docker (privileged, plain TCP) | 28375 |

The Kubernetes and Docker targets run privileged containers and pull images from Docker Hub from inside the container; skip them on machines or CI runners that forbid that (plan risk R-6). The connector suites for those families skip cleanly when the targets are down. Generated material such as SSH keys goes to `docker/testbed/generated/`, which git ignores. The SSH initializer writes the ed25519, passphrase-protected ed25519, and RSA client keys the SSH suites use, plus the `authorized` directory that the `ssh/` containers mount. It keeps existing keys, so running it again is safe.

## Wave 1 fixtures and CI

`Test.Shared/Connectors/Ssh/SshTestbed.cs` builds password and key settings and secret slots; `Storage/StorageTestbed.cs` builds S3, Azure Blob, and GCS resource contexts; `Sql/SqlTestbedTargets.cs` builds PostgreSQL, MySQL, SQL Server, and temporary seeded SQLite targets. Their live suites skip unless `HUGO_TESTBED=1`. SSH covers the direct target and bastion, storage uses seeded bucket fixtures and cleans up per-run writes, and SQL exercises the seeded shop schema plus engine-specific safety cases. `SqliteShopFile` removes its temporary database on disposal.

`.github/workflows/wave1-testbed.yaml` starts the default stack on every push and pull request and on demand. Separate SQLite and PostgreSQL jobs run the wave 1 testbed suites, connector unit suites, and `db-` provider contracts; PostgreSQL provider persistence uses port 55432, separate from the connector target at 28432. Each job captures failures and always removes its own containers and volumes. The default stack needs no Docker socket or privileged container. SQL Server still requires amd64 emulation on Apple silicon; a host without working emulation cannot pass all wave 1 targets.

Storage host ports can be changed with `HUGO_TESTBED_S3_PORT`, `HUGO_TESTBED_S3_CONSOLE_PORT`, `HUGO_TESTBED_AZURITE_PORT`, and `HUGO_TESTBED_GCS_PORT`. Export the same values when starting Compose and running the tests. For all other targets, check the fixed ports above before starting; do not stop unrelated services to free them.

## MCP and A2A targets

The `mcp` and `a2a-agent` connectors have no container here. Their suites (`connectors.agents.mcp` and `connectors.agents.a2a`) start an in-process Voltaic MCP server and an in-process Voltaic A2A agent and run against those, so they need neither `HUGO_TESTBED` nor Docker. The `http` connector suite (`connectors.http`) works the same way with an in-process Watson stub server.

## Nightly wave 2 run

`.github/workflows/nightly-wave2.yaml` runs every night at 06:30 UTC and on demand (workflow_dispatch). It starts the `redis`, `aws`, `observability`, `kubernetes`, and `docker` families one compose file at a time, runs

```bash
HUGO_TESTBED=1 dotnet run --project src/Test.Automated -f net10.0 -- --providers sqlite \
  --suites testbed.redis,testbed.aws,testbed.observability,testbed.kubernetes,testbed.docker,connectors.redis,connectors.aws,connectors.observability,connectors.kubernetes,connectors.docker,connectors.http,connectors.agents.mcp,connectors.agents.a2a
```

and then stops the targets with `down -v`. The Touchstone results (`results-wave2.json`) are uploaded as an artifact, along with the target logs when a step fails. The same command works locally once the families are up.
