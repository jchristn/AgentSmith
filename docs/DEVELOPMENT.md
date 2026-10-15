# Developing Hugo

This guide is for people changing Hugo's code. Hugo is deployed with Docker (see the README); nothing here is a supported way to run it for real use.

## Build and test

```bash
dotnet build src/Hugo.sln
dotnet run --project src/Test.Automated -f net10.0
dotnet test src/Test.Xunit -f net10.0
dotnet test src/Test.Nunit -f net10.0
```

The Touchstone suites run on SQLite by default. To run them on PostgreSQL as well, start a throwaway container and point the tests at it:

```bash
docker run -d --name hugo-test-pg -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=hugo_test -p 127.0.0.1:55432:5432 postgres:16.4-alpine
HUGO_TEST_PROVIDERS=sqlite,postgresql \
HUGO_TEST_POSTGRES="Host=127.0.0.1;Port=55432;Database=hugo_test;Username=postgres;Password=postgres" \
  dotnet run --project src/Test.Automated -f net10.0
docker rm -f hugo-test-pg
```

`--suites <prefix>` runs only the suites whose identifier starts with that prefix, for example `--suites connectors.sql`.

## Connector testbed

Connector integration suites run against real targets in containers. They skip unless `HUGO_TESTBED=1` is set.

```bash
docker compose -f docker/testbed/compose.yaml up -d --wait
HUGO_TESTBED=1 dotnet run --project src/Test.Automated -f net10.0 -- --suites testbed
docker compose -f docker/testbed/compose.yaml down -v
```

`docker/testbed/README.md` lists every target and port. Each connector family can be started on its own from its directory.

## Trying a change end to end

Rebuild the images from your working tree and recreate the stack:

```bash
cd docker
docker compose up -d --build
```

The dashboard has a Vite development server for fast iteration on the UI (`cd dashboard && npm install && npm run dev`, which proxies API calls to the Hugo container on port 8800). It is a development aid only.

## Rules

`CLAUDE.md` holds the code style and architecture rules, and `HUGO_PLAN.md` is the build plan and progress tracker. Never use em-dashes anywhere; `scripts/check-em-dash.sh` enforces it.
