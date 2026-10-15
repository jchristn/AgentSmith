namespace Hugo.Sdk.Responses
{
    /// <summary>
    /// Result of a restart request.
    /// </summary>
    public class RestartResponse
    {
        #region Public-Members

        /// <summary>
        /// Restarting.
        /// </summary>
        public bool Restarting { get; set; } = false;

        /// <summary>
        /// Node.
        /// </summary>
        public string? Node { get; set; } = null;

        #endregion
    }
}
