namespace Hugo.Connectors.Ssh.Tools
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Renci.SshNet;
    using Renci.SshNet.Sftp;

    /// <summary>
    /// <c>ssh_write_file</c>: create a file over SFTP, or replace one when <c>overwrite</c> is true. Classified Write,
    /// or Destructive when <c>overwrite</c> is true (the classifier does not contact the host, so it treats every
    /// overwrite request as Destructive; execution refuses to replace an existing file unless <c>overwrite</c> is true).
    /// Thread-safe.
    /// </summary>
    public class SshWriteFileTool : SshToolBase
    {
        #region Private-Members

        private const string _Schema = @"{
  ""type"": ""object"",
  ""properties"": {
    ""path"": {
      ""type"": ""string"",
      ""minLength"": 1,
      ""description"": ""File to write, for example /tmp/report.txt. Relative paths resolve against the resource's workingDirectory, or the login directory. The parent directory must exist.""
    },
    ""content"": {
      ""type"": ""string"",
      ""description"": ""Full file content. Text is written as UTF-8 unless encoding is base64. Size is capped by the resource's maxFileWriteBytes (default 1 MiB).""
    },
    ""encoding"": {
      ""type"": ""string"",
      ""enum"": [""utf8"", ""base64""],
      ""description"": ""How content is encoded. Default utf8.""
    },
    ""overwrite"": {
      ""type"": ""boolean"",
      ""description"": ""Replace the file if it already exists. Default false, in which case writing to an existing path fails with already_exists. Setting it true makes the call Destructive, which usually requires approval.""
    },
    ""mode"": {
      ""type"": ""string"",
      ""pattern"": ""^0?[0-7]{3}$"",
      ""description"": ""Optional octal permissions to set after writing, for example \""0644\"" or \""600\"".""
    }
  },
  ""required"": [""path"", ""content""],
  ""additionalProperties"": false
}";

        private static readonly Regex _ModeRegex = new Regex("^0?[0-7]{3}$", RegexOptions.CultureInvariant);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public SshWriteFileTool() : base(new ToolDescriptor
        {
            Name = "ssh_write_file",
            Title = "Write a file",
            Description = "Create a file on the SSH host over SFTP with the given content. By default it refuses to touch an existing file (error already_exists); "
                + "pass overwrite=true to replace it, which is Destructive and usually needs approval. State the full path and what you are writing before calling. "
                + "To edit a file, read it with ssh_read_file, then write the complete new content with overwrite=true.",
            InputSchemaJson = _Schema,
            BaseEffect = EffectEnum.Write,
            ClassifiedPerCall = true,
            OutputDescription = "One line saying whether the file was created or overwritten, and how many bytes were written."
        })
        {
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override EffectClassification Classify(ConnectorInvocationContext context, ToolArguments arguments)
        {
            string path = arguments?.GetString("path") ?? String.Empty;
            bool overwrite = arguments != null && arguments.GetBool("overwrite", false);
            if (overwrite)
                return new EffectClassification(EffectEnum.Destructive, "overwrite=true may replace an existing file", "Write (overwriting if present) " + Shorten(path, 200) + " on " + TargetName(context));
            return new EffectClassification(EffectEnum.Write, "creates a new file (fails if it exists)", "Create file " + Shorten(path, 200) + " on " + TargetName(context));
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, SshConnectionOptions options, CancellationToken token)
        {
            string display = options.ResolvePath(arguments.GetRequiredString("path"));
            string path = SftpPath(display);
            if (!arguments.Has("content")) throw new ConnectorException("invalid_argument", "Argument 'content' is required.");
            string content = arguments.GetString("content") ?? String.Empty;
            string encoding = (arguments.GetString("encoding") ?? "utf8").ToLowerInvariant();
            bool overwrite = arguments.GetBool("overwrite", false);
            string? mode = arguments.GetString("mode");
            if (mode != null && !_ModeRegex.IsMatch(mode)) throw new ConnectorException("invalid_argument", "The mode must be three octal digits, optionally with a leading 0, for example 0644.");

            byte[] bytes;
            if (encoding == "base64")
            {
                try
                {
                    bytes = Convert.FromBase64String(content);
                }
                catch (FormatException)
                {
                    throw new ConnectorException("invalid_argument", "The content is not valid base64.");
                }
            }
            else if (encoding == "utf8" || encoding == "utf-8")
            {
                bytes = Encoding.UTF8.GetBytes(content);
            }
            else
            {
                throw new ConnectorException("invalid_argument", "The encoding must be utf8 or base64.");
            }
            if (bytes.Length > options.MaxFileWriteBytes)
                throw new ConnectorException("invalid_argument", "The content is " + bytes.Length.ToString(CultureInfo.InvariantCulture) + " bytes, above the resource's maxFileWriteBytes of " + options.MaxFileWriteBytes.ToString(CultureInfo.InvariantCulture) + ".");

            SftpClient sftp = await GetSftpAsync(context, options, token).ConfigureAwait(false);
            bool existed;
            try
            {
                existed = await sftp.ExistsAsync(path, token).ConfigureAwait(false);
                if (existed)
                {
                    if (!overwrite) return ToolResult.Error("already_exists", "'" + display + "' already exists. Call again with overwrite=true to replace it (Destructive; usually needs approval).");
                    SftpFileAttributes attributes = await sftp.GetAttributesAsync(path, token).ConfigureAwait(false);
                    if (attributes.IsDirectory) return ToolResult.Error("invalid_argument", "'" + display + "' is a directory.");
                }

                using (SftpFileStream stream = await sftp.OpenAsync(path, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, token).ConfigureAwait(false))
                {
                    await stream.WriteAsync(bytes, 0, bytes.Length, token).ConfigureAwait(false);
                    await stream.FlushAsync(token).ConfigureAwait(false);
                }
                if (mode != null) sftp.ChangePermissions(path, Int16.Parse(mode, NumberStyles.None, CultureInfo.InvariantCulture));
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                throw MapSftp(context, e, display);
            }

            ToolResult result = ToolResult.FromText((existed ? "Overwrote " : "Created ") + display + " (" + bytes.Length.ToString(CultureInfo.InvariantCulture) + " bytes" + (mode != null ? ", mode " + mode : String.Empty) + ").");
            result.Metrics["bytes"] = bytes.Length;
            return result;
        }

        #endregion
    }
}
