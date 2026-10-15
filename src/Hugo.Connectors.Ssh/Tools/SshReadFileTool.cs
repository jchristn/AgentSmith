namespace Hugo.Connectors.Ssh.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Renci.SshNet;
    using Renci.SshNet.Sftp;

    /// <summary>
    /// <c>ssh_read_file</c>: read a file over SFTP by byte range or line range. Read. Thread-safe.
    /// </summary>
    public class SshReadFileTool : SshToolBase
    {
        #region Private-Members

        private const string _Schema = @"{
  ""type"": ""object"",
  ""properties"": {
    ""path"": {
      ""type"": ""string"",
      ""minLength"": 1,
      ""description"": ""File to read, for example /etc/nginx/nginx.conf. Relative paths resolve against the resource's workingDirectory, or the login directory.""
    },
    ""offset"": {
      ""type"": ""integer"",
      ""minimum"": 0,
      ""description"": ""Byte offset to start reading at. Default 0. Use it to page through large files; the result says where the next chunk starts.""
    },
    ""length"": {
      ""type"": ""integer"",
      ""minimum"": 1,
      ""description"": ""Maximum bytes to read. Defaults to, and is capped by, the resource's maxFileReadBytes (default 1 MiB) and the call's output limit.""
    },
    ""startLine"": {
      ""type"": ""integer"",
      ""minimum"": 1,
      ""description"": ""First line to return (1-based). Switches to line mode; do not combine with offset or length. Line mode only sees the first maxFileReadBytes of the file.""
    },
    ""lineCount"": {
      ""type"": ""integer"",
      ""minimum"": 1,
      ""description"": ""Lines to return in line mode. Default and maximum: the call's line limit.""
    }
  },
  ""required"": [""path""],
  ""additionalProperties"": false
}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public SshReadFileTool() : base(new ToolDescriptor
        {
            Name = "ssh_read_file",
            Title = "Read a file",
            Description = "Read a file on the SSH host over SFTP, either a byte range (offset, length) or a line range (startLine, lineCount). "
                + "Use it for configuration files and other text; use ssh_tail_file for the end of logs. Output is bounded: the header states the file size and the range returned, "
                + "and tells you the offset to continue from. Binary content (NUL bytes or invalid UTF-8) is returned as base64 in byte mode; line mode refuses it with invalid_argument.",
            InputSchemaJson = _Schema,
            BaseEffect = EffectEnum.Read,
            OutputDescription = "A '#' header with the path, size, and range, followed by the content."
        })
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, SshConnectionOptions options, CancellationToken token)
        {
            string display = options.ResolvePath(arguments.GetRequiredString("path"));
            string path = SftpPath(display);
            bool lineMode = arguments.Has("startLine") || arguments.Has("lineCount");
            if (lineMode && (arguments.Has("offset") || arguments.Has("length")))
                throw new ConnectorException("invalid_argument", "Use either offset and length, or startLine and lineCount, not both.");

            int budget = Math.Max(1024, OutputBudget(context, options) - 512);
            SftpClient sftp = await GetSftpAsync(context, options, token).ConfigureAwait(false);

            SftpFileAttributes attributes;
            try
            {
                attributes = await sftp.GetAttributesAsync(path, token).ConfigureAwait(false);
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                throw MapSftp(context, e, display);
            }
            if (attributes.IsDirectory) return ToolResult.Error("invalid_argument", "'" + display + "' is a directory. Use ssh_list_directory.");
            long size = attributes.Size;

            ToolResult result = new ToolResult();
            if (lineMode)
            {
                int startLine = arguments.GetInt("startLine", 1, 1, Int32.MaxValue);
                int lineCount = arguments.GetInt("lineCount", context.Limits.MaxLines, 1, context.Limits.MaxLines);
                int want = (int)Math.Min(size, options.MaxFileReadBytes);
                byte[] data = await ReadRangeAsync(context, sftp, path, display, 0, want, token).ConfigureAwait(false);
                if (SshBinaryDetector.LooksBinary(data))
                    return ToolResult.Error("invalid_argument", "'" + display + "' looks binary (it contains NUL bytes or is not valid UTF-8), and line mode reads text only. Call ssh_read_file again with offset and length instead of startLine and lineCount to get the bytes as base64.");

                string text = Encoding.UTF8.GetString(data);
                List<string> lines = SplitLines(text);
                bool partial = size > data.Length;
                if (partial && lines.Count > 0 && !text.EndsWith('\n')) lines.RemoveAt(lines.Count - 1);
                int first = startLine - 1;
                List<string> selected = lines.Skip(first).Take(lineCount).ToList();

                StringBuilder header = new StringBuilder();
                header.Append("# ").Append(display).Append(" (").Append(size.ToString(CultureInfo.InvariantCulture)).Append(" bytes)");
                if (selected.Count == 0)
                {
                    header.Append(": no lines at or after line ").Append(startLine.ToString(CultureInfo.InvariantCulture));
                    header.Append(partial ? " within the first " + data.Length.ToString(CultureInfo.InvariantCulture) + " bytes (maxFileReadBytes); use ssh_tail_file or byte mode for later content" : " (the file has " + lines.Count.ToString(CultureInfo.InvariantCulture) + " lines)");
                    result.Text = header.ToString() + "\n";
                    return result;
                }
                int last = first + selected.Count;
                header.Append(": lines ").Append(startLine.ToString(CultureInfo.InvariantCulture)).Append('-').Append(last.ToString(CultureInfo.InvariantCulture));
                if (!partial) header.Append(" of ").Append(lines.Count.ToString(CultureInfo.InvariantCulture));
                else header.Append(" (only the first ").Append(data.Length.ToString(CultureInfo.InvariantCulture)).Append(" bytes were scanned)");
                if (last < lines.Count) header.Append("; continue with startLine=").Append((last + 1).ToString(CultureInfo.InvariantCulture));
                string body = OutputLimiter.Head(String.Concat(selected), budget, context.Limits.MaxLines, result);
                result.Text = header.ToString() + "\n" + body;
                result.Metrics["bytes"] = data.Length;
                return result;
            }

            long offset = arguments.GetLong("offset", 0, 0, Int64.MaxValue);
            if (offset > size) return ToolResult.Error("invalid_argument", "The offset " + offset.ToString(CultureInfo.InvariantCulture) + " is beyond the end of '" + display + "' (" + size.ToString(CultureInfo.InvariantCulture) + " bytes).");
            int length = arguments.GetInt("length", options.MaxFileReadBytes, 1, options.MaxFileReadBytes);
            length = Math.Min(length, budget);
            length = (int)Math.Min(length, size - offset);
            byte[] chunk = await ReadRangeAsync(context, sftp, path, display, offset, length, token).ConfigureAwait(false);
            long end = offset + chunk.Length;

            StringBuilder head = new StringBuilder();
            head.Append("# ").Append(display).Append(" (").Append(size.ToString(CultureInfo.InvariantCulture)).Append(" bytes): bytes ")
                .Append(offset.ToString(CultureInfo.InvariantCulture)).Append('-').Append(end.ToString(CultureInfo.InvariantCulture));
            if (end < size) head.Append("; more data follows, continue with offset=").Append(end.ToString(CultureInfo.InvariantCulture));

            string content;
            if (SshBinaryDetector.LooksBinary(chunk))
            {
                int b64 = Math.Min(chunk.Length, budget * 3 / 4);
                head.Append("; binary content shown as base64");
                if (b64 < chunk.Length) head.Append(" (first ").Append(b64.ToString(CultureInfo.InvariantCulture)).Append(" bytes)");
                content = Convert.ToBase64String(chunk, 0, b64);
                result.MimeType = "text/plain";
            }
            else
            {
                content = DecodeUtf8(chunk);
            }
            string bounded = OutputLimiter.Head(content, budget, context.Limits.MaxLines, result);
            result.OriginalBytes = Math.Max(result.OriginalBytes, size);
            if (end < size) result.Truncated = true;
            result.Text = head.ToString() + "\n" + bounded;
            result.Metrics["bytes"] = chunk.Length;
            return result;
        }

        #endregion

        #region Private-Methods

        private static async Task<byte[]> ReadRangeAsync(ConnectorInvocationContext context, SftpClient sftp, string path, string display, long offset, int length, CancellationToken token)
        {
            if (length <= 0) return Array.Empty<byte>();
            try
            {
                using (SftpFileStream stream = await sftp.OpenAsync(path, FileMode.Open, FileAccess.Read, token).ConfigureAwait(false))
                {
                    if (offset > 0) stream.Seek(offset, SeekOrigin.Begin);
                    byte[] buffer = new byte[length];
                    int total = 0;
                    while (total < length)
                    {
                        token.ThrowIfCancellationRequested();
                        int read = await stream.ReadAsync(buffer, total, length - total, token).ConfigureAwait(false);
                        if (read <= 0) break;
                        total += read;
                    }
                    if (total == length) return buffer;
                    byte[] trimmed = new byte[total];
                    Array.Copy(buffer, trimmed, total);
                    return trimmed;
                }
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                throw MapSftp(context, e, display);
            }
        }

        private static List<string> SplitLines(string text)
        {
            List<string> lines = new List<string>();
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != '\n') continue;
                lines.Add(text.Substring(start, i - start + 1));
                start = i + 1;
            }
            if (start < text.Length) lines.Add(text.Substring(start));
            return lines;
        }

        private static string DecodeUtf8(byte[] data)
        {
            int start = 0;
            while (start < data.Length && start < 3 && (data[start] & 0xC0) == 0x80) start++;
            return Encoding.UTF8.GetString(data, start, data.Length - start);
        }

        #endregion
    }
}
