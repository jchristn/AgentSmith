namespace Hugo.Cli
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Entry point of the hugo command line.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Main. Exit codes: 0 success, 1 error, 2 invalid command line.
        /// </summary>
        /// <param name="args">Arguments.</param>
        /// <returns>Exit code.</returns>
        public static async Task<int> Main(string[] args)
        {
            using (CliConsole console = CliConsole.CreateSystem())
            {
                using (CancellationTokenSource cts = new CancellationTokenSource())
                {
                    // Standard output belongs to the command (for 'mcp stdio', to the protocol). Anything that writes to
                    // Console.Out by accident lands on standard error instead.
                    Console.SetOut(console.StandardError);
                    Console.CancelKeyPress += (sender, e) =>
                    {
                        e.Cancel = true;
                        cts.Cancel();
                        // A blocked read of standard input may not observe cancellation; do not hang on Ctrl+C.
                        Task.Delay(3000).ContinueWith(t => Environment.Exit((int)CliExitCodeEnum.Error), TaskScheduler.Default);
                    };
                    return await CliApplication.RunAsync(args, console, cts.Token).ConfigureAwait(false);
                }
            }
        }
    }
}
