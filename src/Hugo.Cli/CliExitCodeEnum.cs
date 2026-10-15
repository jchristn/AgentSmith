namespace Hugo.Cli
{
    /// <summary>
    /// Process exit codes of the hugo command line.
    /// </summary>
    public enum CliExitCodeEnum
    {
        /// <summary>
        /// The command completed.
        /// </summary>
        Success = 0,

        /// <summary>
        /// The command failed (server, network, credential, or file error).
        /// </summary>
        Error = 1,

        /// <summary>
        /// The command line was not valid (unknown command, unknown option, missing value).
        /// </summary>
        Usage = 2
    }
}
