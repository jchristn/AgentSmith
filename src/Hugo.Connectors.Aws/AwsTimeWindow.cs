namespace Hugo.Connectors.Aws
{
    using System;
    using System.Globalization;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// A UTC time window resolved from tool arguments: <c>startTime</c> and <c>endTime</c> (ISO 8601, UTC when no offset
    /// is given) or <c>lookbackMinutes</c> before <c>endTime</c> (default now). Windows longer than the resource's
    /// <c>maxTimeRangeHours</c> are refused with <c>time_range_exceeded</c>.
    /// </summary>
    public class AwsTimeWindow
    {
        #region Public-Members

        /// <summary>
        /// Start (UTC).
        /// </summary>
        public DateTime StartUtc { get; set; } = DateTime.UtcNow.AddHours(-1);

        /// <summary>
        /// End (UTC).
        /// </summary>
        public DateTime EndUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Length of the window.
        /// </summary>
        public TimeSpan Length
        {
            get
            {
                return EndUtc - StartUtc;
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Resolve a window from arguments.
        /// </summary>
        /// <param name="arguments">Arguments.</param>
        /// <param name="options">Options (default lookback and maximum range).</param>
        /// <param name="nowUtc">Current time (UTC).</param>
        /// <returns>Window.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        /// <exception cref="ConnectorException">Thrown with <c>invalid_argument</c> for unparseable or inverted times and
        /// <c>time_range_exceeded</c> when the window is longer than allowed.</exception>
        public static AwsTimeWindow Resolve(ToolArguments arguments, AwsOptions options, DateTime nowUtc)
        {
            ArgumentNullException.ThrowIfNull(arguments);
            ArgumentNullException.ThrowIfNull(options);

            DateTime end = Parse(arguments.GetString("endTime"), "endTime") ?? nowUtc;
            DateTime? start = Parse(arguments.GetString("startTime"), "startTime");
            if (start == null)
            {
                int lookback = arguments.GetInt("lookbackMinutes", options.DefaultLookbackMinutes, 1, Int32.MaxValue);
                start = end.AddMinutes(-lookback);
            }

            if (start.Value >= end)
                throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "startTime must be before endTime.");
            TimeSpan length = end - start.Value;
            if (length > TimeSpan.FromHours(options.MaxTimeRangeHours))
                throw new ConnectorException(
                    ConnectorErrorCodes.TimeRangeExceeded,
                    "The time window is " + Math.Round(length.TotalHours, 1).ToString(CultureInfo.InvariantCulture) + " hours; this resource allows at most "
                    + options.MaxTimeRangeHours.ToString(CultureInfo.InvariantCulture) + " hours (maxTimeRangeHours). Narrow startTime and endTime.");

            AwsTimeWindow window = new AwsTimeWindow();
            window.StartUtc = start.Value;
            window.EndUtc = end;
            return window;
        }

        /// <summary>
        /// Describe the window, for example <c>2026-10-14T10:00:00Z to 2026-10-14T11:00:00Z</c>.
        /// </summary>
        /// <returns>Text.</returns>
        public override string ToString()
        {
            return AwsResultBuilder.Time(StartUtc) + " to " + AwsResultBuilder.Time(EndUtc);
        }

        #endregion

        #region Private-Methods

        private static DateTime? Parse(string? value, string name)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            if (DateTimeOffset.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset parsed))
                return parsed.UtcDateTime;
            throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, name + " '" + value + "' is not an ISO 8601 timestamp such as 2026-10-14T09:30:00Z.");
        }

        #endregion
    }
}
