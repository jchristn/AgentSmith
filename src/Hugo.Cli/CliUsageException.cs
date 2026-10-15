namespace Hugo.Cli
{
    using System;

    /// <summary>
    /// Thrown when the command line is not valid. The command exits with <see cref="CliExitCodeEnum.Usage"/>.
    /// </summary>
    public class CliUsageException : Exception
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="message">Message describing what is wrong with the command line.</param>
        public CliUsageException(string message) : base(message)
        {
        }
    }
}
