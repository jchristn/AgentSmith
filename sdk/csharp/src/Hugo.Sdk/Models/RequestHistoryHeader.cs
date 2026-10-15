namespace Hugo.Sdk.Models
{
    using System;
    using Hugo.Sdk.Enums;

    /// <summary>
    /// A captured request or response header.
    /// </summary>
    public class RequestHistoryHeader
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string? Id { get; set; } = null;

        /// <summary>
        /// Request history entry Identifier.
        /// </summary>
        public string? RequestHistoryEntryId { get; set; } = null;

        /// <summary>
        /// Request or Response.
        /// </summary>
        public HeaderDirectionEnum Direction { get; set; } = HeaderDirectionEnum.Request;

        /// <summary>
        /// Name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Value.
        /// </summary>
        public string? Value { get; set; } = null;

        #endregion
    }
}
