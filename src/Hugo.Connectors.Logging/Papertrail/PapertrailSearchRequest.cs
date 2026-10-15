namespace Hugo.Connectors.Logging
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;

    /// <summary>
    /// Parameters of Papertrail <c>GET /events/search.json</c>. Null values are not sent.
    /// </summary>
    public class PapertrailSearchRequest
    {
        #region Public-Members

        /// <summary>
        /// Search query (<c>q</c>).
        /// </summary>
        public string? Query { get; set; } = null;

        /// <summary>
        /// System ID or name (<c>system_id</c>).
        /// </summary>
        public string? SystemId { get; set; } = null;

        /// <summary>
        /// Numeric group ID (<c>group_id</c>).
        /// </summary>
        public string? GroupId { get; set; } = null;

        /// <summary>
        /// Return events newer than this ID (<c>min_id</c>).
        /// </summary>
        public string? MinId { get; set; } = null;

        /// <summary>
        /// Return events older than this ID (<c>max_id</c>).
        /// </summary>
        public string? MaxId { get; set; } = null;

        /// <summary>
        /// Oldest time to examine (<c>min_time</c>, sent as epoch seconds).
        /// </summary>
        public DateTime? MinTime { get; set; } = null;

        /// <summary>
        /// Newest time to examine (<c>max_time</c>, sent as epoch seconds).
        /// </summary>
        public DateTime? MaxTime { get; set; } = null;

        /// <summary>
        /// Maximum events (<c>limit</c>). Minimum 1, maximum 10000 (the API maximum), default 1000.
        /// </summary>
        public int Limit
        {
            get
            {
                return _Limit;
            }
            set
            {
                _Limit = Math.Clamp(value, 1, 10000);
            }
        }

        /// <summary>
        /// Search approach (<c>tail</c>). Null leaves the API default.
        /// </summary>
        public bool? Tail { get; set; } = null;

        #endregion

        #region Private-Members

        private int _Limit = 1000;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Query string parameters.
        /// </summary>
        /// <returns>Parameters.</returns>
        public List<KeyValuePair<string, string?>> ToQuery()
        {
            List<KeyValuePair<string, string?>> q = new List<KeyValuePair<string, string?>>();
            q.Add(new KeyValuePair<string, string?>("q", Query));
            q.Add(new KeyValuePair<string, string?>("system_id", SystemId));
            q.Add(new KeyValuePair<string, string?>("group_id", GroupId));
            q.Add(new KeyValuePair<string, string?>("min_id", MinId));
            q.Add(new KeyValuePair<string, string?>("max_id", MaxId));
            q.Add(new KeyValuePair<string, string?>("min_time", MinTime.HasValue ? TimeRange.ToEpochSeconds(MinTime.Value).ToString(CultureInfo.InvariantCulture) : null));
            q.Add(new KeyValuePair<string, string?>("max_time", MaxTime.HasValue ? TimeRange.ToEpochSeconds(MaxTime.Value).ToString(CultureInfo.InvariantCulture) : null));
            q.Add(new KeyValuePair<string, string?>("limit", Limit.ToString(CultureInfo.InvariantCulture)));
            q.Add(new KeyValuePair<string, string?>("tail", Tail.HasValue ? (Tail.Value ? "true" : "false") : null));
            return q;
        }

        #endregion
    }
}
