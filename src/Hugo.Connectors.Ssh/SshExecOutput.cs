namespace Hugo.Connectors.Ssh
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Structured result of a remote command (MCP <c>structuredContent</c>). Not thread-safe for mutation.
    /// </summary>
    public class SshExecOutput
    {
        /// <summary>
        /// Exit status, or null when killed by a signal or timed out.
        /// </summary>
        [JsonPropertyName("exitCode")]
        public int? ExitCode { get; set; } = null;

        /// <summary>
        /// Terminating signal, or null.
        /// </summary>
        [JsonPropertyName("exitSignal")]
        public string? ExitSignal { get; set; } = null;

        /// <summary>
        /// Whether the command exceeded its timeout.
        /// </summary>
        [JsonPropertyName("timedOut")]
        public bool TimedOut { get; set; } = false;

        /// <summary>
        /// Duration in milliseconds.
        /// </summary>
        [JsonPropertyName("durationMs")]
        public long DurationMs { get; set; } = 0;

        /// <summary>
        /// Bounded standard output.
        /// </summary>
        [JsonPropertyName("stdout")]
        public string Stdout { get; set; } = String.Empty;

        /// <summary>
        /// Bounded standard error.
        /// </summary>
        [JsonPropertyName("stderr")]
        public string Stderr { get; set; } = String.Empty;

        /// <summary>
        /// Total bytes written to standard output.
        /// </summary>
        [JsonPropertyName("stdoutBytes")]
        public long StdoutBytes { get; set; } = 0;

        /// <summary>
        /// Total bytes written to standard error.
        /// </summary>
        [JsonPropertyName("stderrBytes")]
        public long StderrBytes { get; set; } = 0;

        /// <summary>
        /// Whether standard output was truncated.
        /// </summary>
        [JsonPropertyName("stdoutTruncated")]
        public bool StdoutTruncated { get; set; } = false;

        /// <summary>
        /// Whether standard error was truncated.
        /// </summary>
        [JsonPropertyName("stderrTruncated")]
        public bool StderrTruncated { get; set; } = false;
    }
}
