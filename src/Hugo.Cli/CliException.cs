namespace Hugo.Cli
{
    using System;

    /// <summary>
    /// Thrown when a command fails for a reason the operator can act on (not signed in, server unreachable, file not
    /// writable). The message is printed to stderr and the command exits with <see cref="CliExitCodeEnum.Error"/>.
    /// </summary>
    public class CliException : Exception
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="message">Message for the operator.</param>
        public CliException(string message) : base(message)
        {
        }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="message">Message for the operator.</param>
        /// <param name="inner">The underlying exception.</param>
        public CliException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}
