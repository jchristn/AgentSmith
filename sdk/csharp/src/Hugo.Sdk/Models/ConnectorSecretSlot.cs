namespace Hugo.Sdk.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A secret slot a connector declares (preview).
    /// </summary>
    public class ConnectorSecretSlot
    {
        #region Public-Members

        /// <summary>
        /// Name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Label.
        /// </summary>
        public string? Label { get; set; } = null;

        /// <summary>
        /// Help.
        /// </summary>
        public string? Help { get; set; } = null;

        /// <summary>
        /// Accepted secret kinds.
        /// </summary>
        public List<string> AcceptedKinds { get; set; } = new List<string>();

        /// <summary>
        /// Required.
        /// </summary>
        public bool Required { get; set; } = false;

        #endregion
    }
}
