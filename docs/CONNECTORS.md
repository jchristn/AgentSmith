# Connectors

A connector is how Hugo reaches a system you run: an SSH host, a bucket, a database, a log service, a cache, a cloud account, a cluster, a Docker daemon, an HTTP API, or a metrics and logs backend. This page is the operator's reference for every connector that ships in Hugo today. It covers what each one connects to, every setting and secret slot, the tools it gives agents, how those tools are classified, the limits that bound them, and what to grant the upstream credential.

Everything said here about a connector itself (settings, slots, tools, classification, limits, probes) comes from its code. Where the page describes what the server does around a connector, such as enforcing the access mode matrix, holding approvals, scrubbing output, or running the health sweep, it follows the design in `HUGO_PLAN.md`, because that part of the server is still being built.

The tool-by-tool reference, with parameter schemas and example calls, is [MCP_CONNECTOR_TOOLS.md](MCP_CONNECTOR_TOOLS.md). If you want to write a connector of your own, read [CONNECTOR_AUTHORING.md](CONNECTOR_AUTHORING.md).

## How the pieces fit

Hugo keeps four things apart, and most configuration questions get easier once you see why.

A **connector** is code. It knows how to speak to one family of systems and publishes a descriptor: a type such as `postgresql`, a family that prefixes its tool names (`sql`), a settings schema, secret slots, tools, and default instructions for the model. Connectors are registered when the server starts and never change at run time.

A **resource** is one concrete target configured by an operator: `prod-db` is a PostgreSQL resource with a host, a database, and a row cap. Its settings are validated against the connector's schema before it is saved, and the dashboard renders the form from the same schema, so the tables below are exactly the fields you will see.

A **secret** lives in Hugo's encrypted vault and has a kind (Password, PrivateKey, ApiToken, AccessKeyPair, ServiceAccountJson, Kubeconfig, ConnectionString, Certificate, or Generic) with named fields. A resource does not hold credentials; it binds secrets to the connector's **slots**. One AWS key pair can back several resources, and rotating it is one edit. Connectors read secret fields only for the duration of a call, and Hugo scrubs every bound secret value from tool output as a backstop.

A **binding** attaches a resource to an agent profile under an alias and an **access mode**: ReadOnly, ReadWriteWithApproval, or ReadWrite. The alias is what the model sees. Every tool takes a `resource` argument whose allowed values are the profile's aliases for that family, so `sql_query` with `resource: "prod-db"` goes to PostgreSQL and the same tool with `resource: "legacy-db"` goes to MySQL.

Every call is then classified as Read, Write, or Destructive. Tools with a fixed effect declare it. General-purpose tools (`sql_query`, `ssh_exec`, `redis_command`, `http_request`, and a few others) look at the arguments of each call: the SQL statement, the shell command line, the Redis command, the HTTP method. Hugo compares the effect with the binding's access mode:

| Effect | ReadOnly | ReadWriteWithApproval | ReadWrite |
|---|---|---|---|
| Read | allowed | allowed | allowed |
| Write | denied | needs approval | allowed |
| Destructive | denied | needs approval | needs approval, unless the operator opts out |

A handful of calls carry an always-approve flag and need a human even where destructive approvals have been switched off: terminating EC2 instances, `FLUSHALL` and its relatives on Redis, and deleting a Kubernetes Namespace, Node, CustomResourceDefinition, PersistentVolume, or StorageClass. Classifiers can also reject a call outright, whatever the access mode, for example a shell command that matches a denied pattern or a path outside an HTTP resource's allowed prefixes.

Every classifier fails closed. A SQL statement that cannot be parsed, an unknown Redis command, a shell line with a here-document, and a Kubernetes manifest that does not parse are all Destructive. A false alarm costs one approval click, and that trade is deliberate.

## Least privilege upstream

Hugo's classifiers are the second line of defense. The first is the credential you bind. If an agent should never drop a table, the database login should not be able to, and no amount of statement parsing changes that. The guidance repeats in each section, but the shape is always the same: give each resource its own credential, grant it what the bindings on that resource need, and no more.

- **SSH:** a dedicated user per host that owns nothing it does not need, no sudo rights unless a binding needs them, and `allowSudo` left off.
- **Databases:** a read-only login for resources bound ReadOnly (`pg_read_all_data` on PostgreSQL, `SELECT` plus `PROCESS` on MySQL, `db_datareader` on SQL Server). SQL Server has no read-only session mode, so there the login is the only engine-level guard.
- **Object storage:** list and read permissions only, scoped to the bucket and to the same prefix as `prefixRestriction`.
- **Redis:** an ACL user with only the commands Hugo sends. See [Redis ACLs for Hugo](connectors/redis-acl.md).
- **AWS:** the read-only or operator IAM policy in [IAM policies for the AWS connector](connectors/aws-iam-policies.md), and `allowedServices` narrowed to match.
- **Kubernetes:** a service account bound to the Roles in [Kubernetes RBAC for Hugo](connectors/kubernetes-rbac.md), one Role binding per allowed namespace.
- **Log services and HTTP APIs:** read-only API tokens where the provider offers them. SolarWinds Observability only accepts full-access tokens, so scope the account instead.

The list is not a substitute for the sections below; it is the part people skip.

## Limits, timeouts, and testing a connection

Every call runs under limits the pipeline passes to the connector: output bytes (64 KiB by default), rows (500), and lines (1000), plus a timeout. Connector settings such as `maxRows` or `maxLogLines` can only lower these. When output is cut, the result says so and points at the way to get the rest (a cursor, an offset, a narrower query). Timeouts take the smallest of the binding's, the profile's, and the connector's own setting, and a connector that hits its own timeout returns a `timeout` error, usually with the partial output.

**Test Connection** in the dashboard, and the background health sweep, call each connector's probe. Every probe is read-only and cheap, and most connectors bound their own probe at 15 to 30 seconds on top of the cancellation the server applies. The sections below say what each probe does, because "connected" means something different for an SSH host than for an S3 bucket. Most probes return details beyond the message (server version, detected topology, the identity a cloud credential resolves to) that show on the resource page.

## Catalog

| Type | Display name | Family | Category | Tools |
|---|---|---|---|---|
| `ssh` | SSH | `ssh` | Compute | 8 |
| `azure-blob` | Azure Blob Storage | `storage` | Storage | 8 |
| `gcs` | Google Cloud Storage | `storage` | Storage | 8 |
| `s3` | Amazon S3 | `storage` | Storage | 8 |
| `mysql` | MySQL | `sql` | Database | 16 |
| `postgresql` | PostgreSQL | `sql` | Database | 16 |
| `sqlite` | SQLite | `sql` | Database | 16 |
| `sqlserver` | SQL Server | `sql` | Database | 16 |
| `papertrail` | Papertrail | `logs` | Logging | 6 |
| `solarwinds-observability` | SolarWinds Observability | `logs` | Logging | 5 |
| `redis` | Redis | `redis` | Cache | 13 |
| `aws` | AWS | `aws` | Cloud | 17 |
| `kubernetes` | Kubernetes | `k8s` | Orchestration | 14 |
| `docker` | Docker host | `docker` | Orchestration | 8 |
| `http` | HTTP API | `http` | Http | 2 |
| `elasticsearch` | Elasticsearch / OpenSearch | `es` | Observability | 5 |
| `loki` | Grafana Loki | `loki` | Observability | 4 |
| `prometheus` | Prometheus | `prom` | Observability | 7 |

Wave 2 of the plan also lists connectors for external MCP servers (`mcp`) and remote A2A agents (`a2a-agent`). The `Hugo.Connectors.Agents` project exists but contains no connector yet, so neither type can be configured.

The testbed in `docker/testbed` runs a real target for every connector that has a self-hostable equivalent, all bound to `127.0.0.1` in the `28xxx` port range. Papertrail, SolarWinds Observability, and the HTTP connector are tested against in-process stubs in `src/Test.Shared/Connectors`. See `docker/testbed/README.md` for the commands.

## SSH

The `ssh` connector reaches Linux and Unix hosts over SSH with SSH.NET. It runs classified shell commands, reads and writes files over SFTP, and inspects system health and services. It supports password and private key authentication (with an optional passphrase), one bastion jump host, keepalives, and host key pinning. Sessions are pooled per resource and rebuilt when the resource's configuration changes.

| Key | Kind | Default | Range or values | Meaning |
|---|---|---|---|---|
| `host` | String, required |  |  | Host name or IP address of the server. |
| `port` | Integer | `22` | 1 to 65535 | SSH port. Default 22. |
| `username` | String, required |  |  | Login user. Use a dedicated, least-privilege account. |
| `authMethod` | Enum, required | `PrivateKey` | `Password`, `PrivateKey` | Password uses the 'password' secret slot; PrivateKey uses the 'privateKey' slot (with optional passphrase). |
| `hostKeyFingerprint` | String |  | pattern `^(SHA256:)?[A-Za-z0-9+/]{43}=?$` | Expected SHA256 host key fingerprint as printed by ssh-keygen -lf (SHA256:...). When empty, the first successful connection pins the key it sees (trust on first use). A different key later is refused. |
| `connectTimeoutSeconds` | Integer | `10` | 1 to 120 | How long to wait for TCP connect, key exchange, and authentication. Default 10, minimum 1, maximum 120. |
| `keepAliveSeconds` | Integer | `30` | 0 to 3600 | Interval for SSH keepalives on pooled sessions. 0 disables them. Default 30. |
| `jumpHost` | String |  |  | Optional bastion to tunnel through (like ssh -J). The target host and port are then resolved from the bastion. Bind the 'jumpPrivateKey' or 'jumpPassword' slot. |
| `jumpPort` | Integer | `22` | 1 to 65535 | Bastion SSH port. Default 22. |
| `jumpUsername` | String |  |  | Bastion login user. Defaults to the target username. |
| `jumpHostKeyFingerprint` | String |  | pattern `^(SHA256:)?[A-Za-z0-9+/]{43}=?$` | Expected SHA256 fingerprint of the bastion. Pinned on first use when empty. |
| `commandTimeoutSeconds` | Integer | `60` | 1 to 900 | Default and maximum run time of a command. Default 60, maximum 900. Calls may ask for less, never more. |
| `maxOutputBytes` | Integer | `65536` | 1024 to 16777216 | Bytes of stdout and of stderr kept per command (the end is kept). The call's output limit may be smaller. Default 65536. |
| `maxFileReadBytes` | Integer | `1048576` | 1024 to 67108864 | Most bytes ssh_read_file reads in one call. Default 1 MiB. |
| `maxFileWriteBytes` | Integer | `1048576` | 1 to 67108864 | Largest file ssh_write_file writes. Default 1 MiB. |
| `workingDirectory` | String |  |  | Directory commands start in and relative paths resolve against. Empty means the login directory. |
| `shell` | String | `/bin/bash -lc` |  | Program and options that run ssh_exec commands; the command is passed as one quoted argument. Default /bin/bash -lc. Empty runs commands through the login shell directly. |
| `environment` | StringList |  |  | Variables exported before each ssh_exec command, one NAME=value per line. |
| `allowSudo` | Boolean | `false` |  | When false, any command using sudo, doas, sudoedit, pkexec, or su is rejected. When true, sudo, doas, and sudoedit must be called with -n (non-interactive) or the command is rejected, because there is no terminal to type a password into; the command after sudo is then classified normally. |
| `readOnlyCommandPatterns` | StringList |  |  | Extra regular expressions (one per line, case-insensitive, matched against the whole simple command) that classify a command as Read, for example ^myapp status( .*)?$. They never override a Destructive classification. |
| `destructiveCommandPatterns` | StringList |  |  | Extra regular expressions (one per line, case-insensitive, searched in each simple command) that classify a command as Destructive. |
| `deniedCommandPatterns` | StringList |  |  | Regular expressions (one per line, case-insensitive, searched in the whole command and in each simple command) that reject a command outright, whatever the access mode. |

| Slot | Accepts | Required | Fields read | Meaning |
|---|---|---|---|---|
| `password` | Password | no | `password` | Login password (field 'password'). Required when authMethod is Password. |
| `privateKey` | PrivateKey | no | `privateKey`, optional `passphrase` | OpenSSH or PEM private key (field 'privateKey') with an optional 'passphrase'. Required when authMethod is PrivateKey. |
| `jumpPrivateKey` | PrivateKey | no | `privateKey`, optional `passphrase` | Private key for the jump host. Used when jumpHost is set; takes precedence over jumpPassword. |
| `jumpPassword` | Password | no | `password` | Password for the jump host. Used when jumpHost is set and no jump host key is bound. |

`authMethod` decides which slot is required: `password` for Password, `privateKey` for PrivateKey. The jump host has no method setting of its own. A bound `jumpPrivateKey` wins over `jumpPassword`, and one of them is required whenever `jumpHost` is set. Hugo connects to the bastion, opens a local port forward to `host:port`, and connects to the target through it, so the bastion's sshd must allow TCP forwarding.

**Host keys.** When `hostKeyFingerprint` is empty, the first successful connection records the key it sees in `SHA256:` form and pins it (trust on first use). Any later connection that sees a different key fails with `host_key_mismatch`. The bastion is pinned the same way through `jumpHostKeyFingerprint`. Paste the fingerprint from `ssh-keygen -lf` into the setting if you would rather not trust the first connection.

| Tool | Effect | Purpose |
|---|---|---|
| [`ssh_system_info`](MCP_CONNECTOR_TOOLS.md#ssh_system_info) | Read | Host system overview |
| [`ssh_exec`](MCP_CONNECTOR_TOOLS.md#ssh_exec) | Read, per call | Run shell command |
| [`ssh_service_status`](MCP_CONNECTOR_TOOLS.md#ssh_service_status) | Read | Service status |
| [`ssh_service_control`](MCP_CONNECTOR_TOOLS.md#ssh_service_control) | Destructive | Control a service |
| [`ssh_read_file`](MCP_CONNECTOR_TOOLS.md#ssh_read_file) | Read | Read a file |
| [`ssh_tail_file`](MCP_CONNECTOR_TOOLS.md#ssh_tail_file) | Read | Tail a file |
| [`ssh_list_directory`](MCP_CONNECTOR_TOOLS.md#ssh_list_directory) | Read | List a directory |
| [`ssh_write_file`](MCP_CONNECTOR_TOOLS.md#ssh_write_file) | Write, per call | Write a file |

**How `ssh_exec` is classified.** The command line is parsed by Hugo's own shell parser and split into simple commands at `;`, `&&`, `||`, `|`, `&`, newlines, and subshells. Command substitutions are classified too. The most severe segment decides, and a rejection beats everything. In order:

1. A match on `deniedCommandPatterns`, searched in the whole line and in each simple command, rejects the call.
2. Input that cannot be parsed is Destructive: here-documents, function definitions, unterminated quotes, a trailing backslash, a command name computed at run time (`$cmd`, globs).
3. Built-in destructive commands are Destructive: `rm`, `dd`, `mkfs*`, `fdisk` and friends, `shutdown`, `reboot`, `kill` variants, `truncate`, `userdel`, `umount`, `fsck`, and more. So are overwriting redirections (`>`, `&>`; redirecting to `/dev/null` is harmless), `sed -i`, `tee` to a file, `systemctl stop|restart|disable|mask|kill`, `find -delete`, `rsync --delete`, `tar x`, and a shell reading its script from standard input, which is what `curl ... | sh` amounts to. Patterns in `destructiveCommandPatterns` add to this list.
4. Patterns in `readOnlyCommandPatterns`, matched against the whole simple command, classify it Read. They can turn a Write into a Read; they can never soften a Destructive verdict.
5. Built-in read-only commands are Read: `cat`, `ls`, `grep`, `tail`, `head`, `df`, `du`, `free`, `ps`, `top`, `uptime`, `journalctl`, `systemctl status`, `ss`, `netstat`, `dig`, `docker ps`, `docker logs`, `kubectl get`, and more than two hundred others, many with argument-aware rules (`curl` is Read unless it writes a file, posts data, or uses a non-GET method).
6. Everything else is Write.

Wrappers are unwrapped and the command they run is classified: `command`, `builtin`, `exec`, `env`, `nice`, `nohup`, `setsid`, `time`, `timeout`, `stdbuf`, `ionice`, `xargs`, `watch`, `busybox`, `eval`, `chroot`, `flock`, `taskset`, `numactl`, `chrt`, `prlimit`, `runuser`, `strace`, `ltrace`, `nsenter`, `unshare`, `setpriv`, `systemd-run`, and `sh -c "..."`. Only the wrapper's own leading options count, so `command rm -rf -v /x` is Destructive; `command -v NAME` and `command -V NAME` are Read because they look a name up and run nothing. A few wrappers that run nothing are classified on their own terms (`taskset -p`, `chrt -p`, and `strace -p` act on a running process and are Write).

With `allowSudo` off, any `sudo`, `su`, `doas`, `sudoedit`, or `pkexec` rejects the call. With it on, `sudo`, `doas`, and `sudoedit` must carry `-n` (alone, in a cluster such as `-nu root`, or as `--non-interactive`), and a call without it is rejected: there is no terminal, so a password prompt would only hang until the timeout. The command after `sudo -n` is then classified normally. Grant passwordless sudo for exactly the commands the agent needs.

The other tools are simpler. `ssh_service_control` is always Destructive, and it is rejected when `useSudo` is set but the resource does not allow sudo. `ssh_write_file` is Write, and Destructive with `overwrite: true`; without it, writing over an existing file fails with `already_exists`. Every other SSH tool is Read.

**Limits.** Standard output and standard error each go into a ring buffer of `maxOutputBytes` that keeps the end of the stream, and the result shows the tail with a note saying how much was cut. A command runs for the smallest of `commandTimeoutSeconds`, the call's `timeoutSeconds`, and the pipeline timeout; when it expires, the command is stopped and the partial output comes back with `timeout`. A non-zero exit code is a normal result. `ssh_read_file` reads at most `maxFileReadBytes`; content with NUL bytes or invalid UTF-8 comes back as base64 in byte mode, and line mode refuses it with `invalid_argument` and points at byte mode. `ssh_write_file` refuses content over `maxFileWriteBytes`, and `ssh_list_directory` stops counting at 100,000 entries.

**Test connection** opens a real session (through the bastion if there is one), runs `uname -a`, and reports `Connected to host:port as user (uname)`. It pins the host key on first use, exactly like a tool call would, and returns the fingerprint, the key algorithm, and the server version as details.

**Testbed:** `docker/testbed/ssh` runs `linuxserver/openssh-server` 10.3 as a target on port 28022 and a bastion on 28023, with password and key users and generated ed25519 and RSA keys.

**Known limitations.**

- OpenSSH 9.8 and later enable `PerSourcePenalties` by default. Every connection from Hugo comes from one address, so a run of authentication failures (a wrong key, a rotated password) can make the host refuse Hugo for a while, for every resource that points at it. Fix the credential first; if lockouts keep happening, exempt Hugo's address with `PerSourcePenaltyExemptList` in `sshd_config`. The testbed turns penalties off so its failure tests can run back to back.
- A bastion is configured with the `jump*` settings. Reusing another SSH resource as the bastion (the plan's `jumpResourceName`) is not implemented.
- Trust-on-first-use pins are handed to the server through the `LearnSetting` hook on the resource context. The server does not implement that hook yet, so today a learned fingerprint is not saved and the next new session pins again. Set `hostKeyFingerprint` explicitly for production hosts.

## Object storage

Three connectors share the `storage` family and one tool set, built on Blobject 6.1.2:

- `s3` for Amazon S3 and S3-compatible stores (MinIO, Less3, Ceph, Wasabi),
- `azure-blob` for Azure Blob Storage containers and Azurite,
- `gcs` for Google Cloud Storage buckets and fake-gcs-server.

Each resource is one bucket or container. Settings common to all three:

| Key | Kind | Default | Range or values | Connectors | Meaning |
|---|---|---|---|---|---|
| `prefixRestriction` | String |  |  | all | When set, every key the agent lists, reads, writes, copies, or deletes must start with this prefix (for example 'reports/'). Include the trailing '/' to restrict to a folder. Empty means the whole bucket or container. |
| `maxObjectReadBytes` | Integer | `1048576` | 1024 to 16777216 | all | Largest amount of object content storage_read returns in one call; larger objects are read in ranges. Default 1048576 (1 MiB), minimum 1024, maximum 16777216. |
| `maxListResults` | Integer | `1000` | 1 to 10000 | all | Largest page storage_list returns. Default 1000, minimum 1, maximum 10000. The call's output row limit also applies. |
| `maxScanObjects` | Integer | `100000` | 100 to 10000000 | all | Most objects storage_stats and folder-style storage_list examine in one call before reporting a truncated result. Default 100000, minimum 100, maximum 10000000. |

Provider settings:

| Key | Kind | Default | Range or values | Connectors | Meaning |
|---|---|---|---|---|---|
| `bucket` | String, required |  | pattern `^[A-Za-z0-9][A-Za-z0-9._-]{1,254}$` | `s3`, `gcs` | Bucket name. |
| `region` | String | `us-east-1` | pattern `^[a-z0-9-]{2,32}$` | `s3` | AWS region of the bucket, for example us-east-1. S3-compatible stores usually accept us-east-1. Default us-east-1. |
| `endpoint` | String |  |  | `s3`, `azure-blob` | *s3:* Endpoint URL of an S3-compatible store, for example http://minio.internal:9000. Leave empty for Amazon S3. When set, path-style addressing is used and the URL scheme decides TLS. *azure-blob:* Blob service endpoint. Default https://{accountName}.blob.core.windows.net. For Azurite use http://127.0.0.1:10000/devstoreaccount1. Ignored with a connection string. |
| `useSsl` | Boolean | `true` |  | `s3` | Use HTTPS. Applies to Amazon S3 and to an endpoint given without a scheme. Default true. |
| `accountName` | String |  | pattern `^[a-z0-9]{3,24}$` | `azure-blob` | Storage account name. Required with the accessKey secret; ignored with a connection string. |
| `container` | String, required |  | pattern `^[a-z0-9][a-z0-9-]{1,61}[a-z0-9]$` | `azure-blob` | Container name (3 to 63 lowercase letters, digits, and hyphens). |
| `projectId` | String, required |  | pattern `^[a-z][a-z0-9-]{4,61}[a-z0-9]$` | `gcs` | Google Cloud project ID that owns the bucket. |
| `customEndpoint` | String |  |  | `gcs` | JSON API endpoint for emulators such as fake-gcs-server, for example http://127.0.0.1:4443. '/storage/v1/' is appended when missing. Leave empty for Google Cloud Storage. |

| Slot | Accepts | Required | Fields read | Meaning |
|---|---|---|---|---|
| `accessKeyPair` | AccessKeyPair | no | `accessKeyId`, `secretAccessKey` (a `sessionToken` is refused) | IAM access key (accessKeyId and secretAccessKey) with the least privilege the bindings need, for example s3:ListBucket and s3:GetObject for read-only. Without it, requests are anonymous and only public buckets work. |
| `accessKey` | ApiToken | no | `token` (the base64 account key) | Storage account key (the token field). Account keys grant full access to the account; prefer a dedicated account for agents. |
| `connectionString` | ConnectionString | no | `connectionString` | Storage connection string. Use instead of accountName plus accessKey. Presigned URLs need a connection string that includes AccountKey. |
| `serviceAccountJson` | ServiceAccountJson | yes | `json` (must have `type: service_account`, `client_email`, `private_key`) | Service account key JSON (the json field). Grant the account only the roles the bindings need, for example roles/storage.objectViewer for read-only. |

An S3 resource without `accessKeyPair` makes anonymous requests, which only work on public buckets and cannot presign. Azure takes either `accessKey` (with `accountName`) or `connectionString`, not both. GCS always needs a service account key, even against the emulator, because the key also signs URLs.

| Tool | Effect | Purpose |
|---|---|---|
| [`storage_list`](MCP_CONNECTOR_TOOLS.md#storage_list) | Read | List objects |
| [`storage_get_metadata`](MCP_CONNECTOR_TOOLS.md#storage_get_metadata) | Read | Get object metadata |
| [`storage_read`](MCP_CONNECTOR_TOOLS.md#storage_read) | Read | Read object |
| [`storage_stats`](MCP_CONNECTOR_TOOLS.md#storage_stats) | Read | Summarize objects |
| [`storage_presign_url`](MCP_CONNECTOR_TOOLS.md#storage_presign_url) | Write, per call | Create presigned URL |
| [`storage_write`](MCP_CONNECTOR_TOOLS.md#storage_write) | Write, per call | Write object |
| [`storage_copy`](MCP_CONNECTOR_TOOLS.md#storage_copy) | Write, per call | Copy object |
| [`storage_delete`](MCP_CONNECTOR_TOOLS.md#storage_delete) | Destructive | Delete object |

**Effects.** Before anything else, every tool checks its keys and prefixes. A key with a leading `/`, a backslash, a control character, a `.` or `..` segment, more than 1024 bytes, or one outside `prefixRestriction` is rejected at classification time, so it never reaches an approval card. Listing, metadata, reads, and stats are Read. `storage_write` and `storage_copy` are Write, and Destructive with `overwrite: true`; without it they fail with `already_exists` when the target exists. `storage_delete` is Destructive and takes exactly one key. `storage_presign_url` is Write for `GET`, because a signed URL hands access to someone outside Hugo, and Destructive for `PUT`, because the holder can replace the object. URLs are signed with the provider SDKs (SigV4, SAS, or a GCS V4 signature).

**Limits.** `storage_read` returns at most `maxObjectReadBytes` per call and gives `nextOffset` to continue. `storage_list` pages with a continuation token, and `storage_stats` scans at most `maxScanObjects` objects before reporting lower bounds. `storage_write` accepts up to 16 MiB of content, and `storage_copy`, which reads the object through Hugo rather than copying server-side, up to 256 MiB.

**Test connection** lists the first key under `prefixRestriction` and reports either that key or that the prefix is empty.

**Least privilege.** For S3, `s3:ListBucket` and `s3:GetObject` on the bucket (with an `s3:prefix` condition matching `prefixRestriction`) cover every Read tool; add `s3:PutObject` for writes, copies, and PUT URLs, and `s3:DeleteObject` for deletes. For GCS, `roles/storage.objectViewer` on the bucket is the read-only role. Azure account keys grant the whole account, so give agents a dedicated storage account, or a connection string carrying a narrowly scoped SAS if you do not need presigned URLs.

**Testbed:** `docker/testbed/storage` runs MinIO (port 28900), Azurite 3.35.0 (28910), and fake-gcs-server 1.56.1 behind a small timestamp-fixing proxy (28920), each seeded with a `hugo-testbed` bucket.

**Known limitations.**

- Blobject decides S3 addressing: path-style when `endpoint` is set, virtual-hosted otherwise. There is no `forcePathStyle` setting, and the path part of `endpoint` is dropped.
- S3 cannot use instance roles or the ambient AWS credential chain (there is no `useInstanceRole`), and temporary credentials with a session token are refused.
- Blobject lists flat, so folders and continuation tokens are computed by the connector. Each page re-walks the listing from the start, and paging past roughly `maxScanObjects` keys fails with `scan_limit`. Narrow the prefix instead.
- GCS reads buffer the whole object and refuse objects over 64 MiB.
- Azure presigned URLs need an account key; a connection string without `AccountKey` returns `not_supported`.
- The overwrite check on write and copy is not atomic. Two writers racing for the same key can both succeed.

## SQL databases

Four connectors share the `sql` family: `sqlite` (Microsoft.Data.Sqlite), `mysql` (MySqlConnector), `sqlserver` (Microsoft.Data.SqlClient), and `postgresql` (Npgsql). The crawlers and the statement classifier descend from Tablix, and so does the database intelligence layer described below.

| Key | Kind | Default | Range or values | Connectors | Meaning |
|---|---|---|---|---|---|
| `host` | String |  |  | `postgresql`, `mysql`, `sqlserver` | Server host name or IP address. Required unless a connectionString secret is bound. |
| `port` | Integer | `5432` (postgresql), `3306` (mysql), `1433` (sqlserver) | 1 to 65535 | `postgresql`, `mysql`, `sqlserver` | TCP port. Defaults to the engine's standard port. |
| `database` | String |  |  | `postgresql`, `mysql`, `sqlserver` | Database to connect to. |
| `username` | String |  |  | `postgresql`, `mysql`, `sqlserver` | Login name. A username field on the bound password secret overrides this value. Prefer a least-privilege, read-only login. |
| `schema` | String |  |  | all | *postgresql:* Schema used by catalog tools when none is given. Default public. *mysql:* Schema (database) used by catalog tools when none is given. Defaults to the connection database. *sqlserver:* Schema used by catalog tools when none is given. Default dbo. *sqlite:* Schema used by catalog tools when none is given: main, or the name of an attached database. Default main. |
| `sslMode` | Enum | `Prefer` | `Disable`, `Prefer`, `Require`, `VerifyCA`, `VerifyFull` | `postgresql`, `mysql`, `sqlserver` | Disable: no TLS. Prefer: TLS when the server offers it (default). Require: TLS without certificate validation. VerifyCA and VerifyFull: TLS with certificate validation. |
| `applicationName` | String | `hugo` |  | `postgresql`, `mysql`, `sqlserver` | Name reported to the server so DBAs can identify Hugo sessions. |
| `connectTimeoutSeconds` | Integer | `15` | 1 to 300 | `postgresql`, `mysql`, `sqlserver` | Seconds to wait for a connection. Minimum 1, maximum 300. |
| `commandTimeoutSeconds` | Integer | `30` | 1 to 3600 | all | Seconds a statement may run before it is cancelled on the server. The binding or profile timeout applies when smaller. Minimum 1, maximum 3600. |
| `maxRows` | Integer | `500` | 1 to 100000 | all | Rows returned by sql_query before the result is truncated. The invocation row limit applies when smaller. Minimum 1, maximum 100000. |
| `schemaCacheMinutes` | Integer | `10` | 0 to 1440 | all | Minutes a sql_describe_schema crawl is reused. 0 disables caching. Maximum 1440. |
| `readOnlyEnforcement` | Enum | `Session` | `Session`, `ClassifierOnly` | all | *postgresql:* Session: reads run inside a transaction with SET TRANSACTION READ ONLY, so PostgreSQL itself rejects writes. ClassifierOnly: rely on Hugo's statement classifier alone. *mysql:* Session: reads run inside START TRANSACTION READ ONLY, so MySQL itself rejects writes. ClassifierOnly: rely on Hugo's statement classifier alone. *sqlserver:* SQL Server has no session read-only mode, so both values rely on Hugo's statement classifier. Bind a login that is only a member of db_datareader for read-only use. *sqlite:* Session: reads open the file with Mode=ReadOnly, so SQLite itself rejects writes. ClassifierOnly: rely on Hugo's statement classifier alone. |
| `allowMultipleStatements` | Boolean | `false` |  | all | Allow sql_query to run several statements in one call. The batch is classified by its most severe statement. Default false. |
| `trustServerCertificate` | Boolean | `false` |  | `sqlserver` | Accept the server's TLS certificate without validation. Only for development servers with self-signed certificates. |
| `filename` | String |  |  | `sqlite` | Path to the SQLite database file as seen by the Hugo server. Required unless a connectionString secret is bound. The file is never created. |

| Slot | Accepts | Required | Fields read | Meaning |
|---|---|---|---|---|
| `password` | Password | no | `password`, optional `username` (overrides the setting) | Password for the login. When the secret also carries a username, it overrides the username setting. |
| `connectionString` | ConnectionString | no | `connectionString` | A complete driver connection string. When bound, it overrides the discrete connection settings; Hugo still applies the command timeout, the application name when absent, and read-only enforcement. |

SQLite has only the `connectionString` slot. A bound connection string replaces every discrete connection setting, including host, port, user, password, TLS, and the connect timeout; Hugo still applies the command timeout, the application name when the string has none, and read-only mode on SQLite. On SQL Server, `sslMode` values map to the driver's `Encrypt` option, and `Disable` maps to `Optional`, the same as `Prefer`.

| Tool | Effect | Purpose |
|---|---|---|
| [`sql_list_schemas`](MCP_CONNECTOR_TOOLS.md#sql_list_schemas) | Read | List schemas |
| [`sql_list_tables`](MCP_CONNECTOR_TOOLS.md#sql_list_tables) | Read | List tables |
| [`sql_describe_table`](MCP_CONNECTOR_TOOLS.md#sql_describe_table) | Read | Describe table |
| [`sql_describe_schema`](MCP_CONNECTOR_TOOLS.md#sql_describe_schema) | Read | Describe schema |
| [`sql_query`](MCP_CONNECTOR_TOOLS.md#sql_query) | Read, per call | Run SQL query |
| [`sql_explain`](MCP_CONNECTOR_TOOLS.md#sql_explain) | Read | Explain SQL |
| [`sql_activity`](MCP_CONNECTOR_TOOLS.md#sql_activity) | Read | Database activity |
| [`sql_table_sizes`](MCP_CONNECTOR_TOOLS.md#sql_table_sizes) | Read | Table sizes |
| [`sql_locks`](MCP_CONNECTOR_TOOLS.md#sql_locks) | Read | Locks and blocking |
| [`sql_get_agent_pack`](MCP_CONNECTOR_TOOLS.md#sql_get_agent_pack) | Read | Get agent pack |
| [`sql_list_relationships`](MCP_CONNECTOR_TOOLS.md#sql_list_relationships) | Read | List relationships |
| [`sql_get_intelligence`](MCP_CONNECTOR_TOOLS.md#sql_get_intelligence) | Read | Get database intelligence |
| [`sql_get_context`](MCP_CONNECTOR_TOOLS.md#sql_get_context) | Read | Get durable context |
| [`sql_update_context`](MCP_CONNECTOR_TOOLS.md#sql_update_context) | Read, updates knowledge | Update durable context |
| [`sql_generate_context`](MCP_CONNECTOR_TOOLS.md#sql_generate_context) | Read, updates knowledge, needs a model | Generate durable context |
| [`sql_ask`](MCP_CONNECTOR_TOOLS.md#sql_ask) | Read, needs a model | Ask a question |

**How `sql_query` is classified.** Hugo tokenizes the statement with a dialect-aware lexer (MySQL backticks and `/*! */` executable comments, PostgreSQL dollar quotes and E-strings, T-SQL brackets), strips comments, and classifies by the leading keyword.

- **Read:** `SELECT`, `VALUES`, `TABLE`, `SHOW`, `DESCRIBE`, `EXPLAIN` without `ANALYZE`, and read-only `PRAGMA`s.
- **Write:** `INSERT`, `UPDATE`, `MERGE`, `CREATE`, `GRANT`, `CALL`, `EXEC`, `SET`, `USE`, transaction control, `VACUUM`, `COPY`, `SELECT ... INTO`, and `SELECT ... FOR UPDATE`.
- **Destructive:** `DELETE`, `DROP`, `TRUNCATE`, `ALTER`, `REVOKE`, `DENY`, `RENAME`, `KILL`, `SHUTDOWN`, and `MERGE` with a `DELETE` branch.

The interesting cases are the ones that look like reads. A CTE that modifies data (`WITH x AS (DELETE ... RETURNING *) SELECT ...`) takes the effect of its worst body. `EXPLAIN ANALYZE` takes the effect of the statement it runs. Side-effecting functions such as `pg_terminate_backend` or `xp_cmdshell` raise a `SELECT` to Destructive, and `nextval` or `pg_notify` raise it to Write. Dynamic SQL in `EXEC`, `DO`, `PREPARE`, or `sp_executesql` is classified from its string literals. A T-SQL batch without semicolons is caught too: `SELECT 1 DELETE FROM t` is Destructive. An unknown first keyword or an unparseable statement is Destructive. Empty input and a second statement (unless `allowMultipleStatements` is on, in which case the batch takes its most severe statement) are rejected. `sql_explain` refuses `analyze: true` for anything but a Read statement, and on SQLite and SQL Server it falls back to an estimated plan with a note saying so.

**Read-only enforcement in depth.** When a call classifies as Read, or the binding is ReadOnly, the connector also asks the engine to refuse writes: PostgreSQL runs it in `SET TRANSACTION READ ONLY`, MySQL in `START TRANSACTION READ ONLY`, and SQLite opens the file with `Mode=ReadOnly`. That happens even under a ReadWrite binding, so a misclassified read still cannot write. SQL Server has no session read-only mode; both `readOnlyEnforcement` values rely on the classifier there, which is why the login matters. Setting `ClassifierOnly` turns the engine guard off on the other three.

**Limits.** `sql_query` returns the smaller of `maxRows`, the call's row limit, and its own `maxRows` argument. At the cap it stops reading and cancels the statement on the server, so a runaway `SELECT *` does not keep streaming. The effective timeout is the smaller of `commandTimeoutSeconds` and the call timeout, and a statement that hits it is cancelled on the server. `sql_activity` and `sql_locks` read `pg_stat_activity` and `pg_locks`, `SHOW FULL PROCESSLIST` and `performance_schema.data_locks` with `sys.innodb_lock_waits`, or `sys.dm_exec_requests` and `sys.dm_tran_locks`. On SQLite they return a note saying they do not apply.

**Test connection** opens a read-only session and asks for the server version (`SHOW server_version`, `SELECT VERSION()`, `SERVERPROPERTY('ProductVersion')`, or `sqlite_version()`), then reports the version, the default schema, and whether read-only enforcement is by session or by classifier.

**Least privilege.** On PostgreSQL, grant `pg_read_all_data` (or `CONNECT` and `SELECT` on the schemas) and `pg_read_all_stats` so `sql_activity` sees other sessions. On MySQL, grant `SELECT` on the database, `PROCESS` for the processlist, and `SELECT` on `performance_schema` and `sys` for `sql_locks`. On SQL Server, add the user to `db_datareader` and grant `VIEW SERVER STATE` for activity and locks. For SQLite, make the file read-only to the Hugo process when no binding needs writes.

**Testbed:** `docker/testbed/sql` runs MySQL 8.4 (28306), SQL Server 2022 (28433), and PostgreSQL 16 (28432), each seeded with the same small `shop` schema (customers, products, orders, order_items, a view) plus `legacy_accounts` and `legacy_invoices`, two tables with no declared foreign key so relationship inference has something to find. SQLite suites build a file from the same seed.

### Database intelligence

Crawling tells an agent what columns exist. It does not tell it that `status = 'shipped'` includes refunded orders, or that `legacy_invoices.account_id` joins to `legacy_accounts` even though nobody declared the key. The SQL connectors carry a second layer for that, and the default instructions steer the model through it in this order.

1. **Start with `sql_get_agent_pack`.** One bounded Markdown briefing: how to work with this resource, the saved database context, main entities, relationships, likely metrics and filters, freshness columns, ambiguous terms, summaries of saved table context, gaps, and suggested questions. It is capped so it fits in a prompt alongside everything else.
2. **Read context before answering questions where meaning matters.** `sql_get_context` returns the durable database-level and table-level notes. Each entry shows who wrote it (Operator, Generated, or Agent), its version, and when.
3. **Plan joins with `sql_list_relationships`.** Declared foreign keys come with confidence 1.0. Inferred candidates come from naming (`customer_id` to `customers`, `account_id` to `legacy_accounts`), adjusted for matching types, unique targets, and indexes, and each edge lists the evidence. Treat inferred edges as candidates until a query confirms them.
4. **Query, or ask.** `sql_query` runs SQL the model writes. `sql_ask` takes a plain-language question and does the whole loop itself (below).
5. **Record what was learned.** `sql_update_context` appends a confirmed join path, a business definition, or a correction to the database or table context, so the next conversation starts smarter.

`sql_get_intelligence` is the compact readout behind the pack: entities with their likely role, metrics with natural aggregations, common filters, freshness and tenancy columns, ambiguous terms, and a context quality score from 0 to 100. The score starts at 20, gains up to 20 for saved database context, up to 30 for table context coverage, and up to 20 for relationships, and loses up to 20 for unresolved ambiguous terms. Labels run Strong (85 and up), Good (65), Needs context (40), and Degraded.

Context is stored in Hugo's knowledge store for the resource, never in the database. `sql_generate_context` drafts it with the profile's model from the crawl, statistics, and a few sample rows per table. Columns that look secret are replaced with `(redacted)` and personal data with `(personal data)` before the model sees them, and the sample never leaves the prompt. Generated entries are stored with source Generated for an operator to review; operator and agent entries are kept unless `overwrite` is set. One call can make up to `1 + maxTables` model calls, so give profiles that use it a long timeout. `sql_update_context` refuses content over 4000 characters and content that looks like secrets, credentials, connection strings, more than two email addresses, or pasted result rows, and it can append to an operator's entry but never replace it.

**How `sql_ask` decides.** First it looks for ambiguous terms in the question: latest (recent, newest), active (inactive, enabled), status (state), revenue (sales, income, and weakly total or amount), owner (assignee), and customer (client, account, user). A term is fine when saved context defines it or the question names exactly one matching table or column. Otherwise the tool returns a clarification question and runs nothing. When the user answers, call again with `clarification` or `assumptions`. Next it picks relevant tables (all of them in schemas of 30 or fewer, otherwise by model selection and name scoring, plus up to four related neighbours), asks the model for one statement, and refuses to run anything that does not classify as a single Read. It runs the statement with the resource's limits, retries once with a repaired statement if it fails, and writes an answer from the returned rows only.

The verification envelope tells the caller how far to trust the answer:

| Status | When |
|---|---|
| `Verified` | The query ran, the result was not truncated, and an answer was written from it. |
| `Partial` | The result was truncated, the model answered from the schema alone without SQL, or no answer text was produced. |
| `Blocked` | No usable plan, a statement that is not a single Read, a second failure, a timeout, or a model error. Nothing unsafe ran. |
| `Ambiguous` | A term needs clarification. No SQL ran. |

Every envelope carries the SQL, the classification, the tables, the row count, and evidence rows, so a human can check the work. `sql_ask` and `sql_generate_context` need a model and are hidden from profiles without one; on a call they return `not_supported`.

**Known limitations.**

- The crawl cache lives in the server process and is shared by the discovery and intelligence tools. It is not yet persisted (the plan's `connector_schema_cache` needs a server hook), so a restart recrawls.
- The server has not yet wired in its durable knowledge store. Until it does, connectors fall back to an in-memory store, and context written by agents or generated by the model is lost on restart.
- MySQL refuses `EXPLAIN` of a write inside a read-only transaction, so `sql_explain` of a non-Read statement opens a writable session on MySQL. The statement is compiled, not run.

## Log services

Two connectors share the `logs` family, so `logs_search` and `logs_tail` work the same way on either and route by the `resource` argument. Provider-specific filters go in `providerOptions`; a key the provider does not support returns `invalid_argument` rather than being silently ignored. Both connectors use one shared `HttpClient` and read rate-limit headers directly.

The common rules: every search is bounded in time. `startTime` and `endTime` accept ISO-8601, `now`, or a duration before now (`15m`, `2h`, `1d`), the default window is the last hour, and a window longer than `maxTimeRangeHours` fails with `time_range_exceeded` before any request is sent. `limit` is capped by `maxEvents` and the call's row limit. A 429 returns `rate_limited` with the upstream's `Retry-After`, and Hugo does not retry on its own. Messages longer than 2000 characters are cut.

The upstream API details, with sources and the open questions, are in [Logging connector API notes](connectors/logging-api-notes.md).

### Papertrail

The `papertrail` connector searches, tails, and counts events through the Papertrail HTTP API (`X-Papertrail-Token`), and lists systems, groups, and saved searches.

| Key | Kind | Default | Range or values | Meaning |
|---|---|---|---|---|
| `baseUrl` | String | `https://papertrailapp.com/api/v1` | pattern `^https?://` | Papertrail API base URL. Change it only for a proxy or a test stub. |
| `defaultGroup` | String |  |  | Group ID or name searched when a call names no group or system. Empty searches every system. |
| `defaultSystem` | String |  |  | System ID or name searched when a call names no system. Empty searches every system. |
| `maxEvents` | Integer | `500` | 1 to 10000 | Upper bound on events returned by one logs_search or logs_tail call. Papertrail allows at most 10000. |
| `maxTimeRangeHours` | Integer | `168` | 1 to 8760 | Longest window one search or volume call may cover. Default 168 (one week). |

| Slot | Accepts | Required | Fields read | Meaning |
|---|---|---|---|---|
| `apiToken` | ApiToken | yes | `token` | Papertrail API token from Settings, Profile. The token has the permissions of its user; a read-only member account is recommended. |

| Tool | Effect | Purpose |
|---|---|---|
| [`logs_search`](MCP_CONNECTOR_TOOLS.md#logs_search) | Read | Search logs |
| [`logs_tail`](MCP_CONNECTOR_TOOLS.md#logs_tail) | Read | Tail logs |
| [`logs_volume`](MCP_CONNECTOR_TOOLS.md#logs_volume) | Read | Log volume over time |
| [`logs_list_systems`](MCP_CONNECTOR_TOOLS.md#logs_list_systems) | Read | List log systems |
| [`logs_list_groups`](MCP_CONNECTOR_TOOLS.md#logs_list_groups) | Read | List log groups |
| [`logs_list_saved_searches`](MCP_CONNECTOR_TOOLS.md#logs_list_saved_searches) | Read | List saved log searches |

Every Papertrail tool is Read. `logs_search` calls `events/search.json` and pages backward with `max_id`; each page holds the newest events not yet returned, listed oldest first. Its cursor carries the original window, query, and filters, and a cursor call that also sends `query`, `startTime`, `endTime`, or `providerOptions` fails with `invalid_argument` instead of quietly ignoring them; only `limit` may change. A group name (rather than a numeric ID) costs one extra `groups.json` lookup. `logs_volume` pages through matching events (up to `maxScanEvents`, at most 100,000) and counts them into equal buckets; when it runs out of budget, the oldest buckets are marked incomplete. Use a specific query to keep the scan small.

**Test connection** calls `systems.json` and reports how many systems the token can see. **Testbed:** an in-process stub (`PapertrailStub`) replays the recorded response shapes, including paging flags and 429 responses.

**Least privilege.** The API token carries its user's permissions. Create a member account without admin rights for Hugo and use its token.

### SolarWinds Observability

The `solarwinds-observability` connector searches and tails logs, lists and reads entities, and queries metrics through the SolarWinds Observability public REST API with a bearer token. The API host is `api.<dataCenter>.cloud.solarwinds.com/v1` unless `baseUrl` overrides it.

| Key | Kind | Default | Range or values | Meaning |
|---|---|---|---|---|
| `dataCenter` | Enum | `na-01` | `na-01`, `na-02`, `eu-01`, `ap-01` | Region of the SolarWinds Observability organization. The API host is api.<dataCenter>.cloud.solarwinds.com. |
| `baseUrl` | String |  | pattern `^https?://` | Full API base URL including /v1. Overrides dataCenter; use only for a proxy or a test stub. |
| `defaultGroup` | String |  |  | Log group searched when a call names none. Empty searches every group. |
| `maxEvents` | Integer | `500` | 1 to 10000 | Upper bound on events returned by one logs_search or logs_tail call. |
| `maxTimeRangeHours` | Integer | `168` | 1 to 8760 | Longest window one log search or metric query may cover. Default 168 (one week). |

| Slot | Accepts | Required | Fields read | Meaning |
|---|---|---|---|---|
| `apiToken` | ApiToken | yes | `token` | SolarWinds Observability API token (Settings, API Tokens). The public API requires a full-access token; ingestion tokens are rejected. |

| Tool | Effect | Purpose |
|---|---|---|
| [`logs_search`](MCP_CONNECTOR_TOOLS.md#logs_search) | Read | Search logs |
| [`logs_tail`](MCP_CONNECTOR_TOOLS.md#logs_tail) | Read | Tail logs |
| [`swo_list_entities`](MCP_CONNECTOR_TOOLS.md#swo_list_entities) | Read | List SolarWinds Observability entities |
| [`swo_get_entity`](MCP_CONNECTOR_TOOLS.md#swo_get_entity) | Read | Get SolarWinds Observability entity |
| [`swo_query_metric`](MCP_CONNECTOR_TOOLS.md#swo_query_metric) | Read | Query SolarWinds Observability metric |

Every tool is Read. `logs_search` calls `/v1/logs` and follows the API's `nextPage` token; as with Papertrail, a cursor call refuses new values for the query, window, or filters. `providerOptions` accepts `group`, `entityId`, and `direction`. `logs_tail` tracks the newest timestamp it has returned and asks for events since then. `swo_query_metric` calls `/v1/metrics/{name}/measurements`, scoped by `entityId` or a SolarWinds filter expression, and shows at most the last 60 points per grouping in text.

**Test connection** calls `/v1/entities?pageSize=1` and reports the base URL and whether any entity is visible. **Testbed:** an in-process stub (`SwoStub`).

**Known limitations.** These come from the API itself as much as from the connector, and the API notes have the detail.

- There is no `swo_list_alerts`. The public API (OpenAPI 1.0.19) has no alert listing; alerts appear to be reachable only through GraphQL, which is out of scope.
- `logs_volume` and the `logs_list_*` tools are Papertrail only.
- The API documents `direction` inconsistently, so the connector passes it through and otherwise relies on the API default (backward). Events come back in the API's order for that direction, and a cursor fetches the next page in the same direction.
- Rate limits, 429 behavior, and the maximum `pageSize` are undocumented. The connector caps pages with `maxEvents`.
- The tail has no server-side "newer than" token. Events that share the last timestamp can repeat, and when more than `limit` events arrive between calls, the tail skips some.
- The public API requires a full-access token; ingestion tokens are rejected. There is no read-only token to give Hugo.

## Redis

The `redis` connector works with Redis and Redis-compatible servers through StackExchange.Redis (RESP2), in standalone, Sentinel, or Cluster topologies, with or without TLS. One pooled connection serves each resource.

| Key | Kind | Default | Range or values | Meaning |
|---|---|---|---|---|
| `endpoints` | StringList, required |  |  | One host:port per line (IPv6 as [address]:port). Standalone: the primary. Cluster: one or more seed nodes. Sentinel: the Sentinel nodes (default port 26379). Default port 6379. |
| `clusterMode` | Enum | `Auto` | `Auto`, `Standalone`, `Cluster`, `Sentinel` | Auto detects standalone or cluster. Sentinel resolves the current primary of sentinelServiceName through the Sentinel endpoints. |
| `sentinelServiceName` | String |  |  | Primary set name monitored by Sentinel (for example mymaster). Required when the topology is Sentinel. |
| `database` | Integer | `0` | 0 to 10000 | Database index. Default 0. Redis Cluster only has database 0. |
| `username` | String |  |  | Redis 6+ ACL user. Empty uses the default user (password only). A dedicated user with only the commands Hugo needs is recommended. |
| `useSsl` | Boolean | `false` |  | Connect with TLS (rediss). Required by most managed Redis services. |
| `sslHost` | String |  |  | Host name expected on the server certificate when it differs from the endpoint host. |
| `verifyTls` | Boolean | `true` |  | Verify the server certificate. Turn off only for test servers with self-signed certificates. |
| `caCertificate` | Text |  |  | PEM certificate of a private certificate authority that signed the server certificate. |
| `connectTimeoutSeconds` | Integer | `5` | 1 to 60 | How long to wait for a connection. Default 5. |
| `commandTimeoutSeconds` | Integer | `30` | 1 to 600 | Client-side limit on one command, a backstop to the tool call timeout. Default 30. |
| `clientName` | String | `hugo` |  | Name shown in CLIENT LIST for Hugo's connection. Default hugo. |
| `maxKeysScanned` | Integer | `10000` | 100 to 1000000 | Most keys one redis_scan or redis_bigkeys call examines before it stops and returns a cursor. Default 10000. |
| `maxValueBytes` | Integer | `65536` | 64 to 16777216 | Values longer than this are cut when read, and redis_set refuses larger values. Default 65536. |
| `deniedCommands` | StringList |  |  | Extra commands redis_command rejects outright, one per line, optionally with a subcommand (for example CONFIG SET). They are added to the built-in denial of KEYS, never substituted for it. Default empty. |
| `allowKeysCommand` | Boolean | `false` |  | KEYS blocks the server while it walks every key, so redis_command rejects it unless this is on. When on, KEYS is Read (unless deniedCommands lists it). Leave it off and use redis_scan. Default false. |
| `readOnlyCommands` | StringList |  |  | Module or custom commands that only read (for example JSON.GET or FT.SEARCH), one per line. They are classified as Read instead of Destructive. Built-in commands keep their classification. |

| Slot | Accepts | Required | Fields read | Meaning |
|---|---|---|---|---|
| `password` | Password | no | `password`, optional `username` (used when the setting is empty) | Redis password (field 'password'; an optional 'username' field is used when the username setting is empty). Leave unbound for servers without authentication. |

| Tool | Effect | Purpose |
|---|---|---|
| [`redis_info`](MCP_CONNECTOR_TOOLS.md#redis_info) | Read | Redis server info |
| [`redis_scan`](MCP_CONNECTOR_TOOLS.md#redis_scan) | Read | Scan Redis keys |
| [`redis_get`](MCP_CONNECTOR_TOOLS.md#redis_get) | Read | Read a Redis key |
| [`redis_ttl`](MCP_CONNECTOR_TOOLS.md#redis_ttl) | Read | Redis key TTLs |
| [`redis_memory_usage`](MCP_CONNECTOR_TOOLS.md#redis_memory_usage) | Read | Redis key memory usage |
| [`redis_slowlog`](MCP_CONNECTOR_TOOLS.md#redis_slowlog) | Read | Redis slow log |
| [`redis_client_list`](MCP_CONNECTOR_TOOLS.md#redis_client_list) | Read | Redis clients |
| [`redis_latency`](MCP_CONNECTOR_TOOLS.md#redis_latency) | Read | Redis latency monitor |
| [`redis_bigkeys`](MCP_CONNECTOR_TOOLS.md#redis_bigkeys) | Read | Find big Redis keys |
| [`redis_set`](MCP_CONNECTOR_TOOLS.md#redis_set) | Write | Set a Redis string |
| [`redis_expire`](MCP_CONNECTOR_TOOLS.md#redis_expire) | Write | Set a Redis key TTL |
| [`redis_delete`](MCP_CONNECTOR_TOOLS.md#redis_delete) | Destructive | Delete Redis keys |
| [`redis_command`](MCP_CONNECTOR_TOOLS.md#redis_command) | Read, per call | Run a Redis command |

**Effects.** The purpose-built tools have fixed effects: nine reads, `redis_set` and `redis_expire` as Write, and `redis_delete` as Destructive with the always-approve flag. `redis_delete` takes up to 100 exact key names and never expands patterns, so find keys with `redis_scan` first.

`redis_command` is the escape hatch, and it is classified from a table covering every Redis 7 command and its subcommands. Reads (`GET`, `HGETALL`, `XRANGE`, `TYPE`, `INFO`, `CONFIG GET`, `CLIENT LIST`, `OBJECT`, `XINFO`, `EVAL_RO`) are Read. Writes (`SET`, `HSET`, `INCR`, `EXPIRE`, `LPOP`, `XADD`, `SCRIPT LOAD`, `BGSAVE`) are Write. Deletes and anything that discards data or changes the server (`DEL`, `UNLINK`, `HDEL`, `RENAME`, `CONFIG SET`, `CLIENT KILL`, `ACL SETUSER`, `MODULE LOAD`) are Destructive, and so are `EVAL`, `EVALSHA`, and `FCALL`, because Hugo cannot see what a script does. Whole-key deletes (`DEL`, `UNLINK`, `GETDEL`), `FLUSHALL`, `FLUSHDB`, `SHUTDOWN`, `SWAPDB`, `DEBUG`, `REPLICAOF`, `FAILOVER`, `SCRIPT FLUSH`, `FUNCTION FLUSH`, and the cluster and sentinel failover commands carry the always-approve flag, so a human approves them even on a ReadWrite binding that has opted out of approvals for Destructive calls. That follows the plan's rule that Redis deletes and flushes always require approval. Deletes inside a value (`HDEL`, `SREM`, `ZREM`, `XDEL`) are Destructive without the flag. Option-dependent commands are handled too: `SORT ... STORE` is Write, and `COPY` or `RESTORE` with `REPLACE` is Destructive.

Some commands are rejected outright because they would hold or reconfigure the shared connection: blocking pops and `WAIT`, `XREAD` with `BLOCK`, `SUBSCRIBE` and its variants, `MONITOR`, `MULTI`/`EXEC`/`WATCH`, `SELECT`, `AUTH`, `HELLO`, and `CLIENT TRACKING`. `KEYS` and the commands in `deniedCommands` are rejected before the table is consulted. Unknown commands, including module commands, are Destructive unless you list them in `readOnlyCommands`, which can only promote unknown commands to Read and never changes a built-in classification.

`deniedCommands` only ever adds to the deny list. `KEYS` stays denied whatever you put there, and the one way to allow it is `allowKeysCommand`. If a command is in `deniedCommands` and `allowKeysCommand` is also on, the explicit denial wins, so listing `KEYS` there keeps it denied.

**Limits.** `redis_scan` and `redis_bigkeys` walk the keyspace with `SCAN`, never `KEYS`, and stop after `maxKeysScanned` keys with a cursor to continue. An empty page with a cursor only means nothing matched yet. Values are cut at `maxValueBytes` on read, and `redis_set` refuses larger values. `redis_bigkeys` measures a sample with `MEMORY USAGE`; it is not a census. Every result is scrubbed of the bound password, so `CONFIG GET requirepass` shows `[redacted]`.

**Test connection** runs `PING` and `INFO server` on the first primary and reports the version, the detected mode (standalone, sentinel, or cluster), the number of primaries, and a warning when `clusterMode` disagrees with what the server says it is.

**Least privilege.** Give Hugo its own ACL user. [Redis ACLs for Hugo](connectors/redis-acl.md) has a read-only user that covers every Read tool and an operator user for the write tools, with the commands each tool sends.

**Testbed:** `docker/testbed/redis` runs Redis 7.4.8 on port 28379 with a password, a read-only ACL user (`hugo-reader`), and seeded keys of every type.

**Known limitations.** Module key types cannot be read by `redis_get`; use `redis_command` with the module's read command and list it in `readOnlyCommands`. Redis Cluster has only database 0.

## AWS

The `aws` connector covers the AWS control plane for one account and one region through the AWS SDK for .NET: identity, EC2 and Auto Scaling, CloudWatch metrics, alarms, and Logs, ECS, RDS, Lambda, Elastic Load Balancing v2, Route 53, SQS, and SNS. It can start, stop, reboot, and terminate instances, scale or redeploy ECS services, and invoke Lambda functions, all behind the access mode.

| Key | Kind | Default | Range or values | Meaning |
|---|---|---|---|---|
| `region` | String, required | `us-east-1` | pattern `^[a-z]{2}(-[a-z0-9]+)+-[0-9]{1,2}$` | AWS region every tool uses, for example us-east-1. Default us-east-1. Bind one resource per region. |
| `useInstanceRole` | Boolean | `false` |  | When true, credentials come from the Hugo server's environment (environment variables, shared profile, ECS task role, or EC2 instance role) instead of the accessKeyPair secret. Default false. |
| `assumeRoleArn` | String |  | pattern `^arn:aws[a-z-]*:iam::[0-9]{12}:role/.+$` | Optional IAM role ARN. The base credentials call sts:AssumeRole and every tool uses the temporary role credentials, refreshed before they expire. Use this for cross-account access. |
| `externalId` | String |  |  | External ID required by the role's trust policy (confused deputy protection). Only used with a role to assume. |
| `sessionName` | String | `hugo` | pattern `^[\w+=,.@-]{2,64}$` | Session name recorded in CloudTrail for assumed role calls. Default hugo. |
| `assumeRoleDurationSeconds` | Integer | `3600` | 900 to 43200 | Lifetime of assumed role credentials. Default 3600, minimum 900, maximum 43200 (the role's maximum session duration also applies). |
| `allowedServices` | StringList |  |  | Services tools may call, one per line: ec2, autoscaling, cloudwatch, logs, ecs, rds, lambda, elb, route53, sqs, sns. Empty allows every service. Tools for other services are refused. aws_caller_identity (sts) is always allowed. |
| `endpointOverride` | String |  |  | Optional URL used for every service instead of the public AWS endpoints, for example http://127.0.0.1:4566 for LocalStack. Leave empty for AWS. |
| `maxResults` | Integer | `50` | 1 to 1000 | Rows a list or describe tool asks for when the call does not say, clamped to each API's range and to the call's row limit. Default 50. |
| `maxRetries` | Integer | `2` | 0 to 10 | Retries the AWS SDK makes for throttling and transient errors before a tool reports rate_limited. Default 2. |
| `defaultLookbackMinutes` | Integer | `60` | 1 to 20160 | Window metric and log tools use when a call gives no start time. Default 60. |
| `maxTimeRangeHours` | Integer | `168` | 1 to 2160 | Longest window a metric, log, or RDS event call may cover; longer calls fail with time_range_exceeded. Logs Insights bills by bytes scanned. Default 168 (7 days). |
| `logsQueryTimeoutSeconds` | Integer | `30` | 1 to 300 | Longest aws_logs_insights_query waits for a query before returning its ID for a later poll. Default 30. |
| `logsMaxEvents` | Integer | `200` | 1 to 10000 | Most events aws_logs_tail returns. The call's line limit also applies. Default 200. |
| `lambdaMaxPayloadBytes` | Integer | `65536` | 2 to 6291456 | Largest request payload aws_lambda_invoke sends. Default 65536; AWS allows 6291456 for synchronous calls. |

| Slot | Accepts | Required | Fields read | Meaning |
|---|---|---|---|---|
| `accessKeyPair` | AccessKeyPair | no | `accessKeyId`, `secretAccessKey`, optional `sessionToken` | IAM access key: accessKeyId and secretAccessKey, plus sessionToken for temporary credentials. Grant only the actions the bindings need; see docs/connectors/aws-iam-policies.md for read-only and operator policies. Not needed when useInstanceRole is on. |

**Credentials.** Bind `accessKeyPair`, or turn on `useInstanceRole` to use the Hugo server's own credentials (environment variables, shared profile, ECS task role, or EC2 instance role), not both. With `assumeRoleArn` set, the base credentials call `sts:AssumeRole` (with `externalId` when the role's trust policy requires one) and every tool uses the temporary credentials, which Hugo refreshes five minutes before they expire. `endpointOverride` sends every service, STS included, to one URL, which is how the testbed points at LocalStack.

| Tool | Effect | Purpose |
|---|---|---|
| [`aws_caller_identity`](MCP_CONNECTOR_TOOLS.md#aws_caller_identity) | Read | Confirm AWS account and identity |
| [`aws_ec2_describe_instances`](MCP_CONNECTOR_TOOLS.md#aws_ec2_describe_instances) | Read | Describe EC2 instances |
| [`aws_ec2_instance_action`](MCP_CONNECTOR_TOOLS.md#aws_ec2_instance_action) | Destructive, per call | EC2 instance action |
| [`aws_autoscaling_describe_groups`](MCP_CONNECTOR_TOOLS.md#aws_autoscaling_describe_groups) | Read | Describe Auto Scaling groups |
| [`aws_cloudwatch_get_metric_data`](MCP_CONNECTOR_TOOLS.md#aws_cloudwatch_get_metric_data) | Read | Get CloudWatch metric data |
| [`aws_cloudwatch_list_alarms`](MCP_CONNECTOR_TOOLS.md#aws_cloudwatch_list_alarms) | Read | List CloudWatch alarms |
| [`aws_logs_insights_query`](MCP_CONNECTOR_TOOLS.md#aws_logs_insights_query) | Read | Run a CloudWatch Logs Insights query |
| [`aws_logs_tail`](MCP_CONNECTOR_TOOLS.md#aws_logs_tail) | Read | Tail CloudWatch log events |
| [`aws_ecs_describe_services`](MCP_CONNECTOR_TOOLS.md#aws_ecs_describe_services) | Read | Describe ECS services |
| [`aws_ecs_update_service`](MCP_CONNECTOR_TOOLS.md#aws_ecs_update_service) | Write, per call | Update ECS service |
| [`aws_rds_describe`](MCP_CONNECTOR_TOOLS.md#aws_rds_describe) | Read | Describe RDS |
| [`aws_lambda_list_functions`](MCP_CONNECTOR_TOOLS.md#aws_lambda_list_functions) | Read | List Lambda functions |
| [`aws_lambda_invoke`](MCP_CONNECTOR_TOOLS.md#aws_lambda_invoke) | Write | Invoke Lambda function |
| [`aws_elb_target_health`](MCP_CONNECTOR_TOOLS.md#aws_elb_target_health) | Read | ELB target health |
| [`aws_route53_list_records`](MCP_CONNECTOR_TOOLS.md#aws_route53_list_records) | Read | List Route 53 records |
| [`aws_sqs_queue_attributes`](MCP_CONNECTOR_TOOLS.md#aws_sqs_queue_attributes) | Read | SQS queue attributes |
| [`aws_sns_list_topics`](MCP_CONNECTOR_TOOLS.md#aws_sns_list_topics) | Read | List SNS topics |

**Effects.** Every tool belongs to a service, and `allowedServices` is checked twice: a tool for a service outside the list is rejected at classification, and refused again at execution. `aws_caller_identity` is always allowed. Most tools are Read. `aws_ec2_instance_action` is classified by action: `start` is Write, `stop` and `reboot` are Destructive, and `terminate` is Destructive with the always-approve flag. `aws_ecs_update_service` is Write, and Destructive with `desiredCount: 0`, because scaling to zero takes the service down. `aws_lambda_invoke` is Write, since a function can change anything; its payload must be valid JSON under `lambdaMaxPayloadBytes`.

**Limits.** List tools take `maxResults` (default from the resource) clamped to each API's range and to the call's row limit, and page with `nextToken`. Metric and log tools use `defaultLookbackMinutes` when no start is given and refuse windows longer than `maxTimeRangeHours`. `aws_logs_insights_query` waits up to `logsQueryTimeoutSeconds` and, if the query is still running, returns its `queryId` so the next call can resume rather than start (and pay for) a new one. `aws_logs_tail` returns at most `logsMaxEvents`.

**Test connection** validates the settings, creates a session (assuming the role if there is one), calls `sts:GetCallerIdentity`, and reports `Authenticated to account ... as arn in region`.

**Least privilege.** [IAM policies for the AWS connector](connectors/aws-iam-policies.md) lists the IAM actions behind every tool and gives a read-only policy, a tag-scoped operator policy, and a cross-account role with an external ID. Narrow `allowedServices` to the same services the policy grants, so the model never sees tools that would fail.

**Testbed:** `docker/testbed/aws` runs LocalStack 4.14.0 on port 28566 with seeded EC2 instances, CloudWatch metrics and alarms, a log group, SQS queues, an SNS topic, a Route 53 zone, and a Lambda function. LocalStack's community edition does not emulate ECS, RDS, Elastic Load Balancing v2, or Auto Scaling, so those tools are tested against a recorded wire-protocol stub (`AwsStubServer`) instead.

**Known limitations.** The tools cover the services listed and nothing else; there is no generic "call any API" tool.

## Kubernetes

The `kubernetes` connector works with any conformant cluster through KubernetesClient. It lists, reads, and describes any kind including custom resources, reads events, logs, and metrics, follows and restarts rollouts, scales, applies manifests with server-side apply, deletes, execs, and cordons nodes, all inside a namespace allow list.

| Key | Kind | Default | Range or values | Meaning |
|---|---|---|---|---|
| `context` | String |  |  | Kubeconfig context to use. Empty uses the kubeconfig's current-context. |
| `serverUrl` | String |  | pattern `^https?://` | Required with token authentication (for example https://10.0.0.1:6443). With a kubeconfig, overrides the context's server when set. |
| `insecureSkipTlsVerify` | Boolean | `false` |  | Accept any API server certificate. Only for disposable test clusters. Default false. |
| `requestTimeoutSeconds` | Integer | `30` | 1 to 600 | Timeout of each API request. Default 30, minimum 1, maximum 600. |
| `allowedNamespaces` | StringList |  |  | Namespaces this resource may read and change, one per line. Empty allows every namespace. Enforced on every call. |
| `defaultNamespace` | String |  | pattern `^[a-z0-9]([-a-z0-9]*[a-z0-9])?$` | Namespace used when a call names none. Empty uses the first allowed namespace, else default. Must be in allowedNamespaces when that is set. |
| `clusterScopedReads` | Boolean | `true` |  | Allow reading cluster-scoped kinds such as nodes, namespaces, and CRDs. Default true. Namespace objects outside allowedNamespaces stay hidden. |
| `allowClusterScoped` | Boolean | `false` |  | Allow changes to cluster-scoped kinds: cordoning nodes, and applying or deleting namespaces, cluster roles, CRDs, and storage classes. Default false. |
| `destructiveKinds` | StringList |  |  | Extra kinds whose apply is always Destructive, one per line. They are added to the built-in list, which always applies: Namespace, Node, CustomResourceDefinition, APIService, ClusterRole, ClusterRoleBinding, Role, RoleBinding, ServiceAccount, PersistentVolume, PersistentVolumeClaim, StorageClass, NetworkPolicy, ResourceQuota, LimitRange, PriorityClass, MutatingWebhookConfiguration, ValidatingWebhookConfiguration, ValidatingAdmissionPolicy, ValidatingAdmissionPolicyBinding. |
| `fieldManager` | String | `hugo` |  | Field manager name for server-side apply and patches. Default hugo. |
| `maxLogLines` | Integer | `500` | 1 to 100000 | Most lines k8s_logs returns. Default 500. |
| `maxObjects` | Integer | `200` | 1 to 5000 | Most objects k8s_list returns in one call (page with continue). Default 200. |
| `execTimeoutSeconds` | Integer | `60` | 1 to 900 | Default and maximum run time of k8s_exec. Default 60. |

| Slot | Accepts | Required | Fields read | Meaning |
|---|---|---|---|---|
| `kubeconfig` | Kubeconfig | no | `kubeconfig` | A kubeconfig with inline credentials (token or client-certificate-data). Exec and auth-provider plugins and file references are refused. Takes precedence over serviceAccountToken. |
| `serviceAccountToken` | ApiToken | no | `token` | A bearer token (field 'token'), usually a service account token. Needs the serverUrl setting. |
| `certificateAuthority` | Certificate | no | `certificate` (or `caCertificate`) | PEM CA certificate of the API server (field 'certificate'), used with token authentication. |

**Credentials.** A bound `kubeconfig` wins. Otherwise bind `serviceAccountToken` with `serverUrl`, plus `certificateAuthority` unless the API server's certificate chains to a public CA. Kubeconfigs must carry their credentials inline (`token`, or `client-certificate-data` and `client-key-data`). Hugo refuses `exec` and `auth-provider` plugins and file references (`certificate-authority`, `client-certificate`, `client-key` paths), because honouring them would run programs or read files on the Hugo server. That rules out cloud kubeconfigs that call `aws eks get-token` or `gke-gcloud-auth-plugin`; create a service account token for Hugo instead, as described in the RBAC guide.

| Tool | Effect | Purpose |
|---|---|---|
| [`k8s_list`](MCP_CONNECTOR_TOOLS.md#k8s_list) | Read | List Kubernetes objects |
| [`k8s_get`](MCP_CONNECTOR_TOOLS.md#k8s_get) | Read | Get a Kubernetes object |
| [`k8s_describe`](MCP_CONNECTOR_TOOLS.md#k8s_describe) | Read | Describe a Kubernetes object |
| [`k8s_events`](MCP_CONNECTOR_TOOLS.md#k8s_events) | Read | List Kubernetes events |
| [`k8s_logs`](MCP_CONNECTOR_TOOLS.md#k8s_logs) | Read | Read container logs |
| [`k8s_top`](MCP_CONNECTOR_TOOLS.md#k8s_top) | Read | Show pod or node resource usage |
| [`k8s_rollout_status`](MCP_CONNECTOR_TOOLS.md#k8s_rollout_status) | Read | Check rollout status |
| [`k8s_rollout_history`](MCP_CONNECTOR_TOOLS.md#k8s_rollout_history) | Read | Show rollout history |
| [`k8s_rollout_restart`](MCP_CONNECTOR_TOOLS.md#k8s_rollout_restart) | Write | Restart a workload |
| [`k8s_scale`](MCP_CONNECTOR_TOOLS.md#k8s_scale) | Write, per call | Scale a workload |
| [`k8s_apply`](MCP_CONNECTOR_TOOLS.md#k8s_apply) | Write, per call | Apply a manifest |
| [`k8s_delete`](MCP_CONNECTOR_TOOLS.md#k8s_delete) | Destructive, per call | Delete a Kubernetes object |
| [`k8s_exec`](MCP_CONNECTOR_TOOLS.md#k8s_exec) | Destructive | Run a command in a container |
| [`k8s_cordon`](MCP_CONNECTOR_TOOLS.md#k8s_cordon) | Destructive | Cordon or uncordon a node |

**Effects.** Read tools stay Read. `k8s_rollout_restart` is Write. `k8s_scale` is Write, and Destructive when `replicas` is 0. `k8s_apply` is Read with `dryRun`, and otherwise Write, becoming Destructive when an object's kind is in `destructiveKinds`, when any field is set to `null`, or when `force` or `allowFieldRemoval` is set. Before applying, Hugo dry-runs every object and compares it with the live one; if any field would silently disappear and `allowFieldRemoval` is false, nothing is applied and the result lists the paths. `k8s_delete` is Destructive (Read with `dryRun`), and deleting a Namespace, Node, CustomResourceDefinition, PersistentVolume, or StorageClass always needs a human. `k8s_exec` and `k8s_cordon` are always Destructive.

The built-in destructive kinds are the ones where one apply can hurt far more than the object itself, and `destructiveKinds` can add to them but never remove one. Namespace, Node, CustomResourceDefinition, and APIService sit underneath other workloads, and deleting or rewriting one cascades to everything inside or served by it. ClusterRole, ClusterRoleBinding, Role, RoleBinding, and ServiceAccount decide who can do what, so a small edit can hand out cluster-admin. PersistentVolume, PersistentVolumeClaim, and StorageClass hold or bind data, and a changed reclaim policy or a recreated claim can lose it. NetworkPolicy, ResourceQuota, LimitRange, and PriorityClass change what every pod in a namespace can reach, consume, or evict. The admission kinds (mutating and validating webhooks, ValidatingAdmissionPolicy and its binding) sit in front of every later API request and can block or rewrite all of them.

Scope checks reject calls rather than classify them. A namespace outside `allowedNamespaces` is rejected, as is a manifest whose namespace differs from the `namespace` argument. Changing a cluster-scoped kind needs `allowClusterScoped`, and reading one needs `clusterScopedReads`.

**Redaction.** `metadata.managedFields` is always stripped. Secret `data` and `stringData` come back as `<redacted: N bytes>`, including the copy in the last-applied annotation, and `k8s_list` and `k8s_describe` show Secret key names and sizes only.

**Limits.** `k8s_list` returns at most `maxObjects` per page and pages with `continue` (one namespace at a time). `k8s_logs` keeps the newest `maxLogLines`. `k8s_exec` runs for at most `execTimeoutSeconds`, and stdout and stderr each get half of the byte budget, keeping their ends.

**Test connection** calls `/version`, then tries to list one pod in the default namespace. A 401 fails the test; a 403 passes with `canListPods: false`, because a narrowly scoped credential is a legitimate setup. It reports the server version, the probe namespace, and the allowed namespaces.

**Least privilege.** [Kubernetes RBAC for Hugo](connectors/kubernetes-rbac.md) has read-only and operator Roles, the small ClusterRoles for cluster-scoped reads and cordoning, and how to mint a service account token for the `serviceAccountToken` slot.

**Testbed:** `docker/testbed/kubernetes` runs k3s v1.35.9 (privileged) with its API on port 28443, two namespaces, a deployment, a crash-looping pod, a Secret, and a custom resource. metrics-server is turned off on purpose, so the `k8s_top` failure path is exercised.

**Known limitations.** `k8s_top` needs metrics-server and returns `not_supported` without it. `k8s_cordon` does not drain. `k8s_delete` takes one object per call; there is no selector delete.

## Docker hosts

The `docker` connector talks to one Docker daemon through Docker.DotNet.Enhanced, the maintained fork that supports Engine 29. It lists and inspects containers and images, reads logs, takes one-shot stats, reads bounded event windows, changes container state, and runs commands in containers.

| Key | Kind | Default | Range or values | Meaning |
|---|---|---|---|---|
| `endpoint` | String, required | `unix:///var/run/docker.sock` | pattern `^(unix\|tcp\|npipe\|http\|https)://` | Daemon address: unix:///var/run/docker.sock (the Hugo host's socket), tcp://host:2376 (TLS) or tcp://host:2375, or npipe://./pipe/docker_engine. Reaching a daemon through an SSH resource is not supported yet. |
| `tlsVerify` | Boolean | `false` |  | Verify the daemon's certificate against the caCertificate in the tlsClientCertificate secret (or the system trust store). Requires the tlsClientCertificate slot. Default false. |
| `requestTimeoutSeconds` | Integer | `30` | 1 to 600 | Timeout of each daemon request. Default 30, minimum 1, maximum 600. |
| `showEnvironmentValues` | Boolean | `false` |  | When false, docker_inspect replaces environment variable values with <redacted> (names stay visible). Turn on only when containers carry no secrets in their environment. Default false. |
| `maxLogLines` | Integer | `500` | 1 to 100000 | Most lines docker_logs returns. Default 500. |
| `maxEventsWindowSeconds` | Integer | `3600` | 60 to 604800 | Largest time window docker_events reads. Default 3600 (one hour). |
| `execTimeoutSeconds` | Integer | `60` | 1 to 900 | Default and maximum run time of docker_exec. Default 60. |
| `maxStatsContainers` | Integer | `20` | 1 to 100 | Most containers docker_stats samples when no container is named. Default 20. |

| Slot | Accepts | Required | Fields read | Meaning |
|---|---|---|---|---|
| `tlsClientCertificate` | Certificate | no | `certificate`, `privateKey`, `caCertificate` | For tcp:// endpoints with TLS: the client certificate (field 'certificate'), its 'privateKey', and the 'caCertificate' that signed the daemon's certificate, all PEM. |

The endpoint can be the Hugo host's socket (`unix:///var/run/docker.sock`, which gives Hugo root-equivalent access to that host), a TCP address (`tcp://host:2376` with TLS, or `2375` without), `http`/`https`, or a Windows named pipe. With a client certificate bound, the endpoint must be `tcp://` or `https://`. With `tlsVerify` on, the daemon's certificate is checked against the `caCertificate` field, or the system trust store when that field is empty.

| Tool | Effect | Purpose |
|---|---|---|
| [`docker_ps`](MCP_CONNECTOR_TOOLS.md#docker_ps) | Read | List containers |
| [`docker_inspect`](MCP_CONNECTOR_TOOLS.md#docker_inspect) | Read | Inspect a container or image |
| [`docker_logs`](MCP_CONNECTOR_TOOLS.md#docker_logs) | Read | Read container logs |
| [`docker_stats`](MCP_CONNECTOR_TOOLS.md#docker_stats) | Read | Show container resource usage |
| [`docker_images`](MCP_CONNECTOR_TOOLS.md#docker_images) | Read | List images |
| [`docker_events`](MCP_CONNECTOR_TOOLS.md#docker_events) | Read | List daemon events |
| [`docker_container_action`](MCP_CONNECTOR_TOOLS.md#docker_container_action) | Write, per call | Start, restart, stop, kill, or remove a container |
| [`docker_exec`](MCP_CONNECTOR_TOOLS.md#docker_exec) | Destructive | Run a command in a container |

**Effects.** `docker_container_action` is classified by action: `start` and `restart` are Write; `stop`, `kill`, and `remove` are Destructive. `docker_exec` is always Destructive. Everything else is Read.

**Redaction and limits.** `docker_inspect` replaces environment variable values with `<redacted>` unless `showEnvironmentValues` is on; names stay visible. Labels and command lines are not redacted, so do not put secrets there either. `docker_logs` keeps the newest `maxLogLines`. `docker_events` reads a past window (15 minutes by default) and clips anything longer than `maxEventsWindowSeconds` by moving the start forward. `docker_stats` samples at most `maxStatsContainers` containers and takes about a second.

**Test connection** asks the daemon for its version and reports the engine version, API version, OS, and whether TLS is off, verified, or unverified.

**Least privilege.** The Docker API has no read-only mode: anyone who can reach the socket can start a privileged container. Treat a Docker resource as root on that host. Prefer TCP with mutual TLS over mounting the socket into Hugo, and use access modes and approvals as the real control.

**Testbed:** `docker/testbed/docker` runs `docker:29.8.2-dind` (privileged) on port 28375 over plain TCP, with a running and an exited fixture container.

**Known limitations.** Reaching a daemon through an SSH resource (the plan's `sshResourceName`) is not supported yet; `ssh://` endpoints fail validation with `not_supported`.

## HTTP APIs

The `http` connector turns any HTTP or REST API under one base URL into two tools: a classified request and a health check. It handles Basic, bearer, API key, and OAuth2 client credentials authentication, with OAuth2 tokens cached and refreshed.

| Key | Kind | Default | Range or values | Meaning |
|---|---|---|---|---|
| `baseUrl` | String, required |  | pattern `^https?://` | Absolute http or https URL every request path is relative to, for example https://api.example.com/v1. No query string or credentials. |
| `defaultHeaders` | StringList |  |  | Headers sent with every request, one 'Name: value' per line, for example 'Accept: application/json'. Authorization, cookies, and the API key header are not allowed here; use authType and a secret. |
| `timeoutSeconds` | Integer | `30` | 1 to 600 | Longest time one request may take, including redirects. Default 30, minimum 1, maximum 600. The binding's timeout applies when shorter. |
| `followRedirects` | Boolean | `true` |  | Follow redirects that stay on the same host, never downgrade https to http, and stay inside the allowed path prefixes. Authentication is never sent to a different origin. Default true. |
| `maxRedirects` | Integer | `5` | 0 to 20 | Most redirects followed per request. Default 5. |
| `verifyTls` | Boolean | `true` |  | When false, invalid or self-signed certificates are accepted (http_health_check still reports them). Keep true for anything reachable from the internet. Default true. |
| `authType` | Enum | `None` | `None`, `Basic`, `Bearer`, `ApiKeyHeader`, `OAuth2ClientCredentials` | None; Basic (basicAuth slot); Bearer (bearerToken slot); ApiKeyHeader (apiKey slot, sent in apiKeyHeaderName); OAuth2ClientCredentials (clientCredentials slot and tokenUrl; tokens are cached and refreshed). |
| `apiKeyHeaderName` | String | `X-API-Key` |  | Header that carries the API key when authType is ApiKeyHeader. Default X-API-Key. |
| `tokenUrl` | String |  | pattern `^https?://` | Token endpoint for the client credentials grant. Required when authType is OAuth2ClientCredentials. |
| `scopes` | StringList |  |  | Scopes requested with the client credentials grant, one per line or space separated. Empty requests the client's default scopes. |
| `allowedMethods` | StringList | `GET`, `HEAD`, `OPTIONS`, `POST`, `PUT`, `PATCH`, `DELETE` |  | HTTP methods http_request may use, one per line. Default GET, HEAD, OPTIONS, POST, PUT, PATCH, DELETE. Remove methods to forbid them outright. |
| `allowedPathPrefixes` | StringList | `/` |  | Path prefixes, relative to the base URL path, that calls may address, one per line, for example /orders. Matching is on whole segments: /orders allows /orders/42 but not /orders-archive. Default / (everything under the base URL). |
| `methodEffectOverrides` | StringList |  |  | Override the effect of matching calls, one 'METHOD /prefix=Effect' per line (METHOD may be *), for example 'POST /search=Read' or 'GET /admin=Destructive'. The longest matching prefix wins. Effects: Read, Write, Destructive. |
| `maxResponseBytes` | Integer | `1048576` | 1024 to 67108864 | Most response body bytes read per request; the rest is not downloaded. The call's output limits may show less. Default 1 MiB. |
| `maxRequestBytes` | Integer | `1048576` | 1 to 67108864 | Largest request body a call may send. Default 1 MiB. |

| Slot | Accepts | Required | Fields read | Meaning |
|---|---|---|---|---|
| `basicAuth` | Password | no | `username`, `password` | Password secret with fields 'username' and 'password'. Used when authType is Basic. |
| `bearerToken` | ApiToken | no | `token` | ApiToken secret (field 'token') sent as Authorization: Bearer. Used when authType is Bearer. |
| `apiKey` | ApiToken | no | `token` | ApiToken secret (field 'token') sent in the apiKeyHeaderName header. Used when authType is ApiKeyHeader. |
| `clientCredentials` | Generic, Password | no | `clientId` and `clientSecret`, or `username` and `password` | Generic secret with 'clientId' and 'clientSecret', or a Password secret with 'username' (client ID) and 'password' (client secret). Used when authType is OAuth2ClientCredentials. |

| Tool | Effect | Purpose |
|---|---|---|
| [`http_request`](MCP_CONNECTOR_TOOLS.md#http_request) | Read, per call | Send HTTP request |
| [`http_health_check`](MCP_CONNECTOR_TOOLS.md#http_health_check) | Read | HTTP health check |

**How `http_request` is classified.** The method must be in `allowedMethods`. The path must be relative, stay inside `allowedPathPrefixes` (matched on whole segments, so `/orders` allows `/orders/42` but not `/orders-archive`), and survive Hugo's path guard: no absolute URLs, no `..` or empty segments, no query string in the path, no encoded slashes, and no characters hidden behind repeated percent-encoding. A failure rejects the call. Then the method sets the effect: `GET`, `HEAD`, and `OPTIONS` are Read; `POST`, `PUT`, and `PATCH` are Write; `DELETE` and any other method are Destructive. Lines in `methodEffectOverrides` (`METHOD /prefix=Effect`, with `*` for any method) replace that effect; the longest matching prefix wins, and a named method beats `*` on the same prefix. That is how `POST /search=Read` makes a search endpoint usable from a ReadOnly binding. `http_health_check` is always Read and still respects the path prefixes.

**Redirects and credentials.** Redirects are followed by Hugo, not by the HTTP library, so each hop is checked: same host, no downgrade from https to http, still under the base path and the allowed prefixes, and at most `maxRedirects`. Authentication is sent only when scheme, host, and port all match the base URL. A refused hop is reported in the result rather than followed. HTTP error statuses (404, 500, 429) are normal results, and `Retry-After` is passed back to the caller.

**Limits.** At most `maxResponseBytes` of the body is downloaded; the rest is never read. Request bodies over `maxRequestBytes` are refused. `Set-Cookie` headers are redacted from results. Callers cannot set `Authorization`, cookies, the API key header, `Host`, or transport headers.

**Test connection** sends `GET` to the first allowed prefix with authentication and the redirect policy applied. A 401 or 403 fails with `auth_failed`, a 5xx with `upstream_error`, and anything else, a 404 included, passes, since many APIs have nothing at their root.

**Least privilege.** Use a token scoped to the endpoints the bindings need, and mirror that scope in `allowedMethods` and `allowedPathPrefixes`. Remove `DELETE` from `allowedMethods` when nothing should ever delete.

**Testbed:** an in-process HTTP/1.1 stub with optional TLS and an OAuth2 token endpoint (`src/Test.Shared/Connectors/Http`).

## Observability backends

Three read-only connectors query the metrics and logs backends most teams already run: `prometheus` (family `prom`), `loki` (family `loki`), and `elasticsearch` (family `es`, for both Elasticsearch and OpenSearch). They share authentication and transport settings:

| Key | Kind | Default | Range or values | Connectors | Meaning |
|---|---|---|---|---|---|
| `baseUrl` | String, required |  | pattern `^https?://` | all | *prometheus:* Prometheus server URL, for example http://prometheus:9090. Include a path prefix if Prometheus runs behind one. *loki:* Loki URL, for example http://loki:3100 (or the Grafana Cloud Logs URL). *elasticsearch:* Cluster URL, for example https://es.example.com:9200 or an OpenSearch endpoint. |
| `authType` | Enum | `None` | `None`, `Basic`, `Bearer`, `ApiKeyHeader`, `OAuth2ClientCredentials` | all | None sends no credentials. Basic uses the basicAuth slot, Bearer the bearerToken slot, ApiKeyHeader the apiKey slot in the apiKeyHeaderName header, and OAuth2ClientCredentials exchanges the clientCredentials slot for a cached access token at tokenUrl. |
| `apiKeyHeaderName` | String | `X-API-Key` | pattern `` ^[!#$%&'*+.^_`\|~0-9A-Za-z-]+$ `` | all | Header that carries the API key when authType is ApiKeyHeader. Default X-API-Key. Use Authorization with apiKeyPrefix 'ApiKey ' for Elasticsearch API keys. |
| `apiKeyPrefix` | String |  |  | all | Text placed before the API key value, including any trailing space (for example 'ApiKey '). Default empty. |
| `tokenUrl` | String |  | pattern `^https?://` | all | Token endpoint for OAuth2ClientCredentials. The client ID and secret are sent with HTTP Basic authentication. |
| `scopes` | StringList |  |  | all | Scopes requested with the client credentials grant, one per line. Empty requests none. |
| `defaultHeaders` | StringList |  |  | all | Headers sent with every request, one 'Name: value' per line. Do not put credentials here; bind a secret instead. |
| `verifyTls` | Boolean | `true` |  | all | When false, invalid or self-signed TLS certificates are accepted. Only disable for test systems. |
| `maxResponseBytes` | Integer | `8388608` | 65536 to 268435456 | all | Largest backend response read for one call. Larger responses fail with too_large. Default 8388608 (8 MiB). |

| Slot | Accepts | Required | Fields read | Meaning |
|---|---|---|---|---|
| `basicAuth` | Password | no | `username`, `password` | Password secret with 'username' and 'password'. Used when authType is Basic. |
| `bearerToken` | ApiToken | no | `token` | Token sent as 'Authorization: Bearer'. Used when authType is Bearer. |
| `apiKey` | ApiToken | no | `token` | Key sent in the apiKeyHeaderName header. Used when authType is ApiKeyHeader. |
| `clientCredentials` | Generic, Password | no | `clientId` and `clientSecret`, or `username` and `password` | Generic secret with 'clientId' and 'clientSecret', or a Password secret with 'username' (client ID) and 'password' (client secret). Used when authType is OAuth2ClientCredentials. |

OAuth2 client credentials are exchanged at `tokenUrl` with HTTP Basic client authentication and cached. A response larger than `maxResponseBytes` fails with `too_large` rather than being cut, because a half-parsed JSON document is worse than none. Time arguments accept RFC 3339, Unix seconds, `now`, or a duration before now, and a window longer than the connector's maximum fails with `time_range_exceeded`.

Every tool in all three connectors is Read, and none is classified per call. They can still be expensive, so the guards below are about cost, not safety.

### Prometheus

| Key | Kind | Default | Range or values | Meaning |
|---|---|---|---|---|
| `maxSeries` | Integer | `500` | 1 to 10000 | Most series, targets, or values one call returns; the rest are dropped and reported. Default 500. |
| `maxPointsPerSeries` | Integer | `240` | 10 to 11000 | Most samples per series in prom_query_range. The step is raised automatically to stay within it. Default 240. |
| `maxRangeHours` | Integer | `168` | 1 to 8760 | Longest window one range, series, or label call may cover. Default 168 (one week). |

| Tool | Effect | Purpose |
|---|---|---|
| [`prom_query`](MCP_CONNECTOR_TOOLS.md#prom_query) | Read | PromQL instant query |
| [`prom_query_range`](MCP_CONNECTOR_TOOLS.md#prom_query_range) | Read | PromQL range query |
| [`prom_alerts`](MCP_CONNECTOR_TOOLS.md#prom_alerts) | Read | Active Prometheus alerts |
| [`prom_rules`](MCP_CONNECTOR_TOOLS.md#prom_rules) | Read | Prometheus rules |
| [`prom_targets`](MCP_CONNECTOR_TOOLS.md#prom_targets) | Read | Prometheus scrape targets |
| [`prom_series`](MCP_CONNECTOR_TOOLS.md#prom_series) | Read | Find Prometheus series |
| [`prom_label_values`](MCP_CONNECTOR_TOOLS.md#prom_label_values) | Read | Prometheus label values |

`prom_query_range` raises the step automatically so no series exceeds `maxPointsPerSeries`, and says so in the result. Series, alerts, rules, and targets beyond `maxSeries` (or the call's row limit) are dropped and counted. **Test connection** calls `api/v1/status/buildinfo` and reports the Prometheus version. **Testbed:** Prometheus 3.7.0 on port 28090, scraping itself, with one always-firing alert and a recording rule.

### Grafana Loki

| Key | Kind | Default | Range or values | Meaning |
|---|---|---|---|---|
| `tenantHeader` | String |  |  | Sent as the X-Scope-OrgID header on every request for multi-tenant Loki. Leave empty for single-tenant Loki. |
| `maxLines` | Integer | `1000` | 1 to 5000 | Most log lines one call returns. Default 1000, maximum 5000. |
| `maxRangeHours` | Integer | `24` | 1 to 8760 | Longest window one query may cover. Default 24. |

| Tool | Effect | Purpose |
|---|---|---|
| [`loki_query`](MCP_CONNECTOR_TOOLS.md#loki_query) | Read | LogQL instant query |
| [`loki_query_range`](MCP_CONNECTOR_TOOLS.md#loki_query_range) | Read | LogQL range query |
| [`loki_labels`](MCP_CONNECTOR_TOOLS.md#loki_labels) | Read | Loki label names |
| [`loki_label_values`](MCP_CONNECTOR_TOOLS.md#loki_label_values) | Read | Loki label values |

Every query must contain a stream selector with at least one matcher (`{app="api"}`), which Loki needs anyway and which keeps a model from asking for every stream. The guard only checks that a matcher exists, so `{app=~".*"}` passes; `maxRangeHours` and `maxLines` are the real bounds. **Test connection** lists label names for the last hour and reports how many it found. **Testbed:** Loki 3.5.5 on port 28100, multi-tenant (`X-Scope-OrgID: hugo-testbed`), with two seeded streams.

### Elasticsearch and OpenSearch

| Key | Kind | Default | Range or values | Meaning |
|---|---|---|---|---|
| `flavor` | Enum | `Elasticsearch` | `Elasticsearch`, `OpenSearch` | Elasticsearch or OpenSearch. Test Connection reports the detected distribution. |
| `allowedIndexPatterns` | StringList |  |  | Index globs calls may read, one per line (for example logs-* and metrics-2026.*). Empty allows every index whose name does not start with a dot. Hidden (dot) indexes need a pattern that starts with a dot. |
| `maxHits` | Integer | `100` | 1 to 10000 | Most hits one es_search returns; larger sizes are lowered. Default 100. |

| Tool | Effect | Purpose |
|---|---|---|
| [`es_search`](MCP_CONNECTOR_TOOLS.md#es_search) | Read | Search Elasticsearch |
| [`es_count`](MCP_CONNECTOR_TOOLS.md#es_count) | Read | Count Elasticsearch documents |
| [`es_cluster_health`](MCP_CONNECTOR_TOOLS.md#es_cluster_health) | Read | Elasticsearch cluster health |
| [`es_indices`](MCP_CONNECTOR_TOOLS.md#es_indices) | Read | List Elasticsearch indexes |
| [`es_mappings`](MCP_CONNECTOR_TOOLS.md#es_mappings) | Read | Elasticsearch field mappings |

Every index argument must match `allowedIndexPatterns`. The guard refuses `_all` and other names starting with `_`, exclusions (`-logs-*`), cross-cluster names, date math, and uppercase names; with an empty list, every index not starting with a dot is allowed, and hidden indexes need a pattern that starts with a dot. `es_search` accepts only `query`, `size`, `from`, `sort`, `_source`, `aggs`, `search_after`, and `track_total_hits`, so `script_fields` and `runtime_mappings` are refused; scripts inside a query or aggregation are not inspected. `size` is lowered to `maxHits` and `from + size` may not exceed 10,000.

Use Elasticsearch API keys with `authType: ApiKeyHeader`, `apiKeyHeaderName: Authorization`, and `apiKeyPrefix: ApiKey ` (with the trailing space). **Test connection** reads the root endpoint and cluster health, reports the distribution, version, cluster name, and status, and warns when `flavor` disagrees with the detected distribution. **Testbed:** OpenSearch 2.19.3 on port 28992 with the security plugin disabled, a log index, a "secret" index outside the allowed patterns, and an alias spanning both.

**Known limitations.** `flavor` changes only the provider name in messages; both flavors use the same REST calls. Hits from indexes outside the allowed patterns, reached through an alias, are dropped from results, but totals, aggregations, and `es_count` still include them. Grant the API key `read` and `view_index_metadata` on the allowed indexes only, and `monitor` on the cluster for `es_cluster_health`, so the cluster enforces what the patterns intend.
