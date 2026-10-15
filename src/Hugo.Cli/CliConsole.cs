namespace Hugo.Cli
{
    using System;
    using System.IO;
    using System.Text;

    /// <summary>
    /// The process environment a command runs in: standard streams, environment variables, the home directory, and
    /// how secrets are read. Tests build one over in-memory streams; <see cref="CreateSystem"/> builds the real one.
    /// Not thread-safe.
    /// </summary>
    public class CliConsole : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Standard input as a byte stream (the MCP stdio transport reads it directly).
        /// </summary>
        public Stream StandardInput { get; }

        /// <summary>
        /// Standard output as a byte stream (the MCP stdio transport writes protocol messages to it, and nothing else).
        /// </summary>
        public Stream StandardOutput { get; }

        /// <summary>
        /// Standard error, for diagnostics, prompts, and error messages.
        /// </summary>
        public TextWriter StandardError { get; }

        /// <summary>
        /// Whether standard input is redirected (not a terminal). When true, secrets are read as plain lines from
        /// standard input and no prompts are written. Default false.
        /// </summary>
        public bool IsInputRedirected { get; set; } = false;

        /// <summary>
        /// Home directory used for the default credential file (<c>~/.hugo/credentials.json</c>). Never null or empty.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or empty.</exception>
        public string HomeDirectory
        {
            get
            {
                return _HomeDirectory;
            }
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(HomeDirectory));
                _HomeDirectory = value;
            }
        }

        /// <summary>
        /// Environment variable lookup. Returns null for unset variables. Never null; the default reports every variable
        /// as unset.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null.</exception>
        public Func<string, string?> GetEnvironmentVariable
        {
            get
            {
                return _GetEnvironmentVariable;
            }
            set
            {
                _GetEnvironmentVariable = value ?? throw new ArgumentNullException(nameof(GetEnvironmentVariable));
            }
        }

        /// <summary>
        /// Reads a secret from the terminal without echoing it, given a prompt. Used only when
        /// <see cref="IsInputRedirected"/> is false. Null reads a plain line from standard input instead.
        /// </summary>
        public Func<string, string?>? SecretPrompt { get; set; } = null;

        /// <summary>
        /// Text reader over <see cref="StandardInput"/> (UTF-8), created on first use.
        /// </summary>
        public TextReader In
        {
            get
            {
                if (_In == null) _In = new StreamReader(StandardInput, new UTF8Encoding(false), false, 4096, true);
                return _In;
            }
        }

        /// <summary>
        /// Text writer over <see cref="StandardOutput"/> (UTF-8, auto flush, LF line endings), created on first use.
        /// Commands other than <c>mcp stdio</c> print their results here.
        /// </summary>
        public TextWriter Out
        {
            get
            {
                if (_Out == null)
                {
                    StreamWriter writer = new StreamWriter(StandardOutput, new UTF8Encoding(false), 4096, true);
                    writer.AutoFlush = true;
                    writer.NewLine = "\n";
                    _Out = writer;
                }
                return _Out;
            }
        }

        #endregion

        #region Private-Members

        private string _HomeDirectory;
        private Func<string, string?> _GetEnvironmentVariable = name => null;
        private StreamReader? _In = null;
        private StreamWriter? _Out = null;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate over explicit streams.
        /// </summary>
        /// <param name="standardInput">Standard input.</param>
        /// <param name="standardOutput">Standard output.</param>
        /// <param name="standardError">Standard error.</param>
        /// <param name="homeDirectory">Home directory.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null or the home directory is empty.</exception>
        public CliConsole(Stream standardInput, Stream standardOutput, TextWriter standardError, string homeDirectory)
        {
            StandardInput = standardInput ?? throw new ArgumentNullException(nameof(standardInput));
            StandardOutput = standardOutput ?? throw new ArgumentNullException(nameof(standardOutput));
            StandardError = standardError ?? throw new ArgumentNullException(nameof(standardError));
            if (String.IsNullOrEmpty(homeDirectory)) throw new ArgumentNullException(nameof(homeDirectory));
            _HomeDirectory = homeDirectory;
        }

        /// <summary>
        /// Build the console of the running process.
        /// </summary>
        /// <returns>Console.</returns>
        public static CliConsole CreateSystem()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (String.IsNullOrEmpty(home)) home = Environment.GetEnvironmentVariable("HOME") ?? Directory.GetCurrentDirectory();
            StreamWriter error = new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false));
            error.AutoFlush = true;
            CliConsole console = new CliConsole(Console.OpenStandardInput(), Console.OpenStandardOutput(), error, home);
            console.IsInputRedirected = Console.IsInputRedirected;
            console.GetEnvironmentVariable = name => Environment.GetEnvironmentVariable(name);
            console.SecretPrompt = ConsoleSecretReader.Read;
            return console;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Read one line from standard input after writing a prompt to standard error (the prompt is skipped when input
        /// is redirected).
        /// </summary>
        /// <param name="prompt">Prompt text.</param>
        /// <returns>The line, or null at end of input.</returns>
        public string? Prompt(string prompt)
        {
            if (!IsInputRedirected && !String.IsNullOrEmpty(prompt)) StandardError.Write(prompt);
            return In.ReadLine();
        }

        /// <summary>
        /// Read a secret. On a terminal it is read without echo through <see cref="SecretPrompt"/>; otherwise it is the
        /// next line of standard input.
        /// </summary>
        /// <param name="prompt">Prompt text.</param>
        /// <returns>The secret, or null at end of input.</returns>
        public string? ReadSecret(string prompt)
        {
            if (!IsInputRedirected && SecretPrompt != null) return SecretPrompt(prompt);
            return Prompt(prompt);
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
                _Out?.Flush();
                _Out?.Dispose();
                _In?.Dispose();
                StandardError.Flush();
            }
            _Disposed = true;
        }

        #endregion
    }
}
