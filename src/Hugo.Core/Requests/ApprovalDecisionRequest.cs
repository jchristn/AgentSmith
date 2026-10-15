namespace Hugo.Core.Requests
{
    using System;

    /// <summary>
    /// Approve, reject, or cancel an approval.
    /// </summary>
    public class ApprovalDecisionRequest
    {
        #region Public-Members

        /// <summary>
        /// Note recorded with the decision (at most 2048 characters).
        /// </summary>
        public string? Note { get; set; } = null;

        #endregion
    }
}
