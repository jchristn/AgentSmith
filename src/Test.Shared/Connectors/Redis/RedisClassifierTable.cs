namespace Test.Shared.Connectors.Redis
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The <c>redis_command</c> classifier table. Each row is <c>expected|command line</c> or
    /// <c>expected|command line|options</c>, where expected is R (Read), W (Write), D (Destructive), A (Destructive that
    /// always requires approval), or X (rejected), and options are <c>deny=CMD</c> or <c>ro=CMD</c> entries separated by
    /// semicolons.
    /// </summary>
    public static class RedisClassifierTable
    {
        #region Private-Members

        private static readonly string[] _Rows = new string[]
        {
            // Strings
            "R|GET user:1", "R|get user:1", "R|MGET a b c", "R|GETRANGE k 0 10", "R|SUBSTR k 0 10", "R|STRLEN k", "R|LCS a b",
            "W|SET k v", "W|set k v EX 10", "W|SET k v NX", "W|SET k v GET", "W|SETNX k v", "W|SETEX k 10 v", "W|PSETEX k 100 v",
            "W|MSET a 1 b 2", "W|MSETNX a 1 b 2", "W|APPEND k v", "W|INCR c", "W|INCRBY c 5", "W|INCRBYFLOAT c 1.5", "W|DECR c",
            "W|DECRBY c 2", "W|SETRANGE k 0 x", "W|GETSET k v", "W|GETEX k EX 10", "D|GETDEL k",
            // Generic keys
            "R|EXISTS a b", "R|TYPE k", "R|TTL k", "R|PTTL k", "R|EXPIRETIME k", "R|PEXPIRETIME k", "R|SCAN 0 MATCH user:* COUNT 100",
            "R|SCAN 0 TYPE hash", "R|DBSIZE", "R|RANDOMKEY", "R|DUMP k", "R|TOUCH a b", "R|SORT_RO list", "R|SORT list LIMIT 0 10",
            "W|SORT list STORE dest", "W|sort list by w_* store dest", "W|EXPIRE k 60", "W|PEXPIRE k 600", "W|EXPIREAT k 1700000000",
            "W|PEXPIREAT k 1700000000000", "W|PERSIST k", "W|COPY a b", "D|COPY a b REPLACE", "W|RENAMENX a b", "D|RENAME a b",
            "W|MOVE k 1", "W|RESTORE k 0 payload", "D|RESTORE k 0 payload REPLACE", "W|RESTORE-ASKING k 0 payload", "D|DEL k",
            "D|DEL a b c", "D|UNLINK k", "D|MIGRATE host 6379 k 0 5000", "X|KEYS *", "X|keys user:*", "X|WAIT 1 0", "X|WAITAOF 1 0 0",
            // Object and memory
            "R|OBJECT ENCODING k", "R|OBJECT FREQ k", "R|OBJECT IDLETIME k", "R|OBJECT REFCOUNT k", "R|OBJECT HELP", "D|OBJECT SETFREQ k",
            "R|MEMORY USAGE k", "R|MEMORY USAGE k SAMPLES 5", "R|MEMORY STATS", "R|MEMORY DOCTOR", "R|MEMORY MALLOC-STATS", "W|MEMORY PURGE",
            // Hashes
            "R|HGET h f", "R|HMGET h a b", "R|HGETALL h", "R|HKEYS h", "R|HVALS h", "R|HLEN h", "R|HEXISTS h f", "R|HSTRLEN h f",
            "R|HSCAN h 0 COUNT 100", "R|HRANDFIELD h 2", "R|HTTL h FIELDS 1 f", "R|HPTTL h FIELDS 1 f", "R|HEXPIRETIME h FIELDS 1 f",
            "W|HSET h f v", "W|HSETNX h f v", "W|HMSET h a 1 b 2", "W|HINCRBY h f 1", "W|HINCRBYFLOAT h f 1.5", "W|HEXPIRE h 60 FIELDS 1 f",
            "W|HPERSIST h FIELDS 1 f", "W|HGETEX h EX 10 FIELDS 1 f", "W|HSETEX h EX 10 FIELDS 1 f v", "D|HDEL h f", "D|HGETDEL h FIELDS 1 f",
            // Lists
            "R|LRANGE l 0 10", "R|LINDEX l 0", "R|LLEN l", "R|LPOS l x", "W|LPUSH l x", "W|RPUSH l x", "W|LPUSHX l x", "W|RPUSHX l x",
            "W|LINSERT l BEFORE a b", "W|LSET l 0 x", "W|LPOP l", "W|RPOP l 2", "W|LMOVE a b LEFT RIGHT", "W|RPOPLPUSH a b",
            "W|LMPOP 1 l LEFT", "D|LREM l 0 x", "D|LTRIM l 0 99", "X|BLPOP l 0", "X|BRPOP l 5", "X|BLMOVE a b LEFT RIGHT 0",
            "X|BRPOPLPUSH a b 0", "X|BLMPOP 0 1 l LEFT",
            // Sets
            "R|SMEMBERS s", "R|SISMEMBER s m", "R|SMISMEMBER s a b", "R|SCARD s", "R|SSCAN s 0", "R|SRANDMEMBER s 3", "R|SINTER a b",
            "R|SUNION a b", "R|SDIFF a b", "R|SINTERCARD 2 a b", "W|SADD s m", "W|SMOVE a b m", "W|SPOP s", "W|SINTERSTORE d a b",
            "W|SUNIONSTORE d a b", "W|SDIFFSTORE d a b", "D|SREM s m",
            // Sorted sets
            "R|ZRANGE z 0 10 WITHSCORES", "R|ZRANGE z 0 10 REV", "R|ZRANGEBYSCORE z 0 100", "R|ZREVRANGEBYSCORE z 100 0", "R|ZRANGEBYLEX z - +",
            "R|ZREVRANGEBYLEX z + -", "R|ZREVRANGE z 0 5", "R|ZSCORE z m", "R|ZMSCORE z a b", "R|ZRANK z m", "R|ZREVRANK z m", "R|ZCARD z",
            "R|ZCOUNT z 0 10", "R|ZLEXCOUNT z - +", "R|ZSCAN z 0", "R|ZRANDMEMBER z 2", "R|ZINTER 2 a b", "R|ZUNION 2 a b", "R|ZDIFF 2 a b",
            "R|ZINTERCARD 2 a b", "W|ZADD z 1 m", "W|ZINCRBY z 1 m", "W|ZPOPMIN z", "W|ZPOPMAX z", "W|ZMPOP 1 z MIN", "W|ZRANGESTORE d z 0 1",
            "W|ZINTERSTORE d 2 a b", "W|ZUNIONSTORE d 2 a b", "W|ZDIFFSTORE d 2 a b", "D|ZREM z m", "D|ZREMRANGEBYRANK z 0 1",
            "D|ZREMRANGEBYSCORE z 0 1", "D|ZREMRANGEBYLEX z - +", "X|BZPOPMIN z 0", "X|BZPOPMAX z 0", "X|BZMPOP 0 1 z MIN",
            // Streams
            "R|XRANGE s - + COUNT 10", "R|XREVRANGE s + - COUNT 10", "R|XLEN s", "R|XREAD COUNT 10 STREAMS s 0", "X|XREAD BLOCK 0 STREAMS s $",
            "R|XPENDING s g", "R|XINFO STREAM s", "R|XINFO GROUPS s", "R|XINFO CONSUMERS s g", "R|XINFO HELP", "W|XADD s * f v",
            "W|XACK s g 1-1", "W|XCLAIM s g c 0 1-1", "W|XAUTOCLAIM s g c 0 0", "W|XSETID s 5-1", "W|XREADGROUP GROUP g c STREAMS s >",
            "X|XREADGROUP GROUP g c BLOCK 100 STREAMS s >", "W|XGROUP CREATE s g $", "W|XGROUP CREATECONSUMER s g c", "W|XGROUP SETID s g $",
            "D|XGROUP DESTROY s g", "D|XGROUP DELCONSUMER s g c", "D|XDEL s 1-1", "D|XTRIM s MAXLEN 100",
            // Bitmaps, HyperLogLog, Geo
            "R|GETBIT k 7", "R|BITCOUNT k", "R|BITPOS k 1", "R|BITFIELD_RO k GET u8 0", "W|SETBIT k 7 1", "W|BITFIELD k INCRBY u8 0 1",
            "W|BITOP AND d a b", "R|PFCOUNT h", "W|PFADD h a b", "W|PFMERGE d a b", "D|PFDEBUG GETREG h", "D|PFSELFTEST",
            "R|GEOPOS g m", "R|GEODIST g a b", "R|GEOHASH g m", "R|GEOSEARCH g FROMMEMBER m BYRADIUS 10 km", "R|GEORADIUS g 0 0 10 km",
            "W|GEORADIUS g 0 0 10 km STORE d", "R|GEORADIUSBYMEMBER g m 10 km", "W|GEORADIUSBYMEMBER g m 10 km STOREDIST d",
            "R|GEORADIUS_RO g 0 0 10 km", "R|GEORADIUSBYMEMBER_RO g m 10 km", "W|GEOADD g 0 0 m", "W|GEOSEARCHSTORE d g FROMMEMBER m BYRADIUS 1 km",
            // Pub/Sub
            "W|PUBLISH ch hello", "W|SPUBLISH ch hello", "R|PUBSUB CHANNELS", "R|PUBSUB NUMSUB ch", "R|PUBSUB NUMPAT", "R|PUBSUB SHARDCHANNELS",
            "X|SUBSCRIBE ch", "X|PSUBSCRIBE ch*", "X|SSUBSCRIBE ch", "X|UNSUBSCRIBE", "X|PUNSUBSCRIBE", "X|SUNSUBSCRIBE",
            // Scripting and functions
            "D|EVAL \"return 1\" 0", "D|EVALSHA abc 0", "R|EVAL_RO \"return 1\" 0", "R|EVALSHA_RO abc 0", "D|FCALL f 0", "R|FCALL_RO f 0",
            "R|SCRIPT EXISTS abc", "W|SCRIPT LOAD \"return 1\"", "A|SCRIPT FLUSH", "D|SCRIPT KILL", "X|SCRIPT DEBUG YES", "R|FUNCTION LIST",
            "R|FUNCTION STATS", "R|FUNCTION DUMP", "W|FUNCTION LOAD code", "D|FUNCTION DELETE lib", "A|FUNCTION FLUSH", "D|FUNCTION RESTORE payload",
            "D|FUNCTION KILL",
            // Transactions and connection
            "X|MULTI", "X|EXEC", "X|DISCARD", "X|WATCH k", "X|UNWATCH", "X|AUTH secret", "X|AUTH user secret", "X|HELLO 3", "X|SELECT 1",
            "X|QUIT", "X|RESET", "R|PING", "R|ECHO hi",
            // Client
            "R|CLIENT LIST", "R|CLIENT INFO", "R|CLIENT ID", "R|CLIENT GETNAME", "R|CLIENT TRACKINGINFO", "R|CLIENT GETREDIR", "W|CLIENT SETNAME x",
            "W|CLIENT SETINFO LIB-NAME x", "W|CLIENT NO-EVICT ON", "W|CLIENT NO-TOUCH ON", "W|CLIENT UNPAUSE", "D|CLIENT KILL ID 5",
            "D|CLIENT PAUSE 1000", "D|CLIENT UNBLOCK 5", "X|CLIENT REPLY OFF", "X|CLIENT TRACKING ON", "X|CLIENT CACHING YES",
            "D|CLIENT NOSUCHSUB",
            // Server
            "R|INFO", "R|INFO memory", "R|TIME", "R|LASTSAVE", "R|ROLE", "R|LOLWUT", "R|DBSIZE", "R|COMMAND", "R|COMMAND COUNT",
            "R|COMMAND DOCS GET", "R|COMMAND INFO GET", "R|COMMAND GETKEYS GET k", "R|COMMAND LIST", "R|CONFIG GET maxmemory",
            "R|config get *", "D|CONFIG SET maxmemory 1gb", "D|CONFIG REWRITE", "D|CONFIG RESETSTAT", "R|CONFIG", "D|CONFIG UNKNOWNSUB",
            "R|SLOWLOG GET 10", "R|SLOWLOG LEN", "D|SLOWLOG RESET", "R|LATENCY LATEST", "R|LATENCY HISTORY command", "R|LATENCY DOCTOR",
            "R|LATENCY GRAPH command", "R|LATENCY HISTOGRAM", "D|LATENCY RESET", "W|BGSAVE", "W|BGREWRITEAOF", "D|SAVE", "A|FLUSHDB",
            "A|FLUSHDB ASYNC", "A|FLUSHALL", "A|flushall sync", "A|SHUTDOWN", "A|SHUTDOWN NOSAVE", "A|DEBUG SLEEP 1", "A|SWAPDB 0 1",
            "A|REPLICAOF host 6379", "A|SLAVEOF NO ONE", "A|FAILOVER", "X|MONITOR", "X|SYNC", "X|PSYNC ? -1", "X|REPLCONF listening-port 1",
            // ACL and modules
            "R|ACL WHOAMI", "R|ACL LIST", "R|ACL USERS", "R|ACL GETUSER default", "R|ACL CAT", "R|ACL LOG", "R|ACL LOG 10", "D|ACL LOG RESET",
            "R|ACL DRYRUN u GET k", "R|ACL GENPASS", "D|ACL SETUSER u on", "D|ACL DELUSER u", "D|ACL LOAD", "W|ACL SAVE", "R|MODULE LIST",
            "D|MODULE LOAD /x.so", "D|MODULE UNLOAD m", "D|MODULE LOADEX /x.so",
            // Cluster and Sentinel
            "R|CLUSTER INFO", "R|CLUSTER NODES", "R|CLUSTER SHARDS", "R|CLUSTER SLOTS", "R|CLUSTER MYID", "R|CLUSTER KEYSLOT k",
            "R|CLUSTER COUNTKEYSINSLOT 1", "R|CLUSTER GETKEYSINSLOT 1 10", "R|CLUSTER LINKS", "R|CLUSTER REPLICAS id", "W|CLUSTER SAVECONFIG",
            "A|CLUSTER FAILOVER", "A|CLUSTER RESET HARD", "A|CLUSTER FORGET id", "D|CLUSTER MEET 1.2.3.4 6379", "D|CLUSTER ADDSLOTS 1",
            "D|CLUSTER DELSLOTS 1", "D|CLUSTER SETSLOT 1 NODE id", "A|CLUSTER REPLICATE id", "X|READONLY", "X|READWRITE", "X|ASKING",
            "R|SENTINEL MASTERS", "R|SENTINEL GET-MASTER-ADDR-BY-NAME mymaster", "R|SENTINEL REPLICAS mymaster", "A|SENTINEL FAILOVER mymaster",
            "D|SENTINEL REMOVE mymaster", "D|SENTINEL SET mymaster quorum 2",
            // Unknown, modules, and resource options
            "D|JSON.GET doc", "D|FT.SEARCH idx hello", "D|NOSUCHCOMMAND", "D|TS.ADD k * 1", "X|", "X|   ",
            "R|JSON.GET doc|ro=JSON.GET", "R|FT.SEARCH idx hello|ro=FT.SEARCH;ro=JSON.GET", "D|JSON.SET doc $ 1|ro=JSON.GET",
            "D|DEL k|ro=DEL", "R|KEYS *|deny=FLUSHALL", "X|CONFIG SET maxmemory 1|deny=CONFIG SET", "R|CONFIG GET maxmemory|deny=CONFIG SET",
            "X|HGETALL h|deny=HGETALL", "X|DEBUG SLEEP 0|deny=DEBUG"
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// The table.
        /// </summary>
        /// <returns>Cases.</returns>
        public static List<RedisClassifierCase> Cases()
        {
            List<RedisClassifierCase> cases = new List<RedisClassifierCase>();
            foreach (string row in _Rows)
            {
                string[] parts = row.Split('|');
                RedisClassifierCase c = new RedisClassifierCase { Expected = parts[0], Command = parts.Length > 1 ? parts[1] : String.Empty };
                if (parts.Length > 2)
                {
                    c.Denied.Clear();
                    foreach (string opt in parts[2].Split(';', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (opt.StartsWith("ro=", StringComparison.Ordinal)) c.ReadOnly.Add(opt.Substring(3));
                        else if (opt.StartsWith("deny=", StringComparison.Ordinal)) c.Denied.Add(opt.Substring(5));
                    }
                }
                cases.Add(c);
            }
            return cases;
        }

        #endregion
    }
}
