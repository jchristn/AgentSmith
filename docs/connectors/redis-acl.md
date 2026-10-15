# Redis ACLs for Hugo

Hugo classifies every Redis call and holds writes and deletes for approval, but the Redis server only knows the user Hugo logs in as. If that user is `default` with full access, a classifier bug or a careless approval is all that stands between an agent and `FLUSHALL`. Redis 6 and later can do better: an ACL user that can run only the commands Hugo sends. Treat the ACL as the first line of defense and Hugo's classifier as the second.

This page gives two users. **`hugo-reader`** covers every Read tool and read-only `redis_command` calls, for resources that are only ever bound ReadOnly. **`hugo-operator`** adds the commands behind `redis_set`, `redis_expire`, and `redis_delete`, for resources bound ReadWriteWithApproval or ReadWrite. Put the user name in the resource's `username` setting (or in the `username` field of the secret bound to the `password` slot) and its password in that secret.

## What each tool sends

The ACLs below are built from this table, which comes from the connector code. If a tool fails with `permission_denied` and the message mentions `NOPERM`, the ACL is missing a command from its row.

| Tool | Effect | Commands sent |
|---|---|---|
| Test connection | | `PING`, `INFO server` |
| `redis_info` | Read | `INFO`, or `INFO <section>` per requested section |
| `redis_scan` | Read | `SCAN ... MATCH ... COUNT ... [TYPE ...]`, `TYPE` and `PTTL` per key (unless `details` is false), `DBSIZE` |
| `redis_get` | Read | `TYPE`, `PTTL`, then by type: `STRLEN` and `GETRANGE`; `HLEN` and `HSCAN` or `HMGET`; `LLEN` and `LRANGE`; `SCARD` and `SSCAN`; `ZCARD` and `ZRANGE ... [REV] WITHSCORES`; `XLEN` and `XRANGE` or `XREVRANGE` |
| `redis_ttl` | Read | `PTTL` |
| `redis_memory_usage` | Read | `MEMORY USAGE ... SAMPLES ...`, `TYPE` |
| `redis_slowlog` | Read | `SLOWLOG GET` |
| `redis_client_list` | Read | `CLIENT LIST` |
| `redis_latency` | Read | `CONFIG GET latency-monitor-threshold`, `LATENCY LATEST` or `LATENCY HISTORY`, and `LATENCY DOCTOR` when asked |
| `redis_bigkeys` | Read | `SCAN`, `MEMORY USAGE ... SAMPLES 5`, `TYPE`, `PTTL`, `DBSIZE` |
| `redis_set` | Write | `SET ... [NX\|XX] [EX ...\|KEEPTTL]` |
| `redis_expire` | Write | `EXPIRE` or `PERSIST`, then `EXISTS` when nothing changed |
| `redis_delete` | Destructive | `UNLINK`, one key per call |
| `redis_command` | per call | Whatever command the agent asks for, plus `COMMAND GETKEYS` on a cluster to route by key |

The client library (StackExchange.Redis) sends a few commands of its own while connecting and keeping the connection healthy: `AUTH`, `CLIENT SETNAME` (the `clientName` setting, `hugo` by default), `CLIENT SETINFO`, `SELECT` when `database` is not 0, `ECHO` or `PING` as a heartbeat, `INFO` and `CONFIG GET` to learn the server's role and settings, `CLUSTER NODES` on a cluster, `GET __Booksleeve_TieBreak` during primary election, and `SUBSCRIBE __Booksleeve_MasterChanged` on a separate subscriber connection. The library tolerates `NOPERM` on most of these, but granting them keeps `ACL LOG` clean and makes topology changes visible to Hugo sooner.

## Read-only: `hugo-reader`

```
ACL SETUSER hugo-reader reset on >CHANGE-ME-TO-A-LONG-RANDOM-PASSWORD
  ~* &__Booksleeve_MasterChanged
  +@read -keys
  +ping +echo +info +select +subscribe
  +client|setname +client|setinfo +client|id +client|list
  +config|get +slowlog|get +memory|usage
  +latency|latest +latency|history +latency|doctor
  +cluster|nodes +command|getkeys
```

The command is wrapped here for reading. Run it as a single line in `redis-cli`, or put everything after the user name on one `user hugo-reader ...` line of an `aclfile`, which survives restarts.

`+@read` is Redis's own category of commands that only read data. It covers `SCAN`, `TYPE`, `PTTL`, `DBSIZE`, `EXISTS`, every type-specific read the tools use, and the read-only commands an agent might reasonably send through `redis_command` (`GET`, `MGET`, `HGETALL`, `XINFO`, `OBJECT ENCODING`). `-keys` takes `KEYS` back out, matching Hugo's default `deniedCommands`. Administrative reads such as `SLOWLOG GET`, `CLIENT LIST`, and `CONFIG GET` are not in `@read`, so they are granted one subcommand at a time. Granting `+slowlog`, `+latency`, or `+memory` whole would also allow `SLOWLOG RESET`, `LATENCY RESET`, and `MEMORY PURGE`, which Hugo classifies as Destructive or Write.

Two of those grants deserve a second look. `CONFIG GET` can read `requirepass` and `masterauth`. Hugo scrubs the resource's own password from every result, but not other secrets the server's configuration might hold, so leave `+config|get` out if your configuration carries credentials and you can live without `redis_latency` showing the monitor threshold. `CLIENT LIST` shows client addresses and the last command each client ran, which is the point of `redis_client_list`.

The key pattern `~*` lets the user read every key. If agents only need part of the keyspace, narrow it: `%R~session:* %R~cache:*` (Redis 7 and later) allows reads of those prefixes only. `SCAN` is not restricted by key patterns, so `redis_scan` still lists every key name, but `TYPE`, `PTTL`, and reads of keys outside the patterns fail with `NOPERM`, which Hugo reports as `permission_denied`. Pass a `pattern` inside the allowed prefixes, or `details: false` to list names without touching the keys.

The testbed's `hugo-reader` (in `docker/testbed/redis/compose.yaml`) is a looser variant built from categories (`+@read +@connection +slowlog +latency +memory ...`). It is convenient for tests; the version above is the one to deploy.

## Operator: `hugo-operator`

```
ACL SETUSER hugo-operator reset on >CHANGE-ME-TO-A-DIFFERENT-PASSWORD
  ~* &__Booksleeve_MasterChanged
  +@read -keys
  +ping +echo +info +select +subscribe
  +client|setname +client|setinfo +client|id +client|list
  +config|get +slowlog|get +memory|usage
  +latency|latest +latency|history +latency|doctor
  +cluster|nodes +command|getkeys
  +set +expire +persist +unlink
```

The last line is the whole difference: `SET` for `redis_set`, `EXPIRE` and `PERSIST` for `redis_expire`, and `UNLINK` for `redis_delete`. Every Write and Destructive tool works, and every one of them still goes through the binding's access mode and approvals.

What this user cannot do matters as much. It cannot `FLUSHALL`, `FLUSHDB`, `CONFIG SET`, `SHUTDOWN`, `DEBUG`, `REPLICAOF`, `SCRIPT FLUSH`, or `EVAL`, and it cannot change other users. Hugo already marks the flush, shutdown, debug, replication, and failover commands as always needing a human, but an approval is a person reading a card under time pressure. The ACL does not get tired.

If operators want broader writes through `redis_command`, add the category and subtract the dangerous part explicitly: `+@write -@dangerous`. That allows `DEL`, `HSET`, `LPUSH`, `INCR`, and the rest of everyday writing while keeping out `FLUSHALL`, `FLUSHDB`, `SWAPDB`, `MIGRATE`, and `RESTORE`. Administrative commands such as `CONFIG SET`, `SHUTDOWN`, and `DEBUG` are not in `@write` at all. Check the result with `ACL DRYRUN hugo-operator FLUSHALL`, which should answer with a permission error.

## Topologies

**Standalone and replicas.** Create the user on the primary and on every replica, or load it from an `aclfile` managed with the rest of your configuration. ACL changes are not replicated.

**Cluster.** Every node needs the same user, because Hugo connects to all of them. Redis Cluster does not propagate ACLs between nodes, so use an `aclfile` deployed to each node or run `ACL SETUSER` on each. Cluster mode only has database 0, so `+select` is harmless but unused.

**Sentinel.** Hugo signs in to the Sentinel nodes with the same user name and password it uses for Redis. On each Sentinel, create a user along the lines of `+ping +info +role +subscribe +sentinel|get-master-addr-by-name +sentinel|sentinels +sentinel|replicas +sentinel|slaves +client|setname +client|setinfo` and `&*` for the `+switch-master` notifications. The Redis nodes themselves get `hugo-reader` or `hugo-operator` as above.

## Checking the result

Run Test Connection, then each Read tool through a ReadOnly binding, then the write tools through a ReadWriteWithApproval binding. `ACL LOG` on the server lists every refused command with the user and the reason, which is the quickest way to find a missing grant. A clean `ACL LOG` after an hour of normal agent use means the ACL matches what Hugo actually does.
