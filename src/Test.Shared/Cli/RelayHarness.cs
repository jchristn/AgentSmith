namespace Test.Shared.Cli
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Pipes;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;
    using Hugo.Cli;
    using Hugo.Cli.Mcp;

    /// <summary>
    /// Runs <c>hugo mcp stdio</c> in process over anonymous pipes, so a test can write a message, wait for what the
    /// relay writes back, and write the next message. Standard output lines are collected by a background reader.
    /// Not thread-safe.
    /// </summary>
    public class RelayHarness : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Every standard output line read so far.
        /// </summary>
        public ConcurrentQueue<string> Lines { get; } = new ConcurrentQueue<string>();

        /// <summary>
        /// Standard error text so far.
        /// </summary>
        public string Stderr
        {
            get
            {
                return _Stderr.ToString();
            }
        }

        #endregion

        #region Private-Members

        private readonly AnonymousPipeServerStream _InputWriter;
        private readonly AnonymousPipeClientStream _InputReader;
        private readonly AnonymousPipeServerStream _OutputWriter;
        private readonly AnonymousPipeClientStream _OutputReader;
        private readonly StringWriter _Stderr = new StringWriter();
        private readonly Channel<string> _Channel = Channel.CreateUnbounded<string>();
        private readonly CliConsole _Console;
        private readonly CancellationTokenSource _Cts;
        private Task<int>? _Run = null;
        private Task? _Reader = null;
        private bool _InputClosed = false;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        private RelayHarness(CliTestEnvironment environment, CancellationToken token)
        {
            _Cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            _InputWriter = new AnonymousPipeServerStream(PipeDirection.Out);
            _InputReader = new AnonymousPipeClientStream(PipeDirection.In, _InputWriter.ClientSafePipeHandle);
            _OutputWriter = new AnonymousPipeServerStream(PipeDirection.Out);
            _OutputReader = new AnonymousPipeClientStream(PipeDirection.In, _OutputWriter.ClientSafePipeHandle);
            _Console = new CliConsole(_InputReader, _OutputWriter, TextWriter.Synchronized(_Stderr), environment.Home);
            _Console.IsInputRedirected = true;
            _Console.GetEnvironmentVariable = name => environment.Environment.TryGetValue(name, out string? value) ? value : null;
        }

        /// <summary>
        /// Start <c>hugo mcp stdio</c> with the given options.
        /// </summary>
        /// <param name="environment">Test environment.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="options">Options after <c>mcp stdio</c>.</param>
        /// <returns>Harness.</returns>
        public static RelayHarness Start(CliTestEnvironment environment, CancellationToken token, params string[] options)
        {
            RelayHarness harness = new RelayHarness(environment, token);
            string[] args = new string[] { "mcp", "stdio" }.Concat(options).ToArray();
            harness._Run = Task.Run(async () =>
            {
                try
                {
                    return await CliApplication.RunAsync(args, harness._Console, harness._Cts.Token).ConfigureAwait(false);
                }
                finally
                {
                    harness._OutputWriter.Dispose();
                }
            });
            harness._Reader = Task.Run(() => harness.ReadLoopAsync());
            return harness;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Write one line to the relay's standard input.
        /// </summary>
        /// <param name="line">Line (a JSON-RPC message).</param>
        /// <returns>Task.</returns>
        public async Task SendAsync(string line)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(line + "\n");
            await _InputWriter.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            await _InputWriter.FlushAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// The next standard output line, or null on timeout or end of output.
        /// </summary>
        /// <param name="timeoutMs">Timeout in milliseconds.</param>
        /// <returns>Line or null.</returns>
        public async Task<string?> ReadAsync(int timeoutMs = 10000)
        {
            using (CancellationTokenSource cts = new CancellationTokenSource(timeoutMs))
            {
                try
                {
                    return await _Channel.Reader.ReadAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
                catch (ChannelClosedException)
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// Read lines until the response with the given ID arrives. Every line read is added to <paramref name="seen"/>.
        /// </summary>
        /// <param name="idKey">Raw JSON ID, for example <c>2</c> or <c>"a"</c>.</param>
        /// <param name="seen">Lines read, in order.</param>
        /// <param name="timeoutMs">Overall timeout.</param>
        /// <returns>The response.</returns>
        /// <exception cref="TimeoutException">Thrown when the response does not arrive.</exception>
        public async Task<JsonRpcMessage> ReadResponseAsync(string idKey, List<string> seen, int timeoutMs = 10000)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                int remaining = Math.Max(1, (int)(deadline - DateTime.UtcNow).TotalMilliseconds);
                string? line = await ReadAsync(remaining).ConfigureAwait(false);
                if (line == null) break;
                seen.Add(line);
                JsonRpcMessage? message = JsonRpcMessage.TryParse(line);
                if (message != null && message.IsResponse && message.IdKey == idKey) return message;
            }
            throw new TimeoutException("No response with id " + idKey + ". Seen: " + String.Join(" | ", seen) + " Stderr: " + Stderr);
        }

        /// <summary>
        /// Close the relay's standard input (end of input).
        /// </summary>
        public void CloseInput()
        {
            if (_InputClosed) return;
            _InputClosed = true;
            _InputWriter.Dispose();
        }

        /// <summary>
        /// Close input and wait for the relay to exit.
        /// </summary>
        /// <param name="timeoutMs">Timeout.</param>
        /// <returns>Exit code.</returns>
        /// <exception cref="TimeoutException">Thrown when the relay does not exit.</exception>
        public async Task<int> StopAsync(int timeoutMs = 15000)
        {
            CloseInput();
            Task finished = await Task.WhenAny(_Run!, Task.Delay(timeoutMs)).ConfigureAwait(false);
            if (finished != _Run) throw new TimeoutException("The relay did not exit after its input closed. Stderr: " + Stderr);
            int code = await _Run!.ConfigureAwait(false);
            if (_Reader != null) await Task.WhenAny(_Reader, Task.Delay(2000)).ConfigureAwait(false);
            return code;
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
                CloseInput();
                _Cts.Cancel();
                if (_Run != null) Task.WhenAny(_Run, Task.Delay(5000)).GetAwaiter().GetResult();
                _OutputReader.Dispose();
                _InputReader.Dispose();
                _Console.Dispose();
                _Cts.Dispose();
            }
            _Disposed = true;
        }

        #endregion

        #region Private-Methods

        private async Task ReadLoopAsync()
        {
            try
            {
                using (StreamReader reader = new StreamReader(_OutputReader, new UTF8Encoding(false), false, 4096, true))
                {
                    while (true)
                    {
                        string? line = await reader.ReadLineAsync().ConfigureAwait(false);
                        if (line == null) break;
                        Lines.Enqueue(line);
                        await _Channel.Writer.WriteAsync(line).ConfigureAwait(false);
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                _Channel.Writer.TryComplete();
            }
        }

        #endregion
    }
}
