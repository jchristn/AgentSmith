namespace Hugo.Connectors.Ssh
{
    using System;

    /// <summary>
    /// Outcome of one remote command. Output is the tail of each stream, bounded by the capture size. Not thread-safe
    /// for mutation.
    /// </summary>
    public class SshCommandResult
    {
        /// <summary>
        /// Exit status, or null when the command was killed by a signal or did not finish.
        /// </summary>
        public int? ExitCode { get; set; } = null;

        /// <summary>
        /// Signal that terminated the command, or null.
        /// </summary>
        public string? ExitSignal { get; set; } = null;

        /// <summary>
        /// Captured standard output (the most recent bytes when the stream exceeded the capture size).
        /// </summary>
        public string Stdout { get; set; } = String.Empty;

        /// <summary>
        /// Captured standard error (the most recent bytes when the stream exceeded the capture size).
        /// </summary>
        public string Stderr { get; set; } = String.Empty;

        /// <summary>
        /// Total bytes the command wrote to standard output.
        /// </summary>
        public long StdoutBytes { get; set; } = 0;

        /// <summary>
        /// Total bytes the command wrote to standard error.
        /// </summary>
        public long StderrBytes { get; set; } = 0;

        /// <summary>
        /// Whether standard output exceeded the capture size.
        /// </summary>
        public bool StdoutDropped { get; set; } = false;

        /// <summary>
        /// Whether standard error exceeded the capture size.
        /// </summary>
        public bool StderrDropped { get; set; } = false;

        /// <summary>
        /// Whether the command was stopped because it exceeded its timeout.
        /// </summary>
        public bool TimedOut { get; set; } = false;

        /// <summary>
        /// Wall-clock duration in milliseconds.
        /// </summary>
        public double DurationMs { get; set; } = 0;
    }
}
