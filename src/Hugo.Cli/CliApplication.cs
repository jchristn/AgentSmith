namespace Hugo.Cli
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Cli.Api;
    using Hugo.Cli.Arguments;
    using Hugo.Cli.Commands;

    /// <summary>
    /// Dispatches a command line to a command and maps failures to exit codes: 0 success, 1 error, 2 usage. Help and
    /// version output go to standard output; errors go to standard error. Thread-safe (stateless).
    /// </summary>
    public static class CliApplication
    {
        #region Public-Members

        /// <summary>
        /// Every command, in help order.
        /// </summary>
        public static IReadOnlyList<CliCommand> Commands
        {
            get
            {
                return new List<CliCommand>
                {
                    new McpStdioCommand(),
                    new LoginCommand(),
                    new LogoutCommand(),
                    new WhoamiCommand(),
                    new ProfilesListCommand()
                };
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run a command line.
        /// </summary>
        /// <param name="args">Arguments (without the program name).</param>
        /// <param name="console">Console.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Exit code.</returns>
        /// <exception cref="ArgumentNullException">Thrown when args or console is null.</exception>
        public static async Task<int> RunAsync(string[] args, CliConsole console, CancellationToken token)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));
            if (console == null) throw new ArgumentNullException(nameof(console));

            CliCommand? command = null;
            try
            {
                if (args.Length == 0)
                {
                    console.StandardError.Write(MainHelp());
                    return (int)CliExitCodeEnum.Usage;
                }

                string first = args[0];
                if (first == "--version" || first == "-v" || first == "version")
                {
                    if (args.Length > 1) throw new CliUsageException("--version takes no arguments.");
                    console.Out.WriteLine("hugo " + ProductInfo.Version);
                    return (int)CliExitCodeEnum.Success;
                }
                if (first == "--help" || first == "-h" || first == "-?" || first == "help")
                {
                    if (args.Length > 1) return WriteTopicHelp(console, args.Skip(1).ToArray());
                    console.Out.Write(MainHelp());
                    return (int)CliExitCodeEnum.Success;
                }

                command = Match(args);
                if (command == null)
                {
                    string? group = Groups().FirstOrDefault(g => g == first);
                    if (group != null)
                    {
                        if (args.Length == 1)
                        {
                            console.StandardError.Write(GroupHelp(group));
                            return (int)CliExitCodeEnum.Usage;
                        }
                        if (args[1] == "--help" || args[1] == "-h" || args[1] == "-?")
                        {
                            console.Out.Write(GroupHelp(group));
                            return (int)CliExitCodeEnum.Success;
                        }
                        throw new CliUsageException("Unknown command 'hugo " + group + " " + args[1] + "'. Run 'hugo " + group + " --help'.");
                    }
                    throw new CliUsageException("Unknown command '" + first + "'. Run 'hugo --help' for the list of commands.");
                }

                int start = command.Words.Length;
                if (args.Skip(start).TakeWhile(a => a != "--").Any(a => a == "--help" || a == "-h" || a == "-?"))
                {
                    console.Out.Write(command.Help());
                    return (int)CliExitCodeEnum.Success;
                }

                ParsedArguments parsed = ArgumentParser.Parse(args, start, command.Options);
                return await command.RunAsync(console, parsed, token).ConfigureAwait(false);
            }
            catch (CliUsageException e)
            {
                console.StandardError.WriteLine("hugo: " + e.Message);
                console.StandardError.WriteLine(command != null ? "Run 'hugo " + command.Name + " --help' for usage." : "Run 'hugo --help' for usage.");
                return (int)CliExitCodeEnum.Usage;
            }
            catch (CliException e)
            {
                console.StandardError.WriteLine("hugo: " + e.Message);
                return (int)CliExitCodeEnum.Error;
            }
            catch (HugoApiException e)
            {
                console.StandardError.WriteLine("hugo: the server answered HTTP " + e.StatusCode + ": " + e.Message);
                return (int)CliExitCodeEnum.Error;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                console.StandardError.WriteLine("hugo: cancelled.");
                return (int)CliExitCodeEnum.Error;
            }
        }

        /// <summary>
        /// The top-level help text.
        /// </summary>
        /// <returns>Help text, ending with a newline.</returns>
        public static string MainHelp()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("hugo ").Append(ProductInfo.Version).Append(": command line for the Hugo super agent server.\n\n");
            sb.Append("Usage: hugo <command> [options]\n\n");
            sb.Append("Commands:\n");
            IReadOnlyList<CliCommand> commands = Commands;
            int width = commands.Max(c => c.Name.Length);
            foreach (CliCommand command in commands)
            {
                sb.Append("  ").Append(command.Name.PadRight(width + 2)).Append(command.Summary).Append('\n');
            }
            sb.Append("\nOptions:\n");
            sb.Append("  ").Append("--version".PadRight(width + 2)).Append("Print the version.\n");
            sb.Append("  ").Append("-h, --help".PadRight(width + 2)).Append("Show help. 'hugo <command> --help' shows a command's options.\n");
            sb.Append("\nEnvironment: HUGO_SERVER, HUGO_TOKEN, HUGO_ACCESS_KEY, HUGO_SECRET_KEY, HUGO_TENANT, HUGO_PROFILE,\n");
            sb.Append("HUGO_CREDENTIALS_FILE. Flags win over the environment, which wins over the login cache\n");
            sb.Append("(~/.hugo/credentials.json).\n");
            sb.Append("\nExit codes: 0 success, 1 error, 2 invalid command line.\n");
            return sb.ToString();
        }

        #endregion

        #region Private-Methods

        private static CliCommand? Match(string[] args)
        {
            CliCommand? best = null;
            foreach (CliCommand command in Commands)
            {
                string[] words = command.Words;
                if (words.Length > args.Length) continue;
                bool matches = true;
                for (int i = 0; i < words.Length; i++)
                {
                    if (!String.Equals(words[i], args[i], StringComparison.Ordinal))
                    {
                        matches = false;
                        break;
                    }
                }
                if (matches && (best == null || words.Length > best.Words.Length)) best = command;
            }
            return best;
        }

        private static IEnumerable<string> Groups()
        {
            return Commands.Where(c => c.Words.Length > 1).Select(c => c.Words[0]).Distinct(StringComparer.Ordinal);
        }

        private static string GroupHelp(string group)
        {
            List<CliCommand> members = Commands.Where(c => c.Words.Length > 1 && c.Words[0] == group).ToList();
            StringBuilder sb = new StringBuilder();
            sb.Append("Usage: hugo ").Append(group).Append(" <command> [options]\n\nCommands:\n");
            int width = members.Max(c => c.Name.Length);
            foreach (CliCommand command in members)
            {
                sb.Append("  ").Append(command.Name.PadRight(width + 2)).Append(command.Summary).Append('\n');
            }
            sb.Append("\nRun 'hugo ").Append(group).Append(" <command> --help' for a command's options.\n");
            return sb.ToString();
        }

        private static int WriteTopicHelp(CliConsole console, string[] topic)
        {
            CliCommand? command = Match(topic);
            if (command != null)
            {
                console.Out.Write(command.Help());
                return (int)CliExitCodeEnum.Success;
            }
            if (topic.Length == 1 && Groups().Contains(topic[0], StringComparer.Ordinal))
            {
                console.Out.Write(GroupHelp(topic[0]));
                return (int)CliExitCodeEnum.Success;
            }
            throw new CliUsageException("No help for '" + String.Join(" ", topic) + "'. Run 'hugo --help' for the list of commands.");
        }

        #endregion
    }
}
