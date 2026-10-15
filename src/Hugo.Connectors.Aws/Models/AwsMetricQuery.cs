namespace Hugo.Connectors.Aws.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One metric or metric math expression requested from aws_cloudwatch_get_metric_data.
    /// </summary>
    public class AwsMetricQuery
    {
        #region Public-Members

        /// <summary>
        /// Query ID used by expressions (lowercase letter first). Generated when empty.
        /// </summary>
        public string? Id { get; set; } = null;

        /// <summary>
        /// Metric namespace, for example <c>AWS/EC2</c>. Required unless <see cref="Expression"/> is set.
        /// </summary>
        public string? Namespace { get; set; } = null;

        /// <summary>
        /// Metric name, for example <c>CPUUtilization</c>. Required unless <see cref="Expression"/> is set.
        /// </summary>
        public string? MetricName { get; set; } = null;

        /// <summary>
        /// Dimensions as name to value, for example <c>{"InstanceId": "i-0abc"}</c>.
        /// </summary>
        public Dictionary<string, string> Dimensions
        {
            get
            {
                return _Dimensions;
            }
            set
            {
                _Dimensions = value ?? new Dictionary<string, string>();
            }
        }

        /// <summary>
        /// Statistic: Average, Sum, Minimum, Maximum, SampleCount, or a percentile such as p99. Default Average.
        /// </summary>
        public string? Stat { get; set; } = null;

        /// <summary>
        /// Metric math expression, for example <c>m0 / m1 * 100</c>, instead of a metric.
        /// </summary>
        public string? Expression { get; set; } = null;

        /// <summary>
        /// Label shown in results.
        /// </summary>
        public string? Label { get; set; } = null;

        #endregion

        #region Private-Members

        private Dictionary<string, string> _Dimensions = new Dictionary<string, string>(StringComparer.Ordinal);

        #endregion
    }
}
