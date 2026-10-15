namespace Hugo.Sdk.Models
{
    using System;

    /// <summary>
    /// A count for one status value.
    /// </summary>
    public class StatusCount
    {
        #region Public-Members

        /// <summary>
        /// Status.
        /// </summary>
        public string Status { get; set; } = String.Empty;

        /// <summary>
        /// Count.
        /// </summary>
        public long Count { get; set; } = 0;

        #endregion
    }
}
