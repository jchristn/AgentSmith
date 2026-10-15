namespace Hugo.Sdk.Models
{
    using System;

    /// <summary>
    /// Whether a principal may perform one operation on one resource type.
    /// </summary>
    public class PermissionCheck
    {
        #region Public-Members

        /// <summary>
        /// Resource type.
        /// </summary>
        public string ResourceType { get; set; } = String.Empty;

        /// <summary>
        /// Operation.
        /// </summary>
        public string Operation { get; set; } = String.Empty;

        /// <summary>
        /// Permitted.
        /// </summary>
        public bool Permitted { get; set; } = false;

        #endregion
    }
}
