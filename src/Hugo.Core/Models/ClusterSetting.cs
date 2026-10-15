namespace Hugo.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Hugo.Core.Enums;
    using Hugo.Core.Helpers;

    /// <summary>
    /// Cluster runtime setting stored in the database.
    /// </summary>
    public class ClusterSetting
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
                _Id = String.IsNullOrWhiteSpace(value) ? IdGenerator.NewClusterSettingId() : value;
            }
        }

        /// <summary>
        /// Setting key, for example Runs.DefaultTimeoutSeconds.
        /// </summary>
        public string SettingKey
        {
            get
            {
                return _SettingKey;
            }
            set
            {
                _SettingKey = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Setting value.
        /// </summary>
        public string? SettingValue { get; set; } = null;

        /// <summary>
        /// Principal that last changed the value.
        /// </summary>
        public string? UpdatedBy { get; set; } = null;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc
        {
            get
            {
                return _CreatedUtc;
            }
            set
            {
                _CreatedUtc = TimestampHelper.Normalize(value);
            }
        }

        /// <summary>
        /// Last update (UTC).
        /// </summary>
        public DateTime LastUpdateUtc
        {
            get
            {
                return _LastUpdateUtc;
            }
            set
            {
                _LastUpdateUtc = TimestampHelper.Normalize(value);
            }
        }

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.NewClusterSettingId();
        private string _SettingKey = String.Empty;
        private DateTime _CreatedUtc = DateTime.UtcNow;
        private DateTime _LastUpdateUtc = DateTime.UtcNow;

        #endregion
    }
}
