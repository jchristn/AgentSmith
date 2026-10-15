# Connector testbed

The testbed runs a real target for every connector that has one, so connector suites never need a cloud account (decision D-030 in `HUGO_PLAN.md`). Papertrail and SolarWinds Observability have no self-hostable equivalent; their suites run against in-process stubs in `src/Test.Shared/Connectors/Logging` instead.

```bash
docker compose -f docker/testbed/compose.yaml up -d --wait
HUGO_TESTBED=1 dotnet run --project src/Test.Automated -f net10.0 -- --suites testbed
docker compose -f docker/testbed/compose.yaml down -v
```

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

The Kubernetes and Docker targets run privileged containers and pull images from Docker Hub from inside the container; skip them on machines or CI runners that forbid that (plan risk R-6). The connector suites for those families skip cleanly when the targets are down. Generated material such as SSH keys goes to `docker/testbed/generated/`, which git ignores.
