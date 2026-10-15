namespace Test.Shared.Cli
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Cli;

    /// <summary>
    /// A throwaway home directory (its path contains spaces), a fake environment, and a helper that runs the CLI
    /// in process over in-memory streams. Not thread-safe.
    /// </summary>
    public class CliTestEnvironment : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Root directory (deleted on dispose). Its path contains spaces.
        /// </summary>
        public string Root { get; }

        /// <summary>
        /// Home directory under <see cref="Root"/>.
        /// </summary>
        public string Home { get; }

        /// <summary>
        /// Default credential file path for <see cref="Home"/>.
        /// </summary>
        public string CredentialsPath
        {
            get
            {
                return Path.Combine(Home, ".hugo", "credentials.json");
            }
        }

        /// <summary>
        /// Environment variables visible to the CLI.
        /// </summary>
        public Dictionary<string, string> Environment { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        #endregion

        #region Private-Members

        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Create the directories.
        /// </summary>
        public CliTestEnvironment()
        {
            Root = Path.Combine(Path.GetTempPath(), "hugo cli tests " + Guid.NewGuid().ToString("N").Substring(0, 8));
            Home = Path.Combine(Root, "home dir");
            Directory.CreateDirectory(Home);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build a console over in-memory streams.
        /// </summary>
        /// <param name="stdin">Standard input text.</param>
        /// <param name="stdout">Standard output stream.</param>
        /// <param name="stderr">Standard error writer.</param>
        /// <returns>Console.</returns>
        public CliConsole Console(string stdin, Stream stdout, TextWriter stderr)
        {
            MemoryStream input = new MemoryStream(new UTF8Encoding(false).GetBytes(stdin ?? String.Empty));
            CliConsole console = new CliConsole(input, stdout, stderr, Home);
            console.IsInputRedirected = true;
            console.GetEnvironmentVariable = name => Environment.TryGetValue(name, out string? value) ? value : null;
            return console;
        }

        /// <summary>
        /// Run the CLI in process.
        /// </summary>
        /// <param name="stdin">Standard input text.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="args">Arguments.</param>
        /// <returns>Result.</returns>
        public async Task<CliRunResult> RunAsync(string stdin, CancellationToken token, params string[] args)
        {
            using (MemoryStream stdout = new MemoryStream())
            {
                StringWriter stderr = new StringWriter();
                TextWriter synchronizedError = TextWriter.Synchronized(stderr);
                int code;
                using (CliConsole console = Console(stdin, stdout, synchronizedError))
                {
                    code = await CliApplication.RunAsync(args, console, token).ConfigureAwait(false);
                }
                CliRunResult result = new CliRunResult();
                result.ExitCode = code;
                result.Stdout = Encoding.UTF8.GetString(stdout.ToArray());
                result.Stderr = stderr.ToString();
                return result;
            }
        }

        /// <summary>
        /// Run the CLI in process with empty standard input.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <param name="args">Arguments.</param>
        /// <returns>Result.</returns>
        public Task<CliRunResult> RunAsync(CancellationToken token, params string[] args)
        {
            return RunAsync(String.Empty, token, args);
        }

        /// <summary>
        /// Dispose.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Dispose.
        /// </summary>
        /// <param name="disposing">Disposing.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing)
            {
                try
                {
                    if (Directory.Exists(Root)) Directory.Delete(Root, true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
            _Disposed = true;
        }

        #endregion
    }
}
