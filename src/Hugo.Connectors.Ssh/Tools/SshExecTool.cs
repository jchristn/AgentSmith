namespace Hugo.Connectors.Ssh.Tools
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Ssh.Classification;

    /// <summary>
    /// <c>ssh_exec</c>: run a non-interactive shell command. Each call is classified by
    /// <see cref="SshCommandClassifier"/>. Thread-safe.
    /// </summary>
    public class SshExecTool : SshToolBase
    {
        #region Private-Members

        private const string _Schema = @"{
  ""type"": ""object"",
  ""properties"": {
    ""command"": {
      ""type"": ""string"",
      ""minLength"": 1,
      ""maxLength"": 16384,
      ""description"": ""Shell command to run non-interactively, for example \""df -h\"" or \""journalctl -u nginx -n 100 --no-pager\"". It runs through the resource's shell (default /bin/bash -lc) with no TTY and no standard input. Never start editors, pagers without -n or --no-pager, or commands that wait for input. Bound output yourself (head, tail -n, grep -m). Read-only commands such as cat, ls, grep, ps, df, journalctl, and systemctl status are classified Read; anything else is Write, and rm, kill, systemctl stop or restart, overwriting redirections, and similar are Destructive and may need approval.""
    },
    ""timeoutSeconds"": {
      ""type"": ""integer"",
      ""minimum"": 1,
      ""maximum"": 900,
      ""description"": ""Optional timeout in seconds. It can only shorten the resource's commandTimeoutSeconds (default 60). When it elapses the command is stopped and the partial output is returned with error code timeout.""
    },
    ""workingDirectory"": {
      ""type"": ""string"",
      ""description"": ""Optional directory to cd into before running the command. Defaults to the resource's workingDirectory, or the login directory.""
    }
  },
  ""required"": [""command""],
  ""additionalProperties"": false
}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public SshExecTool() : base(new ToolDescriptor
        {
            Name = "ssh_exec",
            Title = "Run shell command",
            Description = "Run a shell command on the SSH host and return its exit code, standard output, and standard error (each bounded, keeping the end). "
                + "Use it when no purpose-built ssh_ tool fits. Commands run non-interactively with a timeout: never start editors, pagers, or long-running foreground processes, and bound output with tail -n, head, or grep -m. "
                + "Each command is classified: read-only commands are Read, unknown commands are Write, and destructive ones (rm, kill, systemctl stop/restart, overwriting redirections, package removal) are Destructive and may require approval. "
                + "A non-zero exit code is a normal result, not a tool error.",
            InputSchemaJson = _Schema,
            BaseEffect = EffectEnum.Read,
            ClassifiedPerCall = true,
            OutputDescription = "Text with exit_code, duration_ms, and separate stdout and stderr sections; structured JSON with exitCode, timedOut, stdout, stderr, and truncation flags."
        })
        {
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override EffectClassification Classify(ConnectorInvocationContext context, ToolArguments arguments)
        {
            string? command = arguments?.GetString("command");
            if (String.IsNullOrWhiteSpace(command)) return new EffectClassification(EffectEnum.Destructive, "command argument is missing", "Run an empty command on " + TargetName(context));
            SshClassifierOptions options = context == null ? new SshClassifierOptions() : SshConnectionOptions.FromSettings(context.Resource.Settings).ToClassifierOptions();
            EffectClassification classification = SshCommandClassifier.Classify(command, options);
            classification.ApprovalSummary = "Run \"" + Shorten(command, 300) + "\" on " + TargetName(context);
            return classification;
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, SshConnectionOptions options, CancellationToken token)
        {
            string command = arguments.GetRequiredString("command");
            if (command.IndexOf('\0') >= 0) throw new ConnectorException("invalid_argument", "The command contains a NUL character.");

            EffectClassification classification = Classify(context, arguments);
            if (classification.Rejected) return ToolResult.Error("rejected", "The command was rejected: " + classification.Reason + ".");

            int? requested = arguments.Has("timeoutSeconds") ? arguments.GetInt("timeoutSeconds", options.CommandTimeoutSeconds, 1, 900) : null;
            TimeSpan timeout = EffectiveTimeout(context, options, requested);
            string? workingDirectory = arguments.GetString("workingDirectory") ?? options.WorkingDirectory;
            if (workingDirectory != null && (workingDirectory.IndexOf('\n') >= 0 || workingDirectory.IndexOf('\0') >= 0))
                throw new ConnectorException("invalid_argument", "The working directory contains a NUL or newline character.");

            string remote = SshShell.BuildCommand(command, options.Shell, workingDirectory, options.Environment);
            int budget = OutputBudget(context, options);
            SshCommandResult run = await RunAsync(context, options, remote, timeout, options.MaxOutputBytes, token).ConfigureAwait(false);

            ToolResult result = SshResultFormatter.Build(run, budget, context.Limits.MaxLines, null);
            if (run.TimedOut)
            {
                result.IsError = true;
                result.ErrorCode = "timeout";
                result.Text = "The command did not finish within " + (int)timeout.TotalSeconds + " seconds and was stopped. Partial output follows.\n" + result.Text;
            }
            return result;
        }

        #endregion
    }
}
