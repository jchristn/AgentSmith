# Upgrading Hugo

Upgrading Hugo means running a newer server image against the data you already have. The image migrates the database on startup, refreshes the built-in prompt text it ships, and keeps everything you configured. Most of the work on your side is making a backup you can actually restore, and knowing which two things the upgrade will not do for you: rotate the master key, and overwrite prompt templates you customized.

Hugo is pre-1.0 (`0.1.0-alpha`). Until 1.0, a release may change an API shape, a setting, or a default, and [`CHANGELOG.md`](../CHANGELOG.md) says so when it does. Read the entries between your version and the target before upgrading, especially if you call the REST API from scripts or SDKs.

## Before you upgrade: back up both halves

A Hugo backup has two parts, and one without the other is useless. The database holds every resource, profile, secret, and audit record, with secret values encrypted. `HUGO_MASTER_KEY` decrypts them. Lose the key and the database's secrets are gone; restore an old key against a newer database and the server refuses to start.

In the Compose stack:

```bash
cd docker
cp .env ~/hugo-backup/env-$(date +%Y%m%d)                 # HUGO_MASTER_KEY and HUGO_AUTH_SIGNING_KEY
docker compose stop hugo-server
docker run --rm -v docker_hugo-data:/data -v ~/hugo-backup:/backup alpine \
  tar czf /backup/hugo-data-$(date +%Y%m%d).tgz -C /data .
docker compose start hugo-server
```

The volume name is prefixed with the Compose project name, which is the directory name (`docker`) unless you set `COMPOSE_PROJECT_NAME`; `docker volume ls` shows the real name. The SQLite database is `/app/data/hugo.db` in that volume. If you run on PostgreSQL, take a `pg_dump` of the `hugo` database instead and still keep `.env`. Stopping the server for the copy keeps SQLite consistent; a copy of a live SQLite file can catch a write half done.

Store the `.env` copy somewhere other than the machine running Hugo. It is the one file whose loss cannot be repaired.

## Upgrading the containers

The Compose file pins the Hugo images to a tag (`jchristn77/hugo-server:v0.1.0` and `jchristn77/hugo-dashboard:v0.1.0`). Moving to a new release means moving that tag: pull the new version of the repository (which updates `docker/compose.yaml`), or edit the two `image:` lines yourself. Then:

```bash
cd docker
./update.sh            # Windows: update.bat
```

`update.sh` runs `docker compose pull`, `docker compose down`, `docker compose up -d`, and `docker ps -a`. When `HUGO_DB_TYPE` is `Postgresql` (in the environment or `docker/.env`), it adds `--profile postgres` to each compose command so the PostgreSQL container is updated too. It is non-destructive: named volumes, and therefore your data, survive. Running it without changing the tag pulls the same images again, which is how you pick up a rebuilt image of the same version.

Kubernetes and minikube upgrades follow the same model with a new image tag in the manifests; see [`KUBERNETES.md`](KUBERNETES.md).

## What happens when the new server starts

The server log of the first start after an upgrade tells you everything that changed:

```
[Hugo] Hugo 0.1.0-alpha starting on node hugo-server with settings /app/config/hugo.json
[Hugo] applied 2 schema migrations
[Hugo] database initialized (Sqlite at /app/data/hugo.db, schema version 10)
[Hugo] secrets master key version 1 loaded from Environment
[Integration] seeded or updated 3 Global prompt templates from the built-ins
```

**Schema migrations** are versioned, idempotent, and applied in order at startup; each one is recorded in the `schema_migrations` table. You do not run them by hand. While they run, `GET /v1.0/api/health` reports `status: "migrating"` with `schemaVersion` below `expectedSchemaVersion` and answers 503, so a container orchestrator holds traffic until the database is current. If a migration fails, the server stops with the error in the log and the database stays at the last version that completed.

Migrations run only when `Database.ManageSchema` is true (the default). If your DBA manages the schema, set it to false and apply the migrations from the release yourself; the server then only checks that its tables exist.

**Built-in prompt templates** are refreshed. Every key's Global layer is compared against the new Built-in by hash:

- A Global template nobody edited is updated to the new Built-in, with a version note "Updated to the new built-in default."
- A Global template someone edited is left exactly as it is. Its view reports `defaultUpdatedSinceCustomized: true`, and the dashboard shows a "built-in updated since you customized this" notice with a diff. Review it, and either keep your text or adopt the new default with `POST /v1.0/api/prompt-templates/{key}/reset?layer=Global` (system administrators).
- Tenant overrides are never touched. They sit above the Global layer, so a tenant that customized a key keeps its text even when the default improves underneath it. `GET /v1.0/api/prompt-templates/{key}/diff?layer=Tenant` shows how far it has drifted.

New connector types arrive with their own templates, seeded into the Global layer on that first start. [`PROMPTS.md`](PROMPTS.md) explains the layers in full.

**Seeding** of the default tenant, administrators, roles, and the default profile is skipped for anything that already exists. Built-in roles are refreshed in place with stable IDs, so their grants follow the release.

## Downgrading

Downgrades are not supported. An older server does not know how to undo a newer migration, and it may misread columns it does not expect. If an upgrade goes wrong, stop the stack, restore the data volume and `.env` from the backup you took, set the image tag back, and start again.

## Rotating the master key

Rotation is a separate operation from upgrading, and Hugo never does it on its own. You rotate when the key may have been exposed or on a schedule your policy sets. The process keeps the old key available while every row is re-encrypted, so nothing is unreadable at any point.

1. Generate a new key: `openssl rand -base64 32`.
2. Give the server three values: the new key as `HUGO_MASTER_KEY`, a higher version as `HUGO_MASTER_KEY_VERSION` (2 if you have never rotated), and the old key as `HUGO_MASTER_KEY_PREVIOUS`. If the old key's version is not the new version minus one, also set `HUGO_MASTER_KEY_PREVIOUS_VERSION`. The stock `compose.yaml` passes only `HUGO_MASTER_KEY` through, so put the other variables in a `docker/compose.override.yaml` under `hugo-server.environment`, with their values in `.env`.
3. Restart the server. It finds no canary for the new version, confirms the previous key decrypts an existing canary, records the new canary, and logs how many rows still use the old version.
4. As a system administrator, start the re-encryption:

   ```bash
   curl -s -X POST http://127.0.0.1:8800/v1.0/api/system/master-key/rotate -H "Authorization: Bearer $ADMIN_TOKEN"
   ```

   It re-encrypts secret values and credential secret keys in batches of `Secrets.RotationBatchSize` (200) and returns its progress. Poll `GET /v1.0/api/system/master-key` until `remaining` is 0 and `running` is false. If it stops part way (a restart, a database hiccup), run it again; it only touches rows still on the old version.
5. Remove `HUGO_MASTER_KEY_PREVIOUS` (and `HUGO_MASTER_KEY_PREVIOUS_VERSION`) and restart. Keep `HUGO_MASTER_KEY_VERSION` at its new value from now on.
6. Take a fresh backup, because the old `.env` no longer matches the database.

Rotation is audited as `MasterKeyRotated`. If the server refuses to start during any of this, the message tells you which key or version it could not match; [`TROUBLESHOOTING.md`](TROUBLESHOOTING.md#the-server-will-not-start) walks through each one.

The session signing key (`HUGO_AUTH_SIGNING_KEY`) has no rotation procedure because nothing durable depends on it. Changing it signs everyone out, and that is all.

## After the upgrade

Check three things. `GET /v1.0/api/health` answers 200 with `schemaVersion` equal to `expectedSchemaVersion`. `GET /v1.0/api/info` reports the version you meant to install. And one real round trip works: open a profile's compiled instructions in the dashboard, or have your MCP client list its tools. MCP clients connected before the upgrade will see "Session not found" once and reconnect; that is expected, because MCP sessions live in memory.

Settings added by a release take their defaults until you change them, and [`CONFIGURATION.md`](CONFIGURATION.md) lists every one.
