namespace Hugo.Sdk.Requests
{
    using System;

    /// <summary>
    /// Clone or duplicate an entity under a new name.
    /// </summary>
    public class CloneRequest
    {
        #region Public-Members

        /// <summary>
        /// Name of the copy.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        #endregion
    }
}
