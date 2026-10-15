namespace Hugo.Sdk.Responses
{
    /// <summary>
    /// Number of records a bulk delete removed.
    /// </summary>
    public class DeleteCountResponse
    {
        #region Public-Members

        /// <summary>
        /// Deleted count.
        /// </summary>
        public long DeletedCount { get; set; } = 0;

        #endregion
    }
}
