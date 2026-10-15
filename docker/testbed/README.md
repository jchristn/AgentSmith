# Connector testbed

The testbed runs a real target for every connector that has one, so connector suites never need a cloud account (decision D-030 in `HUGO_PLAN.md`). Papertrail and SolarWinds Observability have no self-hostable equivalent; their suites run against in-process stubs in `src/Test.Shared/Connectors/Logging` instead.

```bash
docker compose -f docker/testbed/compose.yaml up -d --wait
HUGO_TESTBED=1 dotnet run --project src/Test.Automated -f net10.0 -- --suites testbed
docker compose -f docker/testbed/compose.yaml down -v
```

Every target binds to `127.0.0.1` in the `28xxx` port block. Each family directory (`ssh/`, `storage/`, `sql/`) has its own `compose.yaml` and can be started alone. Generated material such as SSH keys goes to `docker/testbed/generated/`, which git ignores.
