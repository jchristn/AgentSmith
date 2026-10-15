namespace Hugo.Connectors.Redis
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using StackExchange.Redis;

    /// <summary>
    /// Walks the keyspace with <c>SCAN</c> (never <c>KEYS</c>), bounded by a number of keys to return and a budget of keys
    /// to examine. On a cluster it visits each primary in turn; the cursor records which primary and where. Thread-safe.
    /// </summary>
    public static class RedisScanner
    {
        #region Public-Members

        /// <summary>
        /// Largest COUNT hint sent with one SCAN. Minimum 10, maximum 100000, default 1000.
        /// </summary>
        public static int MaxBatch
        {
            get
            {
                return _MaxBatch;
            }
            set
            {
                _MaxBatch = Math.Clamp(value, 10, 100000);
            }
        }

        /// <summary>
        /// Key types accepted by the TYPE filter.
        /// </summary>
        public static readonly IReadOnlyList<string> KeyTypes = new List<string> { "string", "hash", "list", "set", "zset", "stream" };

        #endregion

        #region Private-Members

        private static int _MaxBatch = 1000;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Scan one bounded page.
        /// </summary>
        /// <param name="session">Session.</param>
        /// <param name="pattern">MATCH pattern; null or empty means every key.</param>
        /// <param name="type">TYPE filter, or null.</param>
        /// <param name="wantKeys">Keys wanted; the page may hold slightly more when one SCAN batch overshoots.</param>
        /// <param name="budget">Most keys to examine.</param>
        /// <param name="cursor">Cursor from a previous page, or null to start.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">Thrown when session is null.</exception>
        /// <exception cref="ConnectorException">Thrown with <c>invalid_argument</c> for a bad cursor or type.</exception>
        public static async Task<RedisScanPage> ScanAsync(RedisSession session, string? pattern, string? type, int wantKeys, int budget, string? cursor, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(session);
            string match = String.IsNullOrEmpty(pattern) ? "*" : pattern;
            string? typeFilter = String.IsNullOrWhiteSpace(type) ? null : type.Trim().ToLowerInvariant();
            if (typeFilter != null && !KeyTypes.Contains(typeFilter))
                throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "type must be one of " + String.Join(", ", KeyTypes) + ".");

            bool cluster = session.IsCluster;
            List<IServer> servers = cluster ? session.Primaries() : new List<IServer>();
            int nodeCount = cluster ? servers.Count : 1;
            int node = 0;
            ulong position = 0;
            if (!String.IsNullOrWhiteSpace(cursor)) ParseCursor(cursor, nodeCount, out node, out position);

            RedisScanPage page = new RedisScanPage { Pattern = match, Type = typeFilter };
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            int want = Math.Max(1, wantKeys);
            long limit = Math.Max(1, budget);
            IDatabase db = session.Database();

            while (true)
            {
                token.ThrowIfCancellationRequested();
                int hint = (int)Math.Clamp(Math.Min(limit - page.Scanned, (long)Math.Max(10, want - page.Keys.Count)), 10, MaxBatch);
                List<object> args = new List<object> { position.ToString(CultureInfo.InvariantCulture), "MATCH", match, "COUNT", hint };
                if (typeFilter != null)
                {
                    args.Add("TYPE");
                    args.Add(typeFilter);
                }

                Task<RedisResult> call = cluster ? servers[node].ExecuteAsync(0, "SCAN", args, CommandFlags.None) : db.ExecuteAsync("SCAN", args, CommandFlags.None);
                RedisResult reply = await call.WaitAsync(token).ConfigureAwait(false);
                RedisResult[] parts = (RedisResult[])reply!;
                if (parts == null || parts.Length < 2) throw new ConnectorException(ConnectorErrorCodes.UpstreamError, "SCAN returned an unexpected reply.");
                position = UInt64.Parse(parts[0].ToString() ?? "0", NumberStyles.None, CultureInfo.InvariantCulture);
                RedisKey[] keys = (RedisKey[])parts[1]!;
                page.Scanned += hint;

                foreach (RedisKey key in keys)
                {
                    string display = DisplayKey(key);
                    if (!seen.Add(display)) continue;
                    page.Keys.Add(new RedisScanKey { Key = display, Type = typeFilter, Node = cluster ? RedisSession.Format(servers[node]) : null });
                    page.RawKeys.Add(key);
                    page.NodeIndexes.Add(node);
                }

                if (position == 0)
                {
                    node++;
                    if (node >= nodeCount)
                    {
                        page.Complete = true;
                        page.NextCursor = null;
                        return page;
                    }
                }

                if (page.Keys.Count >= want)
                {
                    page.NextCursor = FormatCursor(node, position);
                    return page;
                }

                if (page.Scanned >= limit)
                {
                    page.ScanLimitReached = true;
                    page.NextCursor = FormatCursor(node, position);
                    return page;
                }
            }
        }

        /// <summary>
        /// Printable form of a key: the text when it is printable UTF-8, otherwise <c>base64:...</c>.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <returns>Text.</returns>
        public static string DisplayKey(RedisKey key)
        {
            byte[]? bytes = (byte[]?)key;
            if (bytes == null) return String.Empty;
            RedisDisplayValue v = RedisDisplayValue.FromBytes(bytes, Int32.MaxValue, bytes.LongLength);
            return v.Binary ? "base64:" + v.Text : (v.Text ?? String.Empty);
        }

        /// <summary>
        /// Parse a key argument: <c>base64:...</c> selects raw bytes, anything else is UTF-8 text.
        /// </summary>
        /// <param name="text">Key text.</param>
        /// <returns>Key.</returns>
        /// <exception cref="ConnectorException">Thrown with <c>invalid_argument</c> for bad base64.</exception>
        public static RedisKey ParseKey(string text)
        {
            if (text != null && text.StartsWith("base64:", StringComparison.Ordinal))
            {
                try
                {
                    return Convert.FromBase64String(text.Substring("base64:".Length));
                }
                catch (FormatException)
                {
                    throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Key '" + text + "' starts with base64: but is not valid base64.");
                }
            }
            return text ?? String.Empty;
        }

        /// <summary>
        /// Format a cursor.
        /// </summary>
        /// <param name="node">Primary index.</param>
        /// <param name="position">SCAN cursor on that primary.</param>
        /// <returns>Cursor text, for example <c>0:1792</c>.</returns>
        public static string FormatCursor(int node, ulong position)
        {
            return node.ToString(CultureInfo.InvariantCulture) + ":" + position.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Parse a cursor.
        /// </summary>
        /// <param name="cursor">Cursor text.</param>
        /// <param name="nodeCount">Number of primaries.</param>
        /// <param name="node">Primary index.</param>
        /// <param name="position">SCAN cursor.</param>
        /// <exception cref="ConnectorException">Thrown with <c>invalid_argument</c> when the cursor is malformed or names
        /// a primary that does not exist.</exception>
        public static void ParseCursor(string cursor, int nodeCount, out int node, out ulong position)
        {
            string[] parts = (cursor ?? String.Empty).Trim().Split(':');
            if (parts.Length != 2
                || !Int32.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out node)
                || !UInt64.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out position))
                throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "cursor must be the nextCursor value of a previous call, for example 0:1792.");
            if (node < 0 || node >= Math.Max(1, nodeCount))
                throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "cursor refers to node " + node + ", but the resource has " + nodeCount + " primary node(s). Start a new scan.");
        }

        /// <summary>
        /// Sum of DBSIZE over the primaries, or null when it cannot be read.
        /// </summary>
        /// <param name="session">Session.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Key count or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when session is null.</exception>
        public static async Task<long?> DatabaseSizeAsync(RedisSession session, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(session);
            try
            {
                if (!session.IsCluster)
                {
                    RedisResult r = await session.Database().ExecuteAsync("DBSIZE").WaitAsync(token).ConfigureAwait(false);
                    return (long)r;
                }
                long total = 0;
                foreach (IServer server in session.Primaries())
                {
                    RedisResult r = await server.ExecuteAsync(0, "DBSIZE", new List<object>(), CommandFlags.None).WaitAsync(token).ConfigureAwait(false);
                    total += (long)r;
                }
                return total;
            }
            catch (RedisException)
            {
                return null;
            }
        }

        #endregion
    }
}
