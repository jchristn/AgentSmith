namespace Hugo.Connectors.Redis.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using StackExchange.Redis;

    /// <summary>
    /// <c>redis_get</c>: type-aware read of one key (string, hash, list, set, sorted set, stream) with element and byte
    /// caps and paging. Read. Thread-safe.
    /// </summary>
    public class RedisGetTool : RedisToolBase
    {
        #region Public-Members

        /// <summary>
        /// Input schema.
        /// </summary>
        public static readonly string Schema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"key\"],\"properties\":{"
            + "\"key\":{\"type\":\"string\",\"description\":\"Key name. Prefix with base64: for a binary key name as shown by redis_scan.\"},"
            + "\"limit\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":10000,\"description\":\"Most elements returned for a collection. Default 100. Capped by the row limit.\"},"
            + "\"offset\":{\"type\":\"integer\",\"minimum\":0,\"description\":\"First element (list index, sorted set rank) or first byte of a string. Default 0.\"},"
            + "\"cursor\":{\"type\":\"string\",\"description\":\"Continue a hash or set read with its next value, or start a stream read at an entry ID (prefix ( for exclusive).\"},"
            + "\"fields\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"maxItems\":1000,\"description\":\"Hash only: read just these fields.\"},"
            + "\"reverse\":{\"type\":\"boolean\",\"description\":\"List, sorted set, and stream: start from the end (newest stream entries, highest scores, list tail). Default false.\"}"
            + "}}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public RedisGetTool() : base(new ToolDescriptor
        {
            Name = "redis_get",
            Title = "Read a Redis key",
            Description = "Read one key whatever its type: a string value, hash fields, list elements, set members, sorted set members with scores, or stream entries. "
                + "Reports the type, TTL, and size, returns at most limit elements, cuts each value at the resource's maxValueBytes, and gives a next value for paging. "
                + "Use redis_scan first to find key names.",
            InputSchemaJson = Schema,
            BaseEffect = EffectEnum.Read,
            OutputDescription = "Header (type, size, TTL) and a value or table, plus structured JSON {key, type, ttlMs, length, offset, returned, next, value, items[], truncated}."
        })
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override void ValidateArguments(ConnectorInvocationContext context, RedisConnectionOptions options, ToolArguments arguments)
        {
            RedisScanner.ParseKey(RequiredKey(arguments));
        }

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, RedisSession session, ToolArguments arguments, CancellationToken token)
        {
            string keyText = RequiredKey(arguments);
            RedisKey key = RedisScanner.ParseKey(keyText);
            int limit = arguments.GetInt("limit", 100, 1, Math.Min(10000, context.Limits.MaxRows));
            long offset = arguments.GetLong("offset", 0, 0, Int64.MaxValue);
            bool reverse = arguments.GetBool("reverse", false);
            int maxBytes = session.Options.MaxValueBytes;

            string type = (await ExecAsync(session, "TYPE", new List<object> { key }, token).ConfigureAwait(false)).ToString() ?? "none";
            if (type == "none") return ToolResult.Error(ConnectorErrorCodes.NotFound, "Key " + Quote(keyText) + " does not exist in database " + session.DatabaseIndex() + ".");
            long ttl = (long)await ExecAsync(session, "PTTL", new List<object> { key }, token).ConfigureAwait(false);

            RedisGetPayload payload = new RedisGetPayload { Key = keyText, Type = type, TtlMs = ttl };
            List<string> columns = new List<string>();
            List<List<string?>> rows = new List<List<string?>>();
            StringBuilder body = new StringBuilder();

            switch (type)
            {
                case "string":
                    await ReadStringAsync(session, key, offset, maxBytes, payload, body, token).ConfigureAwait(false);
                    break;
                case "hash":
                    columns.AddRange(new[] { "field", "value" });
                    await ReadHashAsync(session, key, arguments, limit, maxBytes, payload, rows, token).ConfigureAwait(false);
                    break;
                case "list":
                    columns.AddRange(new[] { "index", "value" });
                    await ReadListAsync(session, key, offset, limit, reverse, maxBytes, payload, rows, token).ConfigureAwait(false);
                    break;
                case "set":
                    columns.Add("member");
                    await ReadSetAsync(session, key, arguments.GetString("cursor"), limit, maxBytes, payload, rows, token).ConfigureAwait(false);
                    break;
                case "zset":
                    columns.AddRange(new[] { "rank", "member", "score" });
                    await ReadSortedSetAsync(session, key, offset, limit, reverse, maxBytes, payload, rows, token).ConfigureAwait(false);
                    break;
                case "stream":
                    columns.AddRange(new[] { "id", "fields" });
                    await ReadStreamAsync(session, key, arguments.GetString("cursor"), limit, reverse, maxBytes, payload, rows, token).ConfigureAwait(false);
                    break;
                default:
                    body.Append("Type ").Append(type).Append(" is a module type that redis_get cannot read. Use redis_command with the module's read command (for example JSON.GET) if it is allowed on this resource.\n");
                    break;
            }

            StringBuilder text = new StringBuilder();
            text.Append("Key ").Append(Quote(keyText)).Append(": ").Append(type).Append(", ")
                .Append(payload.Length.ToString(CultureInfo.InvariantCulture)).Append(type == "string" ? " bytes" : " element(s)")
                .Append(", TTL ").Append(RedisOutput.Ttl(ttl)).Append('\n');
            if (payload.Items != null) text.Append("Showing ").Append(payload.Returned).Append(" element(s)").Append(payload.Offset.HasValue ? " from position " + payload.Offset.Value.ToString(CultureInfo.InvariantCulture) : String.Empty).Append(".\n");
            if (payload.Next != null) text.Append("More available; next: ").Append(payload.Next).Append('\n');
            if (payload.Truncated) text.Append("Some values were cut at maxValueBytes (").Append(maxBytes).Append(" bytes).\n");
            text.Append(body);
            if (rows.Any()) text.Append(TextTable.Render(columns, rows.Select(r => (IList<string?>)r).ToList(), 2000));

            ToolResult result = RedisOutput.Text(text.ToString(), payload, context.Limits);
            result.Truncated = result.Truncated || payload.Truncated;
            result.Metrics["elements"] = payload.Returned;
            return result;
        }

        #endregion

        #region Private-Methods

        private static async Task ReadStringAsync(RedisSession session, RedisKey key, long offset, int maxBytes, RedisGetPayload payload, StringBuilder body, CancellationToken token)
        {
            long length = (long)await ExecAsync(session, "STRLEN", new List<object> { key }, token).ConfigureAwait(false);
            payload.Length = length;
            payload.Offset = offset;
            if (offset >= length && length > 0)
            {
                body.Append("Offset ").Append(offset).Append(" is past the end of the value.\n");
                return;
            }
            long end = Math.Min(length, offset + maxBytes) - 1;
            RedisResult range = await ExecAsync(session, "GETRANGE", new List<object> { key, offset, end }, token).ConfigureAwait(false);
            byte[] bytes = (byte[]?)range ?? Array.Empty<byte>();
            RedisDisplayValue value = RedisDisplayValue.FromBytes(bytes, maxBytes, length - offset);
            payload.Value = value;
            payload.Returned = 1;
            payload.Truncated = value.Truncated;
            if (end + 1 < length) payload.Next = (end + 1).ToString(CultureInfo.InvariantCulture);
            body.Append(value.Binary ? "Value (binary, base64):\n" : "Value:\n").Append(value.Text).Append('\n');
        }

        private static async Task ReadHashAsync(RedisSession session, RedisKey key, ToolArguments arguments, int limit, int maxBytes, RedisGetPayload payload, List<List<string?>> rows, CancellationToken token)
        {
            payload.Length = (long)await ExecAsync(session, "HLEN", new List<object> { key }, token).ConfigureAwait(false);
            payload.Items = new List<RedisGetItem>();
            List<string> fields = arguments.GetStringList("fields");
            if (fields.Any())
            {
                List<object> args = new List<object> { key };
                args.AddRange(fields.Take(limit));
                RedisResult[] values = (RedisResult[])(await ExecAsync(session, "HMGET", args, token).ConfigureAwait(false))!;
                for (int i = 0; i < values.Length; i++)
                    AddItem(payload, rows, new RedisGetItem { Field = fields[i], Value = RedisDisplayValue.From((RedisValue)values[i], maxBytes) });
                return;
            }

            await ScanElementsAsync(session, "HSCAN", key, arguments.GetString("cursor"), limit, 2, maxBytes, payload, rows, token).ConfigureAwait(false);
        }

        private static async Task ScanElementsAsync(RedisSession session, string command, RedisKey key, string? cursorArg, int limit, int width, int maxBytes, RedisGetPayload payload, List<List<string?>> rows, CancellationToken token)
        {
            ParseElementCursor(cursorArg, out string cursor, out int skip);
            int rounds = 0;
            while (true)
            {
                RedisResult[] reply = (RedisResult[])(await ExecAsync(session, command, new List<object> { key, cursor, "COUNT", limit }, token).ConfigureAwait(false))!;
                string nextCursor = reply[0].ToString() ?? "0";
                RedisResult[] flat = (RedisResult[])reply[1]!;
                int elements = flat.Length / width;
                int taken = 0;
                for (int e = skip; e < elements; e++)
                {
                    if (payload.Returned >= limit)
                    {
                        payload.Next = cursor + "+" + (skip + taken).ToString(CultureInfo.InvariantCulture);
                        return;
                    }
                    int i = e * width;
                    RedisGetItem item = width == 2
                        ? new RedisGetItem { Field = Text((RedisValue)flat[i]), Value = RedisDisplayValue.From((RedisValue)flat[i + 1], maxBytes) }
                        : new RedisGetItem { Value = RedisDisplayValue.From((RedisValue)flat[i], maxBytes) };
                    AddItem(payload, rows, item);
                    taken++;
                }

                skip = 0;
                cursor = nextCursor;
                rounds++;
                if (cursor == "0")
                {
                    payload.Next = null;
                    return;
                }
                if (payload.Returned >= limit || rounds >= 1000)
                {
                    payload.Next = cursor;
                    return;
                }
            }
        }

        private static void ParseElementCursor(string? text, out string cursor, out int skip)
        {
            cursor = "0";
            skip = 0;
            if (String.IsNullOrWhiteSpace(text)) return;
            string[] parts = text.Trim().Split('+');
            if (parts.Length > 2
                || !UInt64.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out ulong _)
                || (parts.Length == 2 && (!Int32.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out skip) || skip < 0)))
                throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "cursor must be the next value from a previous redis_get call on this hash or set.");
            cursor = parts[0];
        }

        private static async Task ReadListAsync(RedisSession session, RedisKey key, long offset, int limit, bool reverse, int maxBytes, RedisGetPayload payload, List<List<string?>> rows, CancellationToken token)
        {
            long length = (long)await ExecAsync(session, "LLEN", new List<object> { key }, token).ConfigureAwait(false);
            payload.Length = length;
            payload.Offset = offset;
            payload.Items = new List<RedisGetItem>();
            long start = reverse ? -(offset + limit) : offset;
            long stop = reverse ? -(offset + 1) : offset + limit - 1;
            RedisResult[] values = (RedisResult[])(await ExecAsync(session, "LRANGE", new List<object> { key, start, stop }, token).ConfigureAwait(false))!;
            List<RedisResult> ordered = reverse ? Enumerable.Reverse(values).ToList() : values.ToList();
            for (int i = 0; i < ordered.Count; i++)
            {
                long index = reverse ? length - 1 - offset - i : offset + i;
                AddItem(payload, rows, new RedisGetItem { Index = index, Value = RedisDisplayValue.From((RedisValue)ordered[i], maxBytes) });
            }
            if (offset + ordered.Count < length) payload.Next = (offset + ordered.Count).ToString(CultureInfo.InvariantCulture);
        }

        private static async Task ReadSetAsync(RedisSession session, RedisKey key, string? cursorArg, int limit, int maxBytes, RedisGetPayload payload, List<List<string?>> rows, CancellationToken token)
        {
            payload.Length = (long)await ExecAsync(session, "SCARD", new List<object> { key }, token).ConfigureAwait(false);
            payload.Items = new List<RedisGetItem>();
            await ScanElementsAsync(session, "SSCAN", key, cursorArg, limit, 1, maxBytes, payload, rows, token).ConfigureAwait(false);
        }

        private static async Task ReadSortedSetAsync(RedisSession session, RedisKey key, long offset, int limit, bool reverse, int maxBytes, RedisGetPayload payload, List<List<string?>> rows, CancellationToken token)
        {
            long length = (long)await ExecAsync(session, "ZCARD", new List<object> { key }, token).ConfigureAwait(false);
            payload.Length = length;
            payload.Offset = offset;
            payload.Items = new List<RedisGetItem>();
            List<object> args = new List<object> { key, offset, offset + limit - 1 };
            if (reverse) args.Add("REV");
            args.Add("WITHSCORES");
            RedisResult[] values = (RedisResult[])(await ExecAsync(session, "ZRANGE", args, token).ConfigureAwait(false))!;
            for (int i = 0; i + 1 < values.Length; i += 2)
            {
                double score = Double.Parse(values[i + 1].ToString() ?? "0", NumberStyles.Float, CultureInfo.InvariantCulture);
                AddItem(payload, rows, new RedisGetItem { Index = offset + (i / 2), Value = RedisDisplayValue.From((RedisValue)values[i], maxBytes), Score = score });
            }
            if (offset + payload.Returned < length) payload.Next = (offset + payload.Returned).ToString(CultureInfo.InvariantCulture);
        }

        private static async Task ReadStreamAsync(RedisSession session, RedisKey key, string? cursorArg, int limit, bool reverse, int maxBytes, RedisGetPayload payload, List<List<string?>> rows, CancellationToken token)
        {
            payload.Length = (long)await ExecAsync(session, "XLEN", new List<object> { key }, token).ConfigureAwait(false);
            payload.Items = new List<RedisGetItem>();
            string start = String.IsNullOrWhiteSpace(cursorArg) ? (reverse ? "+" : "-") : cursorArg.Trim();
            string end = reverse ? "-" : "+";
            RedisResult[] entries = (RedisResult[])(await ExecAsync(session, reverse ? "XREVRANGE" : "XRANGE", new List<object> { key, start, end, "COUNT", limit }, token).ConfigureAwait(false))!;
            string? lastId = null;
            foreach (RedisResult entry in entries)
            {
                RedisResult[] parts = (RedisResult[])entry!;
                lastId = parts[0].ToString();
                Dictionary<string, RedisDisplayValue> fields = new Dictionary<string, RedisDisplayValue>(StringComparer.Ordinal);
                RedisResult[] kv = (RedisResult[])parts[1]!;
                for (int i = 0; i + 1 < kv.Length; i += 2) fields[Text((RedisValue)kv[i])] = RedisDisplayValue.From((RedisValue)kv[i + 1], maxBytes);
                RedisGetItem item = new RedisGetItem { Id = lastId, Fields = fields };
                payload.Items.Add(item);
                payload.Returned++;
                if (fields.Values.Any(v => v.Truncated)) payload.Truncated = true;
                rows.Add(new List<string?> { lastId, String.Join(", ", fields.Select(f => f.Key + "=" + f.Value.ToInline())) });
            }
            if (entries.Length >= limit && lastId != null) payload.Next = "(" + lastId;
        }

        private static void AddItem(RedisGetPayload payload, List<List<string?>> rows, RedisGetItem item)
        {
            payload.Items!.Add(item);
            payload.Returned++;
            if (item.Value != null && item.Value.Truncated) payload.Truncated = true;
            string value = item.Value == null ? "(nil)" : item.Value.ToInline();
            if (item.Field != null) rows.Add(new List<string?> { item.Field, value });
            else if (item.Score.HasValue) rows.Add(new List<string?> { item.Index?.ToString(CultureInfo.InvariantCulture), value, item.Score.Value.ToString("R", CultureInfo.InvariantCulture) });
            else if (item.Index.HasValue) rows.Add(new List<string?> { item.Index.Value.ToString(CultureInfo.InvariantCulture), value });
            else rows.Add(new List<string?> { value });
        }

        private static string Text(RedisValue value)
        {
            RedisDisplayValue v = RedisDisplayValue.From(value, 1024);
            return v.Binary ? "base64:" + v.Text : (v.Text ?? String.Empty);
        }

        #endregion
    }
}
