namespace Hugo.Connectors.Redis.Classification
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// The Redis command table: every command of Redis 7 (plus the hash field expiry commands of 7.4 and 8.0) classified
    /// as Read, Write, or Destructive, with container commands (<c>CONFIG</c>, <c>CLIENT</c>, ...) classified per
    /// subcommand. Commands that need a dedicated connection (blocking pops, subscriptions, transactions, connection
    /// state) are marked unsupported. Anything not in the table is Destructive (fail closed). Thread-safe.
    /// </summary>
    /// <remarks>
    /// Codes in the data below: R Read, W Write, D Destructive, A Destructive and always requires approval, B blocking
    /// (unsupported), S subscription or push (unsupported), T transaction or connection state (unsupported).
    /// </remarks>
    public static class RedisCommandTable
    {
        #region Private-Members

        private static readonly string[] _Plain = new string[]
        {
            // Strings
            "APPEND:W", "DECR:W", "DECRBY:W", "GET:R", "GETDEL:D", "GETEX:W", "GETRANGE:R", "GETSET:W", "INCR:W", "INCRBY:W",
            "INCRBYFLOAT:W", "LCS:R", "MGET:R", "MSET:W", "MSETNX:W", "PSETEX:W", "SET:W", "SETEX:W", "SETNX:W", "SETRANGE:W",
            "STRLEN:R", "SUBSTR:R",
            // Generic keys
            "COPY:W", "DEL:D", "DUMP:R", "EXISTS:R", "EXPIRE:W", "EXPIREAT:W", "EXPIRETIME:R", "KEYS:R", "MIGRATE:D", "MOVE:W",
            "PERSIST:W", "PEXPIRE:W", "PEXPIREAT:W", "PEXPIRETIME:R", "PTTL:R", "RANDOMKEY:R", "RENAME:D", "RENAMENX:W",
            "RESTORE:W", "RESTORE-ASKING:W", "SCAN:R", "SORT:R", "SORT_RO:R", "TOUCH:R", "TTL:R", "TYPE:R", "UNLINK:D", "WAIT:B",
            "WAITAOF:B",
            // Hashes
            "HDEL:D", "HEXISTS:R", "HGET:R", "HGETALL:R", "HINCRBY:W", "HINCRBYFLOAT:W", "HKEYS:R", "HLEN:R", "HMGET:R",
            "HMSET:W", "HRANDFIELD:R", "HSCAN:R", "HSET:W", "HSETNX:W", "HSTRLEN:R", "HVALS:R", "HEXPIRE:W", "HPEXPIRE:W",
            "HEXPIREAT:W", "HPEXPIREAT:W", "HPERSIST:W", "HTTL:R", "HPTTL:R", "HEXPIRETIME:R", "HPEXPIRETIME:R", "HGETDEL:D",
            "HGETEX:W", "HSETEX:W",
            // Lists
            "BLMOVE:B", "BLMPOP:B", "BLPOP:B", "BRPOP:B", "BRPOPLPUSH:B", "LINDEX:R", "LINSERT:W", "LLEN:R", "LMOVE:W",
            "LMPOP:W", "LPOP:W", "LPOS:R", "LPUSH:W", "LPUSHX:W", "LRANGE:R", "LREM:D", "LSET:W", "LTRIM:D", "RPOP:W",
            "RPOPLPUSH:W", "RPUSH:W", "RPUSHX:W",
            // Sets
            "SADD:W", "SCARD:R", "SDIFF:R", "SDIFFSTORE:W", "SINTER:R", "SINTERCARD:R", "SINTERSTORE:W", "SISMEMBER:R",
            "SMEMBERS:R", "SMISMEMBER:R", "SMOVE:W", "SPOP:W", "SRANDMEMBER:R", "SREM:D", "SSCAN:R", "SUNION:R", "SUNIONSTORE:W",
            // Sorted sets
            "BZMPOP:B", "BZPOPMAX:B", "BZPOPMIN:B", "ZADD:W", "ZCARD:R", "ZCOUNT:R", "ZDIFF:R", "ZDIFFSTORE:W", "ZINCRBY:W",
            "ZINTER:R", "ZINTERCARD:R", "ZINTERSTORE:W", "ZLEXCOUNT:R", "ZMPOP:W", "ZMSCORE:R", "ZPOPMAX:W", "ZPOPMIN:W",
            "ZRANDMEMBER:R", "ZRANGE:R", "ZRANGEBYLEX:R", "ZRANGEBYSCORE:R", "ZRANGESTORE:W", "ZRANK:R", "ZREM:D",
            "ZREMRANGEBYLEX:D", "ZREMRANGEBYRANK:D", "ZREMRANGEBYSCORE:D", "ZREVRANGE:R", "ZREVRANGEBYLEX:R",
            "ZREVRANGEBYSCORE:R", "ZREVRANK:R", "ZSCAN:R", "ZSCORE:R", "ZUNION:R", "ZUNIONSTORE:W",
            // Streams
            "XACK:W", "XADD:W", "XAUTOCLAIM:W", "XCLAIM:W", "XDEL:D", "XLEN:R", "XPENDING:R", "XRANGE:R", "XREAD:R",
            "XREADGROUP:W", "XREVRANGE:R", "XSETID:W", "XTRIM:D",
            // Bitmaps
            "BITCOUNT:R", "BITFIELD:W", "BITFIELD_RO:R", "BITOP:W", "BITPOS:R", "GETBIT:R", "SETBIT:W",
            // HyperLogLog
            "PFADD:W", "PFCOUNT:R", "PFDEBUG:D", "PFMERGE:W", "PFSELFTEST:D",
            // Geo
            "GEOADD:W", "GEODIST:R", "GEOHASH:R", "GEOPOS:R", "GEORADIUS:R", "GEORADIUSBYMEMBER:R", "GEORADIUSBYMEMBER_RO:R",
            "GEORADIUS_RO:R", "GEOSEARCH:R", "GEOSEARCHSTORE:W",
            // Pub/Sub
            "PSUBSCRIBE:S", "PUBLISH:W", "PUNSUBSCRIBE:S", "SPUBLISH:W", "SSUBSCRIBE:S", "SUBSCRIBE:S", "SUNSUBSCRIBE:S",
            "UNSUBSCRIBE:S",
            // Scripting and functions
            "EVAL:D", "EVALSHA:D", "EVALSHA_RO:R", "EVAL_RO:R", "FCALL:D", "FCALL_RO:R",
            // Transactions
            "DISCARD:T", "EXEC:T", "MULTI:T", "UNWATCH:T", "WATCH:T",
            // Connection
            "AUTH:T", "ECHO:R", "HELLO:T", "PING:R", "QUIT:T", "RESET:T", "SELECT:T",
            // Server
            "BGREWRITEAOF:W", "BGSAVE:W", "DBSIZE:R", "DEBUG:A", "FAILOVER:A", "FLUSHALL:A", "FLUSHDB:A", "INFO:R",
            "LASTSAVE:R", "LOLWUT:R", "MONITOR:S", "PSYNC:S", "REPLCONF:T", "REPLICAOF:A", "ROLE:R", "SAVE:D", "SHUTDOWN:A",
            "SLAVEOF:A", "SWAPDB:A", "SYNC:S", "TIME:R",
            // Cluster
            "ASKING:T", "READONLY:T", "READWRITE:T"
        };

        private static readonly string[] _Containers = new string[]
        {
            "ACL=CAT:R DELUSER:D DRYRUN:R GENPASS:R GETUSER:R HELP:R LIST:R LOAD:D LOG:R SAVE:W SETUSER:D USERS:R WHOAMI:R",
            "CLIENT=CACHING:T GETNAME:R GETREDIR:R HELP:R ID:R INFO:R KILL:D LIST:R NO-EVICT:W NO-TOUCH:W PAUSE:D REPLY:T SETINFO:W SETNAME:W TRACKING:T TRACKINGINFO:R UNBLOCK:D UNPAUSE:W",
            "CLUSTER=ADDSLOTS:D ADDSLOTSRANGE:D BUMPEPOCH:D COUNT-FAILURE-REPORTS:R COUNTKEYSINSLOT:R DELSLOTS:D DELSLOTSRANGE:D FAILOVER:A FLUSHSLOTS:D FORGET:A GETKEYSINSLOT:R HELP:R INFO:R KEYSLOT:R LINKS:R MEET:D MYID:R MYSHARDID:R NODES:R REPLICAS:R REPLICATE:A RESET:A SAVECONFIG:W SET-CONFIG-EPOCH:D SETSLOT:D SHARDS:R SLAVES:R SLOTS:R",
            "COMMAND=COUNT:R DOCS:R GETKEYS:R GETKEYSANDFLAGS:R HELP:R INFO:R LIST:R",
            "CONFIG=GET:R HELP:R RESETSTAT:D REWRITE:D SET:D",
            "FUNCTION=DELETE:D DUMP:R FLUSH:A HELP:R KILL:D LIST:R LOAD:W RESTORE:D STATS:R",
            "LATENCY=DOCTOR:R GRAPH:R HELP:R HISTOGRAM:R HISTORY:R LATEST:R RESET:D",
            "MEMORY=DOCTOR:R HELP:R MALLOC-STATS:R PURGE:W STATS:R USAGE:R",
            "MODULE=HELP:R LIST:R LOAD:D LOADEX:D UNLOAD:D",
            "OBJECT=ENCODING:R FREQ:R HELP:R IDLETIME:R REFCOUNT:R",
            "PUBSUB=CHANNELS:R HELP:R NUMPAT:R NUMSUB:R SHARDCHANNELS:R SHARDNUMSUB:R",
            "SCRIPT=DEBUG:T EXISTS:R FLUSH:A HELP:R KILL:D LOAD:W",
            "SENTINEL=CKQUORUM:R CONFIG:D DEBUG:D FAILOVER:A FLUSHCONFIG:D GET-MASTER-ADDR-BY-NAME:R GET-PRIMARY-ADDR-BY-NAME:R HELP:R INFO-CACHE:R IS-MASTER-DOWN-BY-ADDR:R IS-PRIMARY-DOWN-BY-ADDR:R MASTER:R MASTERS:R MONITOR:D MYID:R PENDING-SCRIPTS:R PRIMARIES:R PRIMARY:R REMOVE:D REPLICAS:R RESET:D SENTINELS:R SET:D SIMULATE-FAILURE:D SLAVES:R",
            "SLOWLOG=GET:R HELP:R LEN:R RESET:D",
            "XGROUP=CREATE:W CREATECONSUMER:W DELCONSUMER:D DESTROY:D HELP:R SETID:W",
            "XINFO=CONSUMERS:R GROUPS:R HELP:R STREAM:R"
        };

        private static readonly Lazy<Dictionary<string, RedisCommandSpec>> _Table = new Lazy<Dictionary<string, RedisCommandSpec>>(Build, System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Look up a top-level command.
        /// </summary>
        /// <param name="name">Command name, any case.</param>
        /// <returns>Spec, or null when the command is not in the table.</returns>
        public static RedisCommandSpec? Find(string? name)
        {
            if (String.IsNullOrWhiteSpace(name)) return null;
            return _Table.Value.TryGetValue(name.Trim().ToUpperInvariant(), out RedisCommandSpec? spec) ? spec : null;
        }

        /// <summary>
        /// Every top-level command name in the table, sorted.
        /// </summary>
        /// <returns>Names.</returns>
        public static List<string> Names()
        {
            return _Table.Value.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// Every command and subcommand pair (for example <c>CONFIG SET</c>) in the table, sorted.
        /// </summary>
        /// <returns>Names.</returns>
        public static List<string> AllEntries()
        {
            List<string> entries = new List<string>();
            foreach (RedisCommandSpec spec in _Table.Value.Values)
            {
                entries.Add(spec.Name);
                foreach (RedisCommandSpec sub in spec.Subcommands.Values) entries.Add(spec.Name + " " + sub.Name);
            }
            return entries.OrderBy(e => e, StringComparer.Ordinal).ToList();
        }

        #endregion

        #region Private-Methods

        private static Dictionary<string, RedisCommandSpec> Build()
        {
            Dictionary<string, RedisCommandSpec> table = new Dictionary<string, RedisCommandSpec>(StringComparer.Ordinal);
            foreach (string entry in _Plain)
            {
                RedisCommandSpec spec = Parse(entry);
                if (table.ContainsKey(spec.Name)) throw new InvalidOperationException("Duplicate Redis command " + spec.Name + " in the table.");
                table[spec.Name] = spec;
            }

            foreach (string line in _Containers)
            {
                int eq = line.IndexOf('=');
                string name = line.Substring(0, eq);
                if (table.ContainsKey(name)) throw new InvalidOperationException("Duplicate Redis command " + name + " in the table.");
                RedisCommandSpec container = new RedisCommandSpec(name, EffectEnum.Read);
                foreach (string sub in line.Substring(eq + 1).Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    RedisCommandSpec subSpec = Parse(sub);
                    container.Subcommands[subSpec.Name] = subSpec;
                }
                table[name] = container;
            }

            return table;
        }

        private static RedisCommandSpec Parse(string entry)
        {
            int colon = entry.LastIndexOf(':');
            string name = entry.Substring(0, colon);
            char code = entry[colon + 1];
            switch (code)
            {
                case 'R':
                    return new RedisCommandSpec(name, EffectEnum.Read);
                case 'W':
                    return new RedisCommandSpec(name, EffectEnum.Write);
                case 'D':
                    return new RedisCommandSpec(name, EffectEnum.Destructive);
                case 'A':
                    return new RedisCommandSpec(name, EffectEnum.Destructive, null, true);
                case 'B':
                    return new RedisCommandSpec(name, EffectEnum.Write, "it blocks the shared connection until data arrives; use the non-blocking form (for example LPOP instead of BLPOP)");
                case 'S':
                    return new RedisCommandSpec(name, EffectEnum.Read, "it turns the connection into a subscription or replication stream, which a request and response tool cannot carry");
                case 'T':
                    return new RedisCommandSpec(name, EffectEnum.Write, "it changes per-connection state (transactions, authentication, database selection, client tracking) on a shared, pooled connection");
                default:
                    throw new InvalidOperationException("Unknown classification code '" + code + "' for Redis command " + name + ".");
            }
        }

        #endregion
    }
}
