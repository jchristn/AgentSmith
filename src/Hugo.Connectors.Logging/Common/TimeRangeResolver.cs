namespace Hugo.Connectors.Logging
{
    using System;
    using System.Globalization;
    using System.Text.RegularExpressions;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Parses tool time arguments and enforces the resource's maximum window. A time is either an ISO-8601 timestamp
    /// (UTC assumed when no offset is given), the word <c>now</c>, or a relative duration before now: a whole number
    /// followed by <c>s</c>, <c>m</c>, <c>h</c>, <c>d</c>, or <c>w</c> (for example <c>15m</c>, <c>2h</c>, <c>1d</c>).
    /// Thread-safe.
    /// </summary>
    public static class TimeRangeResolver
    {
        #region Private-Members

        private static readonly Regex _Relative = new Regex(@"^\s*(\d{1,7})\s*([smhdw])\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Parse one point in time.
        /// </summary>
        /// <param name="value">ISO-8601 timestamp, <c>now</c>, or a relative duration.</param>
        /// <param name="nowUtc">Current time, UTC.</param>
        /// <param name="argumentName">Argument name for error messages.</param>
        /// <returns>UTC time.</returns>
        /// <exception cref="ConnectorException">Thrown with code <c>invalid_argument</c> when the value cannot be parsed.</exception>
        public static DateTime ParsePoint(string value, DateTime nowUtc, string argumentName)
        {
            if (String.IsNullOrWhiteSpace(value)) throw new ConnectorException("invalid_argument", "Argument '" + argumentName + "' is empty.");
            string v = value.Trim();
            DateTime now = TimeRange.ToUtc(nowUtc);
            if (String.Equals(v, "now", StringComparison.OrdinalIgnoreCase)) return now;

            Match m = _Relative.Match(v);
            if (m.Success)
            {
                long amount = Int64.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                TimeSpan span;
                switch (Char.ToLowerInvariant(m.Groups[2].Value[0]))
                {
                    case 's': span = TimeSpan.FromSeconds(amount); break;
                    case 'm': span = TimeSpan.FromMinutes(amount); break;
                    case 'h': span = TimeSpan.FromHours(amount); break;
                    case 'd': span = TimeSpan.FromDays(amount); break;
                    default: span = TimeSpan.FromDays(amount * 7); break;
                }
                return now - span;
            }

            if (DateTimeOffset.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset parsed))
                return parsed.UtcDateTime;

            throw new ConnectorException(
                "invalid_argument",
                "Argument '" + argumentName + "' value '" + v + "' is not an ISO-8601 timestamp or a relative duration such as 15m, 2h, or 1d.");
        }

        /// <summary>
        /// Resolve a window from optional start and end arguments and enforce the maximum length.
        /// </summary>
        /// <param name="start">Start argument, or null to use <paramref name="defaultStart"/>.</param>
        /// <param name="end">End argument, or null for now.</param>
        /// <param name="defaultStart">Default start (for example <c>1h</c>).</param>
        /// <param name="maxTimeRangeHours">Maximum window length in hours. Minimum 1.</param>
        /// <param name="nowUtc">Current time, UTC.</param>
        /// <returns>Window.</returns>
        /// <exception cref="ConnectorException">Thrown with code <c>invalid_argument</c> when a value cannot be parsed or start is not before end,
        /// or with code <c>time_range_exceeded</c> when the window is longer than allowed.</exception>
        public static TimeRange Resolve(string? start, string? end, string defaultStart, int maxTimeRangeHours, DateTime nowUtc)
        {
            DateTime now = TimeRange.ToUtc(nowUtc);
            DateTime e = String.IsNullOrWhiteSpace(end) ? now : ParsePoint(end, now, "endTime");
            DateTime s = ParsePoint(String.IsNullOrWhiteSpace(start) ? defaultStart : start, now, "startTime");
            if (s >= e)
                throw new ConnectorException("invalid_argument", "startTime " + TimeRange.Format(s) + " must be before endTime " + TimeRange.Format(e) + ".");

            int max = Math.Max(1, maxTimeRangeHours);
            TimeSpan length = e - s;
            if (length > TimeSpan.FromHours(max))
            {
                throw new ConnectorException(
                    "time_range_exceeded",
                    "The requested window is " + length.TotalHours.ToString("0.#", CultureInfo.InvariantCulture) + " hours; this resource allows at most "
                    + max + " hours (setting maxTimeRangeHours). Narrow startTime and endTime, then search adjacent windows if needed.");
            }

            return new TimeRange(s, e);
        }

        #endregion
    }
}
