namespace Hugo.Connectors.Logging
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;

    /// <summary>
    /// Parameters of SolarWinds Observability <c>GET /v1/metrics/{name}/measurements</c>. Null values are not sent.
    /// </summary>
    public class SwoMetricQuery
    {
        #region Public-Members

        /// <summary>
        /// Metric name (path segment). Required.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Attribute filter (for example <c>id:[e-123]</c>).
        /// </summary>
        public string? Filter { get; set; } = null;

        /// <summary>
        /// Comma-delimited attributes to group by.
        /// </summary>
        public string? GroupBy { get; set; } = null;

        /// <summary>
        /// Aggregation: <c>AVG</c>, <c>COUNT</c>, <c>MIN</c>, <c>MAX</c>, <c>SUM</c>, <c>LAST</c>. Default <c>AVG</c>.
        /// </summary>
        public string AggregateBy { get; set; } = "AVG";

        /// <summary>
        /// Series type: <c>TIMESERIES</c> or <c>SCALAR</c>. Default <c>TIMESERIES</c>.
        /// </summary>
        public string SeriesType { get; set; } = "TIMESERIES";

        /// <summary>
        /// Window start, UTC.
        /// </summary>
        public DateTime? StartTime { get; set; } = null;

        /// <summary>
        /// Window end, UTC.
        /// </summary>
        public DateTime? EndTime { get; set; } = null;

        /// <summary>
        /// Groupings per page. Minimum 1, maximum 1000, default 20.
        /// </summary>
        public int PageSize
        {
            get
            {
                return _PageSize;
            }
            set
            {
                _PageSize = Math.Clamp(value, 1, 1000);
            }
        }

        /// <summary>
        /// Page token.
        /// </summary>
        public string? SkipToken { get; set; } = null;

        #endregion

        #region Private-Members

        private int _PageSize = 20;

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
            q.Add(new KeyValuePair<string, string?>("groupBy", GroupBy));
            q.Add(new KeyValuePair<string, string?>("aggregateBy", AggregateBy));
            q.Add(new KeyValuePair<string, string?>("seriesType", SeriesType));
            q.Add(new KeyValuePair<string, string?>("startTime", StartTime.HasValue ? TimeRange.Format(StartTime.Value) : null));
            q.Add(new KeyValuePair<string, string?>("endTime", EndTime.HasValue ? TimeRange.Format(EndTime.Value) : null));
            q.Add(new KeyValuePair<string, string?>("pageSize", PageSize.ToString(CultureInfo.InvariantCulture)));
            q.Add(new KeyValuePair<string, string?>("skipToken", SkipToken));
            return q;
        }

        #endregion
    }
}
