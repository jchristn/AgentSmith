namespace Hugo.Connectors.Ssh.Tools
{
    using System;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>ssh_system_info</c>: one read-only snapshot of host health. Thread-safe.
    /// </summary>
    public class SshSystemInfoTool : SshToolBase
    {
        #region Private-Members

        private const string _Schema = @"{
  ""type"": ""object"",
  ""properties"": {
    ""topProcesses"": {
      ""type"": ""integer"",
      ""minimum"": 1,
      ""maximum"": 50,
      ""description"": ""How many processes to list in each of the top-by-CPU and top-by-memory tables. Default 10.""
    }
  },
  ""additionalProperties"": false
}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public SshSystemInfoTool() : base(new ToolDescriptor
        {
            Name = "ssh_system_info",
            Title = "Host system overview",
            Description = "Read-only snapshot of the SSH host: hostname, OS release, kernel, uptime and load averages, CPU count, memory, disk usage per file system, and the top processes by CPU and by memory. "
                + "Call this first when investigating a host; it answers most \"is the box healthy\" questions in one call.",
            InputSchemaJson = _Schema,
            BaseEffect = EffectEnum.Read,
            OutputDescription = "Text sections headed '== name' for each fact."
        })
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, SshConnectionOptions options, CancellationToken token)
        {
            int top = arguments.GetInt("topProcesses", 10, 1, 50);
            string rows = (top + 1).ToString(CultureInfo.InvariantCulture);
            string script =
                "echo '== hostname'; hostname 2>/dev/null || cat /etc/hostname; "
                + "echo '== os'; if [ -r /etc/os-release ]; then grep -E '^(PRETTY_NAME|NAME|ID|VERSION_ID)=' /etc/os-release; else uname -s; fi; "
                + "echo '== kernel'; uname -srmv; "
                + "echo '== uptime and load'; uptime; "
                + "echo '== cpu count'; nproc 2>/dev/null || grep -c ^processor /proc/cpuinfo; "
                + "echo '== memory (MiB)'; free -m 2>/dev/null || head -n 5 /proc/meminfo; "
                + "echo '== disk usage'; df -hP -x tmpfs -x devtmpfs -x squashfs 2>/dev/null || df -hP; "
                + "echo '== top processes by cpu'; ps -eo pid,user,pcpu,pmem,etime,comm --sort=-pcpu 2>/dev/null | head -n " + rows + "; "
                + "echo '== top processes by memory'; ps -eo pid,user,pcpu,pmem,rss,comm --sort=-pmem 2>/dev/null | head -n " + rows + "; "
                + "exit 0";

            TimeSpan timeout = EffectiveTimeout(context, options, 30);
            SshCommandResult run = await RunAsync(context, options, SshShell.PosixScript(script), timeout, options.MaxOutputBytes, token).ConfigureAwait(false);
            if (run.TimedOut) return ToolResult.Error("timeout", "Collecting system information did not finish within " + (int)timeout.TotalSeconds + " seconds.");

            ToolResult result = new ToolResult();
            string text = OutputLimiter.Head(run.Stdout, OutputBudget(context, options), context.Limits.MaxLines, result);
            if (!String.IsNullOrWhiteSpace(run.Stderr)) text += "\n== warnings\n" + OutputLimiter.Tail(run.Stderr, 2048, 20, result);
            result.Text = text;
            result.Metrics["upstreamMs"] = run.DurationMs;
            return result;
        }

        #endregion
    }
}
