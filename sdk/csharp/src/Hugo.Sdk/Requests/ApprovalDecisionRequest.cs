namespace Hugo.Sdk.Requests
{
    /// <summary>
    /// Approve, reject, or cancel an approval (preview).
    /// </summary>
    public class ApprovalDecisionRequest
    {
        #region Public-Members

        /// <summary>
        /// Decision note.
        /// </summary>
        public string? Note { get; set; } = null;

        #endregion
    }
}
