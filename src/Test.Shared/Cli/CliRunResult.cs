namespace Test.Shared.Cli
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// The outcome of one in-process CLI run.
    /// </summary>
    public class CliRunResult
    {
        /// <summary>
        /// Exit code.
        /// </summary>
        public int ExitCode { get; set; } = 0;

        /// <summary>
        /// Standard output text.
        /// </summary>
        public string Stdout { get; set; } = String.Empty;

        /// <summary>
        /// Standard error text.
        /// </summary>
        public string Stderr { get; set; } = String.Empty;

        /// <summary>
        /// Non-empty standard output lines.
        /// </summary>
        public List<string> StdoutLines
        {
            get
            {
                return Stdout.Split('\n').Where(l => l.Length > 0).ToList();
            }
        }

        /// <summary>
        /// Description for assertion messages.
        /// </summary>
        /// <returns>Text.</returns>
        public override string ToString()
        {
            return "exit " + ExitCode + "; stdout: [" + Stdout + "]; stderr: [" + Stderr + "]";
        }
    }
}
