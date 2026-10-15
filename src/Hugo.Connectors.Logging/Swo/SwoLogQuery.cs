namespace Hugo.Connectors.Logging
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;

    /// <summary>
    /// Parameters of SolarWinds Observability <c>GET /v1/logs</c>. Null values are not sent.
    /// </summary>
    public class SwoLogQuery
    {
        #region Public-Members

        /// <summary>
        /// Search query (<c>filter</c>).
        /// </summary>
        public string? Filter { get; set; } = null;

        /// <summary>
        /// Log group name (<c>group</c>).
        /// </summary>
        public string? Group { get; set; } = null;

        /// <summary>
        /// Entity ID (<c>entityId</c>).
        /// </summary>
        public string? EntityId { get; set; } = null;

        /// <summary>
        /// Window start (<c>startTime</c>), UTC.
        /// </summary>
        public DateTime? StartTime { get; set; } = null;

        /// <summary>
        /// Window end (<c>endTime</c>), UTC.
        /// </summary>
        public DateTime? EndTime { get; set; } = null;

        /// <summary>
        /// Direction (<c>backward</c>, <c>forward</c>, <c>tail</c>). Null uses the API default (backward).
        /// </summary>
        public string? Direction { get; set; } = null;

        /// <summary>
        /// Page size (<c>pageSize</c>). Minimum 1, maximum 10000, default 100.
        /// </summary>
        public int PageSize
        {
            get
            {
                return _PageSize;
            }
            set
            {
                _PageSize = Math.Clamp(value, 1, 10000);
            }
        }

        /// <summary>
        /// Page token (<c>skipToken</c>).
        /// </summary>
        public string? SkipToken { get; set; } = null;

        #endregion

        #region Private-Members

        private int _PageSize = 100;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Query string parameters.
        /// </summary>
        /// <returns>Parameters.</returns>
        public List<KeyValuePair<string, string?>> ToQuery()
        {
            List<KeyValuePair<string, string?>> q = new List<KeyValuePair<string, string?>>();
            q.Add(new KeyValuePair<string, string?>("filter", Filter));
            q.Add(new KeyValuePair<string, string?>("group", Group));
            q.Add(new KeyValuePair<string, string?>("entityId", EntityId));
            q.Add(new KeyValuePair<string, string?>("startTime", StartTime.HasValue ? TimeRange.Format(StartTime.Value) : null));
            q.Add(new KeyValuePair<string, string?>("endTime", EndTime.HasValue ? TimeRange.Format(EndTime.Value) : null));
            q.Add(new KeyValuePair<string, string?>("direction", Direction));
            q.Add(new KeyValuePair<string, string?>("pageSize", PageSize.ToString(CultureInfo.InvariantCulture)));
            q.Add(new KeyValuePair<string, string?>("skipToken", SkipToken));
            return q;
        }

        #endregion
    }
}
