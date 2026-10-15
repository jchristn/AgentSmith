namespace Hugo.Connectors.Ssh.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Renci.SshNet;
    using Renci.SshNet.Sftp;

    /// <summary>
    /// <c>ssh_list_directory</c>: list a directory over SFTP. Read. Thread-safe.
    /// </summary>
    public class SshListDirectoryTool : SshToolBase
    {
        #region Public-Members

        /// <summary>
        /// Maximum entries scanned per call before the listing is reported as incomplete. Minimum 100, maximum 1000000,
        /// default 100000.
        /// </summary>
        public int MaxScanEntries
        {
            get
            {
                return _MaxScanEntries;
            }
            set
            {
                _MaxScanEntries = Math.Clamp(value, 100, 1000000);
            }
        }

        #endregion

        #region Private-Members

        private const string _Schema = @"{
  ""type"": ""object"",
  ""properties"": {
    ""path"": {
      ""type"": ""string"",
      ""description"": ""Directory to list, for example /var/log. Defaults to the resource's workingDirectory, or the login directory. Relative paths resolve the same way.""
    },
    ""includeHidden"": {
      ""type"": ""boolean"",
      ""description"": ""Include entries whose names start with a dot. Default false.""
    },
    ""limit"": {
      ""type"": ""integer"",
      ""minimum"": 1,
      ""description"": ""Maximum entries to return (directories first, then by name). Default and maximum: the call's row limit.""
    }
  },
  ""additionalProperties"": false
}";

        private int _MaxScanEntries = 100000;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public SshListDirectoryTool() : base(new ToolDescriptor
        {
            Name = "ssh_list_directory",
            Title = "List a directory",
            Description = "List a directory on the SSH host over SFTP: name, type, size in bytes, permissions, and last modified time (UTC). "
                + "Directories come first, then files by name, and the listing is capped at the row limit with the total count reported. "
                + "Use it instead of ssh_exec with ls; it is always Read.",
            InputSchemaJson = _Schema,
            BaseEffect = EffectEnum.Read,
            OutputDescription = "A '#' header with the total, then a table with columns name, type, size, mode, modified."
        })
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, SshConnectionOptions options, CancellationToken token)
        {
            string? requested = arguments.GetString("path");
            string display = String.IsNullOrWhiteSpace(requested) ? (options.WorkingDirectory ?? ".") : options.ResolvePath(requested);
            string path = SftpPath(display);
            bool includeHidden = arguments.GetBool("includeHidden", false);
            int limit = arguments.GetInt("limit", context.Limits.MaxRows, 1, context.Limits.MaxRows);

            SftpClient sftp = await GetSftpAsync(context, options, token).ConfigureAwait(false);
            List<ISftpFile> entries = new List<ISftpFile>();
            int hidden = 0;
            bool scanCapped = false;
            try
            {
                await foreach (ISftpFile file in sftp.ListDirectoryAsync(path, token).ConfigureAwait(false))
                {
                    if (file.Name == "." || file.Name == "..") continue;
                    if (!includeHidden && file.Name.StartsWith('.'))
                    {
                        hidden++;
                        continue;
                    }
                    entries.Add(file);
                    if (entries.Count >= _MaxScanEntries)
                    {
                        scanCapped = true;
                        break;
                    }
                }
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                throw MapSftp(context, e, display);
            }

            List<ISftpFile> sorted = entries
                .OrderBy(f => f.IsDirectory ? 0 : 1)
                .ThenBy(f => f.Name, StringComparer.Ordinal)
                .ToList();
            List<ISftpFile> shown = sorted.Take(limit).ToList();

            List<IList<string?>> rows = new List<IList<string?>>();
            foreach (ISftpFile f in shown)
            {
                rows.Add(new List<string?>
                {
                    f.Name,
                    TypeOf(f),
                    f.IsDirectory ? "-" : f.Length.ToString(CultureInfo.InvariantCulture),
                    ModeOf(f),
                    f.LastWriteTimeUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
                });
            }

            StringBuilder header = new StringBuilder();
            header.Append("# ").Append(display).Append(": ").Append(entries.Count.ToString(CultureInfo.InvariantCulture)).Append(scanCapped ? "+" : String.Empty).Append(" entries");
            if (shown.Count < entries.Count) header.Append(", showing the first ").Append(shown.Count.ToString(CultureInfo.InvariantCulture));
            if (hidden > 0) header.Append("; ").Append(hidden.ToString(CultureInfo.InvariantCulture)).Append(" hidden entries omitted (set includeHidden=true)");
            if (scanCapped) header.Append("; the directory is too large to scan completely");

            ToolResult result = new ToolResult();
            string table = TextTable.Render(new List<string> { "name", "type", "size", "mode", "modified" }, rows, 200);
            result.Text = header.ToString() + "\n" + OutputLimiter.Head(table, Math.Max(1024, OutputBudget(context, options) - 512), 0, result);
            if (shown.Count < entries.Count || scanCapped) result.Truncated = true;
            result.Metrics["rows"] = shown.Count;
            result.Metrics["totalEntries"] = entries.Count;
            return result;
        }

        #endregion

        #region Private-Methods

        private static string TypeOf(ISftpFile f)
        {
            if (f.IsDirectory) return "dir";
            if (f.IsSymbolicLink) return "link";
            if (f.IsRegularFile) return "file";
            if (f.IsSocket) return "socket";
            if (f.IsNamedPipe) return "fifo";
            if (f.IsBlockDevice) return "block";
            if (f.IsCharacterDevice) return "char";
            return "other";
        }

        private static string ModeOf(ISftpFile f)
        {
            char type = f.IsDirectory ? 'd' : f.IsSymbolicLink ? 'l' : f.IsRegularFile ? '-' : f.IsSocket ? 's' : f.IsNamedPipe ? 'p' : f.IsBlockDevice ? 'b' : f.IsCharacterDevice ? 'c' : '?';
            StringBuilder sb = new StringBuilder();
            sb.Append(type);
            sb.Append(f.OwnerCanRead ? 'r' : '-').Append(f.OwnerCanWrite ? 'w' : '-').Append(f.OwnerCanExecute ? 'x' : '-');
            sb.Append(f.GroupCanRead ? 'r' : '-').Append(f.GroupCanWrite ? 'w' : '-').Append(f.GroupCanExecute ? 'x' : '-');
            sb.Append(f.OthersCanRead ? 'r' : '-').Append(f.OthersCanWrite ? 'w' : '-').Append(f.OthersCanExecute ? 'x' : '-');
            return sb.ToString();
        }

        #endregion
    }
}
