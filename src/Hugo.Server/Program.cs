namespace Hugo.Server
{
    /// <summary>
    /// Application entry point.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Main entry point.
        /// </summary>
        /// <param name="args">Command-line arguments; the first may be the settings file path.</param>
        /// <returns>Exit code.</returns>
        public static int Main(string[] args)
        {
            return Bootstrapper.Run(args);
        }
    }
}
