namespace Hugo.Connectors.Storage
{
    using System;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Blobject.Core;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>storage_read</c> (Read): read an object, or a byte range of it, as text or base64. Content is bounded by the
    /// resource's <c>maxObjectReadBytes</c> and the call's output limits; truncated reads report the offset to continue
    /// from. Blobject has no ranged GET, so a range is served by streaming and discarding the bytes before the offset.
    /// Thread-safe.
    /// </summary>
    public class StorageReadTool : StorageToolBase
    {
        #region Public-Members

        /// <summary>
        /// Tool name.
        /// </summary>
        public const string ToolName = "storage_read";

        /// <summary>
        /// Input schema shared by every storage connector.
        /// </summary>
        public const string InputSchema =
            "{\"type\":\"object\",\"properties\":{"
            + "\"key\":{\"type\":\"string\",\"minLength\":1,\"maxLength\":1024,\"description\":\"Full object key, for example 'logs/app.log'.\"},"
            + "\"offset\":{\"type\":\"integer\",\"minimum\":0,\"description\":\"First byte to return. Default 0. Use nextOffset from a truncated read to continue.\"},"
            + "\"length\":{\"type\":\"integer\",\"minimum\":1,\"description\":\"Maximum bytes to return. Capped by the resource read limit and the output limit.\"},"
            + "\"encoding\":{\"type\":\"string\",\"enum\":[\"auto\",\"text\",\"base64\"],\"description\":\"auto (default) returns text for textual content types or valid UTF-8 and base64 otherwise.\"}"
            + "},\"required\":[\"key\"],\"additionalProperties\":false}";

        /// <summary>
        /// Largest object a read will touch on providers whose Blobject client downloads the whole object into memory
        /// before streaming (Google Cloud Storage). Minimum 1 MiB, maximum 1 GiB, default 64 MiB.
        /// </summary>
        public long MaxBufferedObjectBytes
        {
            get
            {
                return _MaxBufferedObjectBytes;
            }
            set
            {
                _MaxBufferedObjectBytes = Math.Clamp(value, 1024L * 1024L, 1024L * 1024L * 1024L);
            }
        }

        /// <summary>
        /// Bytes reserved in the output budget for the header lines. Minimum 0, maximum 4096, default 512.
        /// </summary>
        public int HeaderReserveBytes
        {
            get
            {
                return _HeaderReserveBytes;
            }
            set
            {
                _HeaderReserveBytes = Math.Clamp(value, 0, 4096);
            }
        }

        #endregion

        #region Private-Members

        private long _MaxBufferedObjectBytes = 64L * 1024L * 1024L;
        private int _HeaderReserveBytes = 512;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="provider">Provider.</param>
        public StorageReadTool(StorageProviderEnum provider) : base(provider, BuildDescriptor())
        {
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override EffectClassification Classify(ConnectorInvocationContext context, ToolArguments arguments)
        {
            ArgumentNullException.ThrowIfNull(arguments);
            string? key = arguments.GetString("key");
            EffectClassification? rejected = CheckKey(context, key, "key", false);
            if (rejected != null) return rejected;
            return new EffectClassification(EffectEnum.Read, "reading an object is read-only", "Read '" + key + "' in " + Where(context));
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(
            ConnectorInvocationContext context,
            ToolArguments arguments,
            StorageSession session,
            StorageOptions options,
            CancellationToken token)
        {
            string key = StorageKeyPolicy.ValidateKey(arguments.GetString("key"), options.PrefixRestriction);
            long offset = arguments.GetLong("offset", 0, 0, Int64.MaxValue);
            string mode = (arguments.GetString("encoding") ?? "auto").Trim().ToLowerInvariant();
            if (mode != "auto" && mode != "text" && mode != "base64")
                throw new ConnectorException("invalid_argument", "Argument 'encoding' must be auto, text, or base64.");

            int contentBudget = Math.Max(256, OutputBudget(context, options) - _HeaderReserveBytes);
            long requested = arguments.GetLong("length", contentBudget, 1, Int64.MaxValue);

            BlobMetadata md = await session.Client.GetMetadataAsync(key, token).ConfigureAwait(false);
            long size = md.ContentLength;
            if (session.BuffersWholeObjectOnRead && size > _MaxBufferedObjectBytes)
                throw new ConnectorException(
                    "too_large",
                    "Object '" + key + "' is " + StorageContent.FormatBytes(size) + ". This provider's client downloads whole objects, so reads are limited to objects up to "
                    + StorageContent.FormatBytes(_MaxBufferedObjectBytes) + ".");

            StorageReadResult read = new StorageReadResult();
            read.Key = key;
            read.ContentType = String.IsNullOrEmpty(md.ContentType) ? null : md.ContentType;
            read.Size = size;
            read.Offset = offset;

            byte[] data = Array.Empty<byte>();
            if (offset < size)
            {
                int toRead = (int)Math.Min(Math.Min(requested, contentBudget), size - offset);
                data = await ReadRangeAsync(session, key, offset, toRead, token).ConfigureAwait(false);
            }

            Encode(read, data, mode, contentBudget, context.Limits.MaxLines);
            long end = offset + read.BytesReturned;
            read.Truncated = end < size;
            read.NextOffset = read.Truncated ? end : null;

            ToolResult result = new ToolResult();
            result.Text = Render(read, offset >= size && size > 0);
            result.StructuredJson = StorageJson.Serialize(read);
            result.MimeType = "text/plain";
            result.Truncated = read.Truncated;
            result.OriginalBytes = size;
            result.Metrics["bytes"] = read.BytesReturned;
            return result;
        }

        #endregion

        #region Private-Methods

        private static ToolDescriptor BuildDescriptor()
        {
            ToolDescriptor d = new ToolDescriptor();
            d.Name = ToolName;
            d.Title = "Read object";
            d.Description =
                "Read an object's content. Textual objects (text/*, JSON, XML, YAML, CSV, or valid UTF-8) come back as text; "
                + "binary objects come back as base64. Output is capped by the resource's read limit and the output limit; when "
                + "truncated, the result gives nextOffset so you can read the next range with offset. Check size first with "
                + "storage_get_metadata for large objects, and prefer small ranges over reading huge files.";
            d.InputSchemaJson = InputSchema;
            d.BaseEffect = EffectEnum.Read;
            d.OutputDescription = "Header lines (key, size, content type, range, encoding) followed by the content.";
            return d;
        }

        private static async Task<byte[]> ReadRangeAsync(StorageSession session, string key, long offset, int count, CancellationToken token)
        {
            using (BlobData blob = await session.Client.GetStreamAsync(key, token).ConfigureAwait(false))
            {
                Stream stream = blob.Data;
                byte[] skip = new byte[65536];
                long remaining = offset;
                while (remaining > 0)
                {
                    token.ThrowIfCancellationRequested();
                    int n = await stream.ReadAsync(skip, 0, (int)Math.Min(skip.Length, remaining), token).ConfigureAwait(false);
                    if (n <= 0) return Array.Empty<byte>();
                    remaining -= n;
                }

                byte[] buffer = new byte[count];
                int total = 0;
                while (total < count)
                {
                    int n = await stream.ReadAsync(buffer, total, count - total, token).ConfigureAwait(false);
                    if (n <= 0) break;
                    total += n;
                }
                if (total == count) return buffer;
                byte[] trimmed = new byte[total];
                Buffer.BlockCopy(buffer, 0, trimmed, 0, total);
                return trimmed;
            }
        }

        private static void Encode(StorageReadResult read, byte[] data, string mode, int contentBudget, int maxLines)
        {
            bool partial = read.Offset + data.Length < read.Size;
            bool textual = StorageContent.IsTextualContentType(read.ContentType);
            string text;
            int used;

            bool asText;
            if (mode == "base64")
            {
                asText = false;
                text = String.Empty;
                used = 0;
            }
            else if (StorageContent.TryDecodeUtf8(data, data.Length, partial, out text, out used))
            {
                asText = true;
            }
            else if (mode == "text" || textual)
            {
                used = partial ? StorageContent.CompleteUtf8Length(data, data.Length) : data.Length;
                text = Encoding.UTF8.GetString(data, 0, used);
                asText = true;
            }
            else
            {
                asText = false;
                used = 0;
            }

            if (asText)
            {
                if (maxLines > 0)
                {
                    int lines = 0;
                    for (int i = 0; i < text.Length; i++)
                    {
                        if (text[i] != '\n') continue;
                        lines++;
                        if (lines >= maxLines && i < text.Length - 1)
                        {
                            text = text.Substring(0, i + 1);
                            used = Encoding.UTF8.GetByteCount(text);
                            break;
                        }
                    }
                }
                read.Encoding = "text";
                read.Content = text;
                read.BytesReturned = used;
                return;
            }

            int rawBudget = Math.Max(3, (contentBudget / 4) * 3);
            int count = Math.Min(data.Length, rawBudget);
            read.Encoding = "base64";
            read.Content = Convert.ToBase64String(data, 0, count);
            read.BytesReturned = count;
        }

        private static string Render(StorageReadResult read, bool beyondEnd)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Object '").Append(read.Key).Append("' (").Append(read.Size).Append(" bytes");
            if (read.ContentType != null) sb.Append(", ").Append(read.ContentType);
            sb.Append(")\n");
            if (beyondEnd)
            {
                sb.Append("Offset ").Append(read.Offset).Append(" is at or past the end of the object; nothing to return.\n");
                return sb.ToString();
            }
            if (read.BytesReturned == 0)
            {
                sb.Append("The object is empty.\n");
                return sb.ToString();
            }
            sb.Append("bytes ").Append(read.Offset).Append('-').Append(read.Offset + read.BytesReturned - 1).Append(", encoding ").Append(read.Encoding).Append('\n');
            if (read.Truncated)
                sb.Append("Truncated: returned ").Append(read.BytesReturned).Append(" of ").Append(read.Size).Append(" bytes. Continue with offset ").Append(read.NextOffset).Append(".\n");
            sb.Append("---\n");
            sb.Append(read.Content);
            if (!read.Content.EndsWith('\n')) sb.Append('\n');
            return sb.ToString();
        }

        #endregion
    }
}
