namespace Hugo.Connectors.Ssh
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Builds remote command lines safely: POSIX single-quote quoting, environment exports, working directory, and the
    /// configured shell wrapper. Thread-safe.
    /// </summary>
    public static class SshShell
    {
        #region Private-Members

        private static readonly Regex _EnvironmentEntry = new Regex("^[A-Za-z_][A-Za-z0-9_]*=", RegexOptions.CultureInvariant);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Quote a value as one POSIX shell word.
        /// </summary>
        /// <param name="value">Value. Null is treated as empty.</param>
        /// <returns>Quoted word, for example <c>'it'\''s'</c>.</returns>
        public static string Quote(string? value)
        {
            return "'" + (value ?? String.Empty).Replace("'", "'\\''", StringComparison.Ordinal) + "'";
        }

        /// <summary>
        /// Quote a path as one POSIX shell word, keeping a leading <c>~</c> or <c>~/</c> expandable as <c>$HOME</c>.
        /// </summary>
        /// <param name="path">Path. Null is treated as empty.</param>
        /// <returns>Quoted path.</returns>
        public static string QuotePath(string? path)
        {
            string p = path ?? String.Empty;
            if (p == "~") return "\"$HOME\"";
            if (p.StartsWith("~/", StringComparison.Ordinal)) return "\"$HOME\"/" + Quote(p.Substring(2));
            return Quote(p);
        }

        /// <summary>
        /// Whether an environment entry has the form <c>NAME=value</c> with a valid variable name and no newline.
        /// </summary>
        /// <param name="entry">Entry.</param>
        /// <returns>True when valid.</returns>
        public static bool IsValidEnvironmentEntry(string? entry)
        {
            return !String.IsNullOrEmpty(entry) && entry.IndexOf('\n') < 0 && entry.IndexOf('\0') < 0 && _EnvironmentEntry.IsMatch(entry);
        }

        /// <summary>
        /// Build the remote command line for a user command: exports, an optional <c>cd</c>, then the shell wrapper with
        /// the command as one quoted argument (or the command as is when the shell is empty).
        /// </summary>
        /// <param name="command">User command.</param>
        /// <param name="shell">Shell wrapper such as <c>/bin/bash -lc</c>, or empty.</param>
        /// <param name="workingDirectory">Working directory, or null.</param>
        /// <param name="environment">Environment entries; invalid entries are skipped.</param>
        /// <returns>Remote command line.</returns>
        /// <exception cref="ArgumentNullException">Thrown when command is null.</exception>
        public static string BuildCommand(string command, string? shell, string? workingDirectory, IEnumerable<string>? environment)
        {
            ArgumentNullException.ThrowIfNull(command);
            StringBuilder sb = new StringBuilder();
            if (environment != null)
            {
                foreach (string entry in environment)
                {
                    if (!IsValidEnvironmentEntry(entry)) continue;
                    sb.Append("export ").Append(Quote(entry)).Append("; ");
                }
            }
            if (!String.IsNullOrWhiteSpace(workingDirectory)) sb.Append("cd ").Append(Quote(workingDirectory)).Append(" && ");
            if (String.IsNullOrWhiteSpace(shell)) sb.Append(command);
            else sb.Append(shell.Trim()).Append(' ').Append(Quote(command));
            return sb.ToString();
        }

        /// <summary>
        /// Wrap a fixed, connector-authored POSIX script so it runs under <c>/bin/sh</c> whatever the login shell is.
        /// </summary>
        /// <param name="script">Script.</param>
        /// <returns>Remote command line.</returns>
        /// <exception cref="ArgumentNullException">Thrown when script is null.</exception>
        public static string PosixScript(string script)
        {
            ArgumentNullException.ThrowIfNull(script);
            return "/bin/sh -c " + Quote(script);
        }

        #endregion
    }
}
