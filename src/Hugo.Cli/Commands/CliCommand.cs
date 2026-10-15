namespace Hugo.Cli.Commands
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Cli.Arguments;

    /// <summary>
    /// Base class of every hugo command. Thread-safe (commands hold no state between runs).
    /// </summary>
    public abstract class CliCommand
    {
        #region Public-Members

        /// <summary>
        /// The command words, for example <c>mcp stdio</c>.
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// One-line summary for the command list.
        /// </summary>
        public abstract string Summary { get; }

        /// <summary>
        /// Paragraphs shown in the command's help after the usage line.
        /// </summary>
        public abstract string Details { get; }

        /// <summary>
        /// Options the command accepts.
        /// </summary>
        public abstract IReadOnlyList<OptionSpec> Options { get; }

        /// <summary>
        /// The command words split on spaces.
        /// </summary>
        public string[] Words
        {
            get
            {
                return Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run the command.
        /// </summary>
        /// <param name="console">Console.</param>
        /// <param name="arguments">Parsed options.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Exit code.</returns>
        public abstract Task<int> RunAsync(CliConsole console, ParsedArguments arguments, CancellationToken token);

        /// <summary>
        /// The full help text of the command.
        /// </summary>
        /// <returns>Help text, ending with a newline.</returns>
        public string Help()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Usage: hugo ").Append(Name).Append(" [options]\n\n");
            sb.Append(Details.TrimEnd()).Append("\n\n");
            sb.Append("Options:\n");
            List<string> left = new List<string>();
            List<string> right = new List<string>();
            foreach (OptionSpec option in Options)
            {
                left.Add("--" + option.Name + (option.TakesValue ? " " + option.ValueName : String.Empty));
                right.Add(option.Description);
            }
            left.Add("-h, --help");
            right.Add("Show this help.");
            int width = 0;
            foreach (string item in left) width = Math.Max(width, item.Length);
            for (int i = 0; i < left.Count; i++)
            {
                sb.Append("  ").Append(left[i].PadRight(width + 2)).Append(right[i]).Append('\n');
            }
            return sb.ToString();
        }

        #endregion
    }
}
