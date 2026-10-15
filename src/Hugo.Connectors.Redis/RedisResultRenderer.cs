namespace Hugo.Connectors.Redis
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Text.Json;
    using StackExchange.Redis;

    /// <summary>
    /// Renders a raw <see cref="RedisResult"/> (from <c>redis_command</c>) the way redis-cli does, with a cap on the
    /// number of elements and on each value's bytes, and as structured JSON. Not thread-safe; use one instance per call.
    /// </summary>
    public class RedisResultRenderer
    {
        #region Public-Members

        /// <summary>
        /// Most elements rendered across all nesting levels. Minimum 1, default 500.
        /// </summary>
        public int MaxElements
        {
            get
            {
                return _MaxElements;
            }
            set
            {
                _MaxElements = Math.Max(1, value);
            }
        }

        /// <summary>
        /// Byte cap of each value. Minimum 16, default 65536.
        /// </summary>
        public int MaxValueBytes
        {
            get
            {
                return _MaxValueBytes;
            }
            set
            {
                _MaxValueBytes = Math.Max(16, value);
            }
        }

        /// <summary>
        /// Most nesting levels rendered. Minimum 1, default 8.
        /// </summary>
        public int MaxDepth
        {
            get
            {
                return _MaxDepth;
            }
            set
            {
                _MaxDepth = Math.Max(1, value);
            }
        }

        /// <summary>
        /// Whether the last render omitted elements or cut values.
        /// </summary>
        public bool Truncated { get; private set; } = false;

        /// <summary>
        /// Elements rendered by the last render.
        /// </summary>
        public int Elements { get; private set; } = 0;

        #endregion

        #region Private-Members

        private int _MaxElements = 500;
        private int _MaxValueBytes = 65536;
        private int _MaxDepth = 8;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Render as redis-cli style text.
        /// </summary>
        /// <param name="result">Result.</param>
        /// <returns>Text.</returns>
        /// <exception cref="ArgumentNullException">Thrown when result is null.</exception>
        public string RenderText(RedisResult result)
        {
            ArgumentNullException.ThrowIfNull(result);
            Truncated = false;
            Elements = 0;
            StringBuilder sb = new StringBuilder();
            AppendText(sb, result, String.Empty, 0);
            return sb.ToString();
        }

        /// <summary>
        /// Render as a JSON value (string, number, null, or nested arrays; binary values as <c>{"base64": ...}</c>).
        /// </summary>
        /// <param name="result">Result.</param>
        /// <returns>JSON text.</returns>
        /// <exception cref="ArgumentNullException">Thrown when result is null.</exception>
        public string RenderJson(RedisResult result)
        {
            ArgumentNullException.ThrowIfNull(result);
            int budget = MaxElements;
            using (MemoryStream ms = new MemoryStream())
            {
                using (Utf8JsonWriter writer = new Utf8JsonWriter(ms))
                {
                    WriteJson(writer, result, 0, ref budget);
                }
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        #endregion

        #region Private-Methods

        private void AppendText(StringBuilder sb, RedisResult result, string indent, int depth)
        {
            if (result.IsNull)
            {
                sb.Append(result.Resp2Type == ResultType.Array ? "(empty array)" : "(nil)").Append('\n');
                Elements++;
                return;
            }

            switch (result.Resp2Type)
            {
                case ResultType.Integer:
                    sb.Append("(integer) ").Append(((long)result).ToString(CultureInfo.InvariantCulture)).Append('\n');
                    Elements++;
                    return;
                case ResultType.Error:
                    sb.Append("(error) ").Append(result.ToString()).Append('\n');
                    Elements++;
                    return;
                case ResultType.Array:
                    RedisResult[] items = (RedisResult[])result!;
                    if (items.Length == 0)
                    {
                        sb.Append("(empty array)\n");
                        Elements++;
                        return;
                    }
                    if (depth >= MaxDepth)
                    {
                        sb.Append("(nested array of ").Append(items.Length).Append(" elements omitted)\n");
                        Truncated = true;
                        return;
                    }
                    int width = items.Length.ToString(CultureInfo.InvariantCulture).Length;
                    for (int i = 0; i < items.Length; i++)
                    {
                        if (Elements >= MaxElements)
                        {
                            sb.Append(indent).Append("... ").Append(items.Length - i).Append(" more element(s) omitted\n");
                            Truncated = true;
                            return;
                        }
                        string label = (i + 1).ToString(CultureInfo.InvariantCulture).PadLeft(width) + ") ";
                        sb.Append(i == 0 ? String.Empty : indent).Append(label);
                        AppendText(sb, items[i], indent + new string(' ', label.Length), depth + 1);
                    }
                    return;
                default:
                    RedisDisplayValue v = RedisDisplayValue.From((RedisValue)result, MaxValueBytes);
                    if (v.Truncated) Truncated = true;
                    sb.Append(result.Resp2Type == ResultType.SimpleString && !v.Binary ? v.Text : v.ToInline()).Append('\n');
                    Elements++;
                    return;
            }
        }

        private void WriteJson(Utf8JsonWriter writer, RedisResult result, int depth, ref int budget)
        {
            if (result.IsNull)
            {
                writer.WriteNullValue();
                return;
            }

            switch (result.Resp2Type)
            {
                case ResultType.Integer:
                    writer.WriteNumberValue((long)result);
                    budget--;
                    return;
                case ResultType.Error:
                    writer.WriteStartObject();
                    writer.WriteString("error", result.ToString());
                    writer.WriteEndObject();
                    budget--;
                    return;
                case ResultType.Array:
                    RedisResult[] items = (RedisResult[])result!;
                    writer.WriteStartArray();
                    if (depth < MaxDepth)
                    {
                        foreach (RedisResult item in items)
                        {
                            if (budget <= 0) break;
                            WriteJson(writer, item, depth + 1, ref budget);
                        }
                    }
                    writer.WriteEndArray();
                    return;
                default:
                    RedisDisplayValue v = RedisDisplayValue.From((RedisValue)result, MaxValueBytes);
                    if (v.Binary)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("base64", v.Text);
                        writer.WriteEndObject();
                    }
                    else
                    {
                        writer.WriteStringValue(v.Text);
                    }
                    budget--;
                    return;
            }
        }

        #endregion
    }
}
