namespace Hugo.Sdk.Responses
{
    using System;

    /// <summary>
    /// Master key version and rotation progress.
    /// </summary>
    public class MasterKeyRotationStatus
    {
        #region Public-Members

        /// <summary>
        /// Running.
        /// </summary>
        public bool Running { get; set; } = false;

        /// <summary>
        /// Target key version.
        /// </summary>
        public int TargetKeyVersion { get; set; } = 0;

        /// <summary>
        /// Total.
        /// </summary>
        public long Total { get; set; } = 0;

        /// <summary>
        /// Completed.
        /// </summary>
        public long Completed { get; set; } = 0;

        /// <summary>
        /// Failed.
        /// </summary>
        public long Failed { get; set; } = 0;

        /// <summary>
        /// Remaining.
        /// </summary>
        public long Remaining { get; set; } = 0;

        /// <summary>
        /// Started UTC.
        /// </summary>
        public DateTime? StartedUtc { get; set; } = null;

        /// <summary>
        /// Completed UTC.
        /// </summary>
        public DateTime? CompletedUtc { get; set; } = null;

        /// <summary>
        /// Last error.
        /// </summary>
        public string? LastError { get; set; } = null;

        #endregion
    }
}
