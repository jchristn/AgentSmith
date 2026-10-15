namespace Hugo.Cli.Arguments
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Parses the options of one command. Accepts <c>--name value</c>, <c>--name=value</c>, flags, <c>--help</c> and
    /// <c>-h</c> everywhere, and <c>--</c> to end option parsing. Values may contain spaces when the shell passes them
    /// as one argument. Thread-safe (stateless).
    /// </summary>
    public static class ArgumentParser
    {
        /// <summary>
        /// Parse arguments.
        /// </summary>
        /// <param name="args">All arguments.</param>
        /// <param name="startIndex">Index of the first argument after the command words.</param>
        /// <param name="specs">Options the command accepts.</param>
        /// <param name="maxPositionals">Maximum positional arguments the command accepts. Default 0.</param>
        /// <returns>Parsed arguments.</returns>
        /// <exception cref="ArgumentNullException">Thrown when args or specs is null.</exception>
        /// <exception cref="CliUsageException">Thrown for unknown options, missing values, repeated options, or too many positional arguments.</exception>
        public static ParsedArguments Parse(string[] args, int startIndex, IReadOnlyList<OptionSpec> specs, int maxPositionals = 0)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));
            if (specs == null) throw new ArgumentNullException(nameof(specs));

            ParsedArguments parsed = new ParsedArguments();
            bool optionsEnded = false;
            for (int i = Math.Max(0, startIndex); i < args.Length; i++)
            {
                string arg = args[i] ?? String.Empty;
                if (!optionsEnded && arg == "--")
                {
                    optionsEnded = true;
                    continue;
                }
                if (!optionsEnded && (arg == "-h" || arg == "--help" || arg == "-?"))
                {
                    parsed.Flags.Add("help");
                    continue;
                }
                if (!optionsEnded && arg.StartsWith("--", StringComparison.Ordinal))
                {
                    string body = arg.Substring(2);
                    string? inlineValue = null;
                    int eq = body.IndexOf('=', StringComparison.Ordinal);
                    if (eq >= 0)
                    {
                        inlineValue = body.Substring(eq + 1);
                        body = body.Substring(0, eq);
                    }
                    OptionSpec? spec = specs.FirstOrDefault(s => String.Equals(s.Name, body, StringComparison.Ordinal));
                    if (spec == null) throw new CliUsageException("Unknown option '--" + body + "'.");
                    if (parsed.Values.ContainsKey(spec.Name) || parsed.Flags.Contains(spec.Name))
                        throw new CliUsageException("Option '--" + spec.Name + "' was given more than once.");
                    if (!spec.TakesValue)
                    {
                        if (inlineValue != null) throw new CliUsageException("Option '--" + spec.Name + "' does not take a value.");
                        parsed.Flags.Add(spec.Name);
                        continue;
                    }
                    if (inlineValue == null)
                    {
                        if (i + 1 >= args.Length) throw new CliUsageException("Option '--" + spec.Name + "' requires a value (" + spec.ValueName + ").");
                        inlineValue = args[++i] ?? String.Empty;
                    }
                    parsed.Values[spec.Name] = inlineValue;
                    continue;
                }
                if (!optionsEnded && arg.StartsWith("-", StringComparison.Ordinal) && arg.Length > 1)
                    throw new CliUsageException("Unknown option '" + arg + "'. Options start with two dashes, for example --server.");
                parsed.Positionals.Add(arg);
            }

            if (parsed.Positionals.Count > maxPositionals)
                throw new CliUsageException("Unexpected argument '" + parsed.Positionals[maxPositionals] + "'.");
            return parsed;
        }
    }
}
