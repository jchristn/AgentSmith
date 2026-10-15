namespace Hugo.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Hugo.Core.Enums;
    using Hugo.Core.Helpers;

    /// <summary>
    /// Child record of RequestHistoryEntry: captured request and response headers (secrets redacted).
    /// </summary>
    public class RequestHistoryHeader
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id
        {
            get
            {
                return _Id;
            }
            set
            {
                _Id = String.IsNullOrWhiteSpace(value) ? IdGenerator.NewRequestHistoryHeaderId() : value;
            }
        }

        /// <summary>
        /// Parent RequestHistoryEntry identifier.
        /// </summary>
        public string RequestHistoryEntryId
        {
            get
            {
                return _RequestHistoryEntryId;
            }
            set
            {
                _RequestHistoryEntryId = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Request or response.
        /// </summary>
        public HeaderDirectionEnum Direction { get; set; } = HeaderDirectionEnum.Request;

        /// <summary>
        /// Header name.
        /// </summary>
        public string Name
        {
            get
            {
                return _Name;
            }
            set
            {
                _Name = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Header value.
        /// </summary>
        public string Value
        {
            get
            {
                return _Value;
            }
            set
            {
                _Value = value ?? String.Empty;
            }
        }

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.NewRequestHistoryHeaderId();
        private string _RequestHistoryEntryId = String.Empty;
        private string _Name = String.Empty;
        private string _Value = String.Empty;

        #endregion
    }
}
