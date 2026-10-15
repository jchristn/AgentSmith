namespace Hugo.Connectors.Ssh.Tools
{
    using System;
    using System.Globalization;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>ssh_service_status</c>: service state plus recent journal lines. Thread-safe.
    /// </summary>
    public class SshServiceStatusTool : SshToolBase
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
      ""description"": ""Service unit or service name, for example \""nginx\"", \""nginx.service\"", or \""postgresql@16-main\"".""
    },
    ""journalLines"": {
      ""type"": ""integer"",
      ""minimum"": 0,
      ""maximum"": 500,
      ""description"": ""Recent journal lines to include for the unit (systemd hosts only). Default 30. Use 0 to skip the journal.""
    }
  },
  ""required"": [""unit""],
  ""additionalProperties"": false
}";

        private static readonly Regex _UnitRegex = new Regex("^[A-Za-z0-9@._:-]{1,256}$", RegexOptions.CultureInvariant);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public SshServiceStatusTool() : base(new ToolDescriptor
        {
            Name = "ssh_service_status",
            Title = "Service status",
            Description = "Read-only status of one service on the SSH host: systemctl status plus its most recent journal lines on systemd hosts, "
                + "or the OpenRC or SysV status elsewhere. Prefer this over ssh_exec for questions like \"is nginx running\" or \"why did the service fail\". "
                + "The exit code follows systemctl conventions (0 active, 3 inactive or failed, 4 unknown unit).",
            InputSchemaJson = _Schema,
            BaseEffect = EffectEnum.Read,
            OutputDescription = "Text with exit_code, then status output and recent journal lines in the stdout section."
        })
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Validate a unit name.
        /// </summary>
        /// <param name="unit">Unit.</param>
        /// <returns>True when the name is safe to pass to service managers.</returns>
        public static bool IsValidUnit(string? unit)
        {
            return !String.IsNullOrEmpty(unit) && _UnitRegex.IsMatch(unit) && !unit.StartsWith('-');
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, SshConnectionOptions options, CancellationToken token)
        {
            string unit = arguments.GetRequiredString("unit");
            if (!IsValidUnit(unit)) throw new ConnectorException("invalid_argument", "The unit name may contain only letters, digits, and @ . _ : - characters.");
            int lines = arguments.GetInt("journalLines", 30, 0, 500);
            string u = SshShell.Quote(unit);
            string n = lines.ToString(CultureInfo.InvariantCulture);

            string script =
                "if command -v systemctl >/dev/null 2>&1 && [ -d /run/systemd/system ]; then "
                + "systemctl status --no-pager --full -n 0 -- " + u + "; rc=$?; "
                + (lines > 0 ? "echo; echo '--- recent journal (" + n + " lines) ---'; journalctl -u " + u + " -n " + n + " --no-pager 2>&1; " : String.Empty)
                + "exit $rc; "
                + "elif command -v rc-service >/dev/null 2>&1; then rc-service " + u + " status; "
                + "elif command -v service >/dev/null 2>&1; then service " + u + " status; "
                + "elif [ -d /run/service/" + unit + " ] && command -v s6-svstat >/dev/null 2>&1; then s6-svstat /run/service/" + unit + "; "
                + "else echo 'No supported service manager (systemctl, rc-service, service, s6) was found on this host.' >&2; exit 127; fi";

            TimeSpan timeout = EffectiveTimeout(context, options, 30);
            SshCommandResult run = await RunAsync(context, options, SshShell.PosixScript(script), timeout, options.MaxOutputBytes, token).ConfigureAwait(false);
            ToolResult result = SshResultFormatter.Build(run, OutputBudget(context, options), context.Limits.MaxLines, "service: " + unit);
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
            return result;
        }

        #endregion
    }
}
