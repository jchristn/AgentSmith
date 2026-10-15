namespace Hugo.Connectors.Ssh.Tools
{
    using System;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>ssh_service_control</c>: start, stop, restart, or reload a service. Always Destructive. Thread-safe.
    /// </summary>
    public class SshServiceControlTool : SshToolBase
    {
        #region Private-Members

        private const string _Schema = @"{
  ""type"": ""object"",
  ""properties"": {
    ""unit"": {
      ""type"": ""string"",
      ""minLength"": 1,
      ""maxLength"": 256,
      ""pattern"": ""^[A-Za-z0-9@._:-]+$"",
      ""description"": ""Service unit or service name, for example \""nginx\"" or \""nginx.service\"". Check it with ssh_service_status first.""
    },
    ""action"": {
      ""type"": ""string"",
      ""enum"": [""start"", ""stop"", ""restart"", ""reload""],
      ""description"": ""What to do. Prefer reload over restart when the service supports it, because it does not drop connections.""
    },
    ""useSudo"": {
      ""type"": ""boolean"",
      ""description"": ""Run the service manager through sudo -n (non-interactive). Only works when the resource has allowSudo enabled and the user has passwordless sudo for the command. Default false.""
    }
  },
  ""required"": [""unit"", ""action""],
  ""additionalProperties"": false
}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public SshServiceControlTool() : base(new ToolDescriptor
        {
            Name = "ssh_service_control",
            Title = "Control a service",
            Description = "Start, stop, restart, or reload one service on the SSH host with systemctl (or OpenRC or SysV service where systemd is absent). "
                + "Every call is Destructive and usually needs human approval: state the unit, the action, and why before calling. "
                + "Check the current state with ssh_service_status first and again afterwards. The result includes the service state after the action.",
            InputSchemaJson = _Schema,
            BaseEffect = EffectEnum.Destructive,
            OutputDescription = "Text with exit_code and the service manager output; error code command_failed when the action failed."
        })
        {
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override EffectClassification Classify(ConnectorInvocationContext context, ToolArguments arguments)
        {
            string unit = arguments?.GetString("unit") ?? String.Empty;
            string action = (arguments?.GetString("action") ?? String.Empty).ToLowerInvariant();
            bool useSudo = arguments != null && arguments.GetBool("useSudo", false);
            string summary = Capitalize(action) + " service " + Shorten(unit, 100) + " on " + TargetName(context) + (useSudo ? " (with sudo)" : String.Empty);
            if (useSudo && context != null && !SshConnectionOptions.FromSettings(context.Resource.Settings).AllowSudo)
            {
                EffectClassification rejected = EffectClassification.Reject("sudo is not allowed on this resource (allowSudo is false)");
                rejected.ApprovalSummary = summary;
                return rejected;
            }
            return new EffectClassification(EffectEnum.Destructive, "service control (" + (action.Length == 0 ? "unknown action" : action) + ") changes a running service", summary);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, SshConnectionOptions options, CancellationToken token)
        {
            string unit = arguments.GetRequiredString("unit");
            if (!SshServiceStatusTool.IsValidUnit(unit)) throw new ConnectorException("invalid_argument", "The unit name may contain only letters, digits, and @ . _ : - characters.");
            string action = arguments.GetRequiredString("action").ToLowerInvariant();
            if (action != "start" && action != "stop" && action != "restart" && action != "reload")
                throw new ConnectorException("invalid_argument", "The action must be start, stop, restart, or reload.");
            bool useSudo = arguments.GetBool("useSudo", false);
            if (useSudo && !options.AllowSudo) return ToolResult.Error("rejected", "sudo is not allowed on this resource (allowSudo is false).");

            string sudo = useSudo ? "sudo -n " : String.Empty;
            string u = SshShell.Quote(unit);
            string script =
                "if command -v systemctl >/dev/null 2>&1 && [ -d /run/systemd/system ]; then "
                + sudo + "systemctl " + action + " -- " + u + "; rc=$?; echo \"state after " + action + ": $(systemctl is-active -- " + u + " 2>&1)\"; exit $rc; "
                + "elif command -v rc-service >/dev/null 2>&1; then " + sudo + "rc-service " + u + " " + action + "; "
                + "elif command -v service >/dev/null 2>&1; then " + sudo + "service " + u + " " + action + "; "
                + "else echo 'No supported service manager (systemctl, rc-service, service) was found on this host.' >&2; exit 127; fi";

            TimeSpan timeout = EffectiveTimeout(context, options, null);
            SshCommandResult run = await RunAsync(context, options, SshShell.PosixScript(script), timeout, options.MaxOutputBytes, token).ConfigureAwait(false);
            ToolResult result = SshResultFormatter.Build(run, OutputBudget(context, options), context.Limits.MaxLines, "service: " + unit + ", action: " + action);
            if (run.TimedOut)
            {
                result.IsError = true;
                result.ErrorCode = "timeout";
            }
            else if (run.ExitCode == 127)
            {
                result.IsError = true;
                result.ErrorCode = "not_supported";
            }
            else if (run.ExitCode != 0)
            {
                result.IsError = true;
                result.ErrorCode = "command_failed";
                result.Text = "The service manager reported failure (exit code " + (run.ExitCode.HasValue ? run.ExitCode.Value.ToString(CultureInfo.InvariantCulture) : "none") + ").\n" + result.Text;
            }
            return result;
        }

        #endregion

        #region Private-Methods

        private static string Capitalize(string value)
        {
            if (String.IsNullOrEmpty(value)) return "Control";
            return Char.ToUpperInvariant(value[0]) + value.Substring(1);
        }

        #endregion
    }
}
