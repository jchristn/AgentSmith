namespace Hugo.Server.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Coordinates a restart requested through the settings API (P1.34). The host drains and exits with
    /// <see cref="RestartExitCode"/> (75) so the container restart policy starts it again. Thread-safe.
    /// </summary>
    public static class RestartCoordinator
    {
        #region Public-Members

        /// <summary>
        /// Exit code used after a restart request (75, EX_TEMPFAIL).
        /// </summary>
        public static readonly int RestartExitCode = 75;

        /// <summary>
        /// Process exit code: 0 normally, <see cref="RestartExitCode"/> after a restart request.
        /// </summary>
        public static int ExitCode
        {
            get
            {
                return Volatile.Read(ref _ExitCode);
            }
        }

        /// <summary>
        /// Whether a restart has been requested.
        /// </summary>
        public static bool RestartRequested
        {
            get
            {
                return Volatile.Read(ref _ExitCode) == RestartExitCode;
            }
        }

        /// <summary>
        /// Raised after the requested delay; the bootstrapper subscribes and begins a graceful shutdown. In-process test
        /// hosts that do not subscribe are not stopped.
        /// </summary>
        public static event EventHandler? Requested;

        #endregion

        #region Private-Members

        private static int _ExitCode = 0;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Request a restart after a short delay (so the response reaches the caller first).
        /// </summary>
        /// <param name="delayMs">Delay in milliseconds. Minimum 0, maximum 60000.</param>
        public static void RequestRestart(int delayMs)
        {
            Interlocked.Exchange(ref _ExitCode, RestartExitCode);
            int delay = Math.Clamp(delayMs, 0, 60000);
            _ = Task.Run(async () =>
            {
                await Task.Delay(delay).ConfigureAwait(false);
                Requested?.Invoke(null, EventArgs.Empty);
            });
        }

        #endregion
    }
}
