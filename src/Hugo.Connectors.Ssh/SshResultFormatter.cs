namespace Hugo.Connectors.Ssh
{
    using System;
    using System.Globalization;
    using System.Text;
    using System.Text.Json;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Turns an <see cref="SshCommandResult"/> into a bounded <see cref="ToolResult"/> with exit code, stdout, and
    /// stderr reported separately. Thread-safe.
    /// </summary>
    public static class SshResultFormatter
    {
        /// <summary>
        /// Build a tool result. 512 bytes of the budget are reserved for the header lines. Standard error gets a quarter
        /// of the rest (at least 1024 bytes, and no more than it needs); standard output gets the remainder. Each stream
        /// keeps its end.
        /// </summary>
        /// <param name="command">Command result.</param>
        /// <param name="budgetBytes">Total byte budget for both streams.</param>
        /// <param name="maxLines">Maximum lines per stream.</param>
        /// <param name="header">Optional first line, for example the command that ran.</param>
        /// <returns>Tool result. Not an error, even for a non-zero exit code; timeouts are reported by the caller.</returns>
        /// <exception cref="ArgumentNullException">Thrown when command is null.</exception>
        public static ToolResult Build(SshCommandResult command, int budgetBytes, int maxLines, string? header)
        {
            ArgumentNullException.ThrowIfNull(command);
            int budget = Math.Max(2048, budgetBytes - 512);
            int stderrBudget = (int)Math.Min(Math.Max(1024, budget / 4), Math.Max(1024, Encoding.UTF8.GetByteCount(command.Stderr)));
            int stdoutBudget = Math.Max(1024, budget - stderrBudget);

            ToolResult result = new ToolResult();
            ToolResult outProbe = new ToolResult();
            ToolResult errProbe = new ToolResult();
            string stdout = SshOutput.BoundTail(command.Stdout, command.StdoutBytes, command.StdoutDropped, stdoutBudget, maxLines, outProbe);
            string stderr = SshOutput.BoundTail(command.Stderr, command.StderrBytes, command.StderrDropped, stderrBudget, maxLines, errProbe);
            result.Truncated = outProbe.Truncated || errProbe.Truncated;
            result.OriginalBytes = Math.Max(command.StdoutBytes, outProbe.OriginalBytes) + Math.Max(command.StderrBytes, errProbe.OriginalBytes);

            StringBuilder sb = new StringBuilder();
            if (!String.IsNullOrEmpty(header)) sb.Append(header).Append('\n');
            if (command.TimedOut) sb.Append("exit_code: none (timed out after ").Append(((long)command.DurationMs).ToString(CultureInfo.InvariantCulture)).Append(" ms; the command was stopped)\n");
            else if (command.ExitCode.HasValue) sb.Append("exit_code: ").Append(command.ExitCode.Value.ToString(CultureInfo.InvariantCulture)).Append('\n');
            else sb.Append("exit_code: none (terminated by signal ").Append(command.ExitSignal ?? "unknown").Append(")\n");
            sb.Append("duration_ms: ").Append(((long)command.DurationMs).ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("--- stdout (").Append(command.StdoutBytes.ToString(CultureInfo.InvariantCulture)).Append(" bytes) ---\n");
            sb.Append(stdout.Length == 0 ? "(empty)\n" : (stdout.EndsWith('\n') ? stdout : stdout + "\n"));
            sb.Append("--- stderr (").Append(command.StderrBytes.ToString(CultureInfo.InvariantCulture)).Append(" bytes) ---\n");
            sb.Append(stderr.Length == 0 ? "(empty)\n" : (stderr.EndsWith('\n') ? stderr : stderr + "\n"));
            result.Text = sb.ToString();

            SshExecOutput structured = new SshExecOutput
            {
                ExitCode = command.ExitCode,
                ExitSignal = command.ExitSignal,
                TimedOut = command.TimedOut,
                DurationMs = (long)command.DurationMs,
                Stdout = stdout,
                Stderr = stderr,
                StdoutBytes = command.StdoutBytes,
                StderrBytes = command.StderrBytes,
                StdoutTruncated = outProbe.Truncated,
                StderrTruncated = errProbe.Truncated
            };
            result.StructuredJson = JsonSerializer.Serialize(structured);

            if (command.ExitCode.HasValue) result.Metrics["exitCode"] = command.ExitCode.Value;
            result.Metrics["upstreamMs"] = command.DurationMs;
            result.Metrics["stdoutBytes"] = command.StdoutBytes;
            result.Metrics["stderrBytes"] = command.StderrBytes;
            return result;
        }
    }
}
