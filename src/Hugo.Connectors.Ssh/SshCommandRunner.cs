namespace Hugo.Connectors.Ssh
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Renci.SshNet;

    /// <summary>
    /// Runs one remote command with a timeout and bounded output capture. Standard output and standard error are read
    /// while the command runs into fixed-size tail buffers, so memory stays bounded however much the command prints.
    /// Thread-safe.
    /// </summary>
    public static class SshCommandRunner
    {
        #region Public-Methods

        /// <summary>
        /// Run a command.
        /// </summary>
        /// <param name="client">Connected client.</param>
        /// <param name="commandText">Remote command line.</param>
        /// <param name="timeout">Timeout. When it elapses the command is signalled and closed, and the result has <see cref="SshCommandResult.TimedOut"/> set.</param>
        /// <param name="captureBytes">Bytes kept per stream. Minimum 1024.</param>
        /// <param name="token">Cancellation token. Cancellation stops the command and throws.</param>
        /// <returns>Result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when client or commandText is null.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the token is cancelled.</exception>
        public static async Task<SshCommandResult> RunAsync(SshClient client, string commandText, TimeSpan timeout, int captureBytes, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(client);
            ArgumentNullException.ThrowIfNull(commandText);
            token.ThrowIfCancellationRequested();

            int capture = Math.Max(1024, captureBytes);
            SshTailBuffer stdout = new SshTailBuffer(capture);
            SshTailBuffer stderr = new SshTailBuffer(capture);
            Stopwatch sw = Stopwatch.StartNew();
            SshCommandResult result = new SshCommandResult();

            using (SshCommand command = client.CreateCommand(commandText))
            using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                cts.CancelAfter(timeout);
                Task execute = command.ExecuteAsync(cts.Token);
                Stream outStream = command.OutputStream;
                Stream errStream = command.ExtendedOutputStream;
                Task pumpOut = Task.Run(() => Pump(outStream, stdout), CancellationToken.None);
                Task pumpErr = Task.Run(() => Pump(errStream, stderr), CancellationToken.None);

                try
                {
                    await execute.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    result.TimedOut = true;
                }
                catch (OperationCanceledException)
                {
                    await DrainAsync(pumpOut, pumpErr).ConfigureAwait(false);
                    throw;
                }

                await DrainAsync(pumpOut, pumpErr).ConfigureAwait(false);
                if (!result.TimedOut)
                {
                    result.ExitCode = command.ExitStatus;
                    result.ExitSignal = command.ExitSignal;
                }
            }

            sw.Stop();
            result.DurationMs = sw.Elapsed.TotalMilliseconds;
            result.Stdout = stdout.ToString();
            result.Stderr = stderr.ToString();
            result.StdoutBytes = stdout.TotalBytes;
            result.StderrBytes = stderr.TotalBytes;
            result.StdoutDropped = stdout.Dropped;
            result.StderrDropped = stderr.Dropped;
            return result;
        }

        #endregion

        #region Private-Methods

        private static void Pump(Stream? source, SshTailBuffer target)
        {
            if (source == null) return;
            byte[] buffer = new byte[16384];
            try
            {
                int read;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0) target.Write(buffer, 0, read);
            }
            catch (ObjectDisposedException)
            {
            }
            catch (IOException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

        private static async Task DrainAsync(Task a, Task b)
        {
            Task both = Task.WhenAll(a, b);
            Task finished = await Task.WhenAny(both, Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None)).ConfigureAwait(false);
            if (finished == both) await both.ConfigureAwait(false);
        }

        #endregion
    }
}
