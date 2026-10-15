namespace Hugo.Core.Secrets
{
    using System;

    /// <summary>
    /// Progress of a master key rotation (P2.10).
    /// </summary>
    public class MasterKeyRotationStatus
    {
        /// <summary>
        /// Whether a rotation is running.
        /// </summary>
        public bool Running { get; set; } = false;

        /// <summary>
        /// Key version values are re-encrypted to.
        /// </summary>
        public int TargetKeyVersion { get; set; } = 0;

        /// <summary>
        /// Secret values and credential secrets found under an older key version when the run started.
        /// </summary>
        public long Total { get; set; } = 0;

        /// <summary>
        /// Rows re-encrypted so far.
        /// </summary>
        public long Completed { get; set; } = 0;

        /// <summary>
        /// Rows that could not be decrypted with any configured key and were left unchanged.
        /// </summary>
        public long Failed { get; set; } = 0;

        /// <summary>
        /// Secret values still under an older key version (refreshed when a run ends and on every status read).
        /// </summary>
        public long Remaining { get; set; } = 0;

        /// <summary>
        /// When the last run started (UTC), or null.
        /// </summary>
        public DateTime? StartedUtc { get; set; } = null;

        /// <summary>
        /// When the last run ended (UTC), or null.
        /// </summary>
        public DateTime? CompletedUtc { get; set; } = null;

        /// <summary>
        /// Last error message, or null.
        /// </summary>
        public string? LastError { get; set; } = null;
    }
}
