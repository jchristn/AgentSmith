namespace Test.Shared.Connectors.Ssh
{
    using System;
    using Hugo.Connectors.Ssh.Classification;

    /// <summary>
    /// One row of the SSH command classifier table.
    /// </summary>
    public class SshClassifierCase
    {
        /// <summary>
        /// Command line to classify.
        /// </summary>
        public string Command { get; set; } = String.Empty;

        /// <summary>
        /// Expected outcome: <c>R</c> (Read), <c>W</c> (Write), <c>D</c> (Destructive), or <c>X</c> (Rejected).
        /// </summary>
        public string Expected { get; set; } = "D";

        /// <summary>
        /// Classifier options, or null for defaults (no sudo, no extra patterns).
        /// </summary>
        public SshClassifierOptions? Options { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="command">Command.</param>
        /// <param name="expected">Expected outcome.</param>
        /// <param name="options">Options, or null.</param>
        public SshClassifierCase(string command, string expected, SshClassifierOptions? options = null)
        {
            Command = command;
            Expected = expected;
            Options = options;
        }
    }
}
