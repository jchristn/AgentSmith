namespace Hugo.Connectors.Ssh.Tools
{
    using System;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>ssh_tail_file</c>: last N lines of a file, optionally filtered by a pattern. Read. Thread-safe.
    /// </summary>
    public class SshTailFileTool : SshToolBase
    {
        #region Private-Members

        private const string _Schema = @"{
  ""type"": ""object"",
  ""properties"": {
    ""path"": {
      ""type"": ""string"",
      ""minLength"": 1,
      ""description"": ""File to read, for example /var/log/nginx/error.log. Relative paths resolve against the resource's workingDirectory, or the login directory.""
    },
    ""lines"": {
      ""type"": ""integer"",
      ""minimum"": 1,
      ""maximum"": 5000,
      ""description"": ""How many lines to return from the end of the file (after filtering). Default 100. The call's line limit also applies.""
    },
    ""grep"": {
      ""type"": ""string"",
      ""description"": ""Optional extended regular expression (grep -E). When set, only matching lines are kept, and the last N matches are returned. Example: \""ERROR|WARN\"".""
    },
    ""ignoreCase"": {
      ""type"": ""boolean"",
      ""description"": ""Match grep case-insensitively. Default false.""
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
        public SshTailFileTool() : base(new ToolDescriptor
        {
            Name = "ssh_tail_file",
            Title = "Tail a file",
            Description = "Return the last lines of a file on the SSH host, optionally keeping only lines that match a grep -E pattern (the last N matches). "
                + "Prefer this over ssh_exec with tail or grep for log files: it is always Read, quotes arguments safely, and reports a missing file as not_found.",
            InputSchemaJson = _Schema,
            BaseEffect = EffectEnum.Read,
            OutputDescription = "The selected lines, oldest first, after a one-line header."
        })
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, SshConnectionOptions options, CancellationToken token)
        {
            string path = options.ResolvePath(arguments.GetRequiredString("path"));
            int lines = Math.Min(arguments.GetInt("lines", 100, 1, 5000), context.Limits.MaxLines);
            string? grep = arguments.GetString("grep");
            bool ignoreCase = arguments.GetBool("ignoreCase", false);
            if (grep != null && (grep.IndexOf('\n') >= 0 || grep.IndexOf('\0') >= 0)) throw new ConnectorException("invalid_argument", "The grep pattern contains a NUL or newline character.");

            string p = SshShell.QuotePath(path);
            string n = lines.ToString(CultureInfo.InvariantCulture);
            string read = String.IsNullOrEmpty(grep)
                ? "tail -n " + n + " -- " + p
                : "grep -E " + (ignoreCase ? "-i " : String.Empty) + "-e " + SshShell.Quote(grep) + " -- " + p + " | tail -n " + n;
            string script =
                "if [ ! -e " + p + " ]; then echo 'not found' >&2; exit 44; fi; "
                + "if [ -d " + p + " ]; then echo 'is a directory' >&2; exit 45; fi; "
                + "if [ ! -r " + p + " ]; then echo 'not readable' >&2; exit 77; fi; "
                + read;

            TimeSpan timeout = EffectiveTimeout(context, options, null);
            SshCommandResult run = await RunAsync(context, options, SshShell.PosixScript(script), timeout, options.MaxOutputBytes, token).ConfigureAwait(false);
            if (run.TimedOut) return ToolResult.Error("timeout", "Reading '" + path + "' did not finish within " + (int)timeout.TotalSeconds + " seconds.");
            if (run.ExitCode == 44) return ToolResult.Error("not_found", "'" + path + "' was not found.");
            if (run.ExitCode == 45) return ToolResult.Error("invalid_argument", "'" + path + "' is a directory. Use ssh_list_directory.");
            if (run.ExitCode == 77) return ToolResult.Error("permission_denied", "'" + path + "' is not readable by this user.");
            if (run.ExitCode.HasValue && run.ExitCode.Value > 1 && String.IsNullOrEmpty(run.Stdout))
                return ToolResult.Error("upstream_error", "Reading '" + path + "' failed: " + OutputLimiter.Tail(run.Stderr, 1024, 5, null).Trim());

            ToolResult result = new ToolResult();
            string body = SshOutput.BoundTail(run.Stdout, run.StdoutBytes, run.StdoutDropped, Math.Max(1024, OutputBudget(context, options) - 256), context.Limits.MaxLines, result);
            string header = "# " + path + ": last " + n + (String.IsNullOrEmpty(grep) ? " lines" : " lines matching /" + grep + "/" + (ignoreCase ? "i" : String.Empty));
            if (body.Length == 0) body = String.IsNullOrEmpty(grep) ? "(file is empty)\n" : "(no matching lines)\n";
            result.Text = header + "\n" + body;
            result.Metrics["upstreamMs"] = run.DurationMs;
            return result;
        }

        #endregion
    }
}
