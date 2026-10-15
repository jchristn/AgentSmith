namespace Hugo.Connectors.Observability
{
    using System;
    using System.Globalization;
    using System.Text.RegularExpressions;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Time parsing and formatting shared by the Prometheus and Loki connectors. Accepts RFC 3339 timestamps, Unix
    /// seconds (with an optional fraction), <c>now</c>, <c>now-15m</c>, and relative durations such as <c>15m</c>,
    /// <c>2h</c>, <c>1d</c> meaning that long before now. Thread-safe.
    /// </summary>
    public static class ObservabilityTime
    {
        #region Private-Members

        private static readonly Regex _Duration = new Regex("^(?:(\\d+(?:\\.\\d+)?)(ms|s|m|h|d|w|y))+$", RegexOptions.CultureInvariant);
        private static readonly Regex _DurationPart = new Regex("(\\d+(?:\\.\\d+)?)(ms|s|m|h|d|w|y)", RegexOptions.CultureInvariant);
        private static readonly Regex _Number = new Regex("^\\d+(\\.\\d+)?$", RegexOptions.CultureInvariant);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Parse a duration: a Prometheus-style duration (<c>30s</c>, <c>5m</c>, <c>1h30m</c>, <c>1d</c>, <c>1w</c>) or a
        /// plain number of seconds.
        /// </summary>
        /// <param name="value">Text.</param>
        /// <param name="argumentName">Argument name for error messages.</param>
        /// <returns>Duration.</returns>
        /// <exception cref="ConnectorException">Thrown with <c>invalid_argument</c> when the text is not a duration.</exception>
        public static TimeSpan ParseDuration(string value, string argumentName)
        {
            string v = (value ?? String.Empty).Trim().ToLowerInvariant();
            if (_Number.IsMatch(v)) return TimeSpan.FromSeconds(Double.Parse(v, CultureInfo.InvariantCulture));
            if (!_Duration.IsMatch(v))
                throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Argument '" + argumentName + "' must be a duration such as 30s, 5m, 1h, or 1d, or a number of seconds; got '" + Shorten(value) + "'.");
            double seconds = 0;
            foreach (Match m in _DurationPart.Matches(v))
            {
                double n = Double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                switch (m.Groups[2].Value)
                {
                    case "ms": seconds += n / 1000.0; break;
                    case "s": seconds += n; break;
                    case "m": seconds += n * 60; break;
                    case "h": seconds += n * 3600; break;
                    case "d": seconds += n * 86400; break;
                    case "w": seconds += n * 604800; break;
                    default: seconds += n * 31536000; break;
                }
            }
            return TimeSpan.FromSeconds(seconds);
        }

        /// <summary>
        /// Parse a point in time.
        /// </summary>
        /// <param name="value">Text: RFC 3339, Unix seconds, <c>now</c>, <c>now-15m</c>, or a duration before now.</param>
        /// <param name="now">Current time.</param>
        /// <param name="argumentName">Argument name for error messages.</param>
        /// <returns>UTC time.</returns>
        /// <exception cref="ConnectorException">Thrown with <c>invalid_argument</c> when the text cannot be parsed.</exception>
        public static DateTimeOffset ParsePoint(string value, DateTimeOffset now, string argumentName)
        {
            string v = (value ?? String.Empty).Trim();
            string lower = v.ToLowerInvariant();
            if (lower.Length == 0 || lower == "now") return now.ToUniversalTime();
            if (lower.StartsWith("now-", StringComparison.Ordinal)) return now.ToUniversalTime() - ParseDuration(lower.Substring(4), argumentName);
            if (_Number.IsMatch(lower))
            {
                double seconds = Double.Parse(lower, CultureInfo.InvariantCulture);
                if (seconds > 1e17) seconds = seconds / 1e9;
                else if (seconds > 1e11) seconds = seconds / 1e3;
                return DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Round(seconds * 1000.0));
            }
            if (_Duration.IsMatch(lower)) return now.ToUniversalTime() - ParseDuration(lower, argumentName);
            if (DateTimeOffset.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset parsed))
                return parsed.ToUniversalTime();
            throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Argument '" + argumentName + "' must be an RFC 3339 timestamp, Unix seconds, 'now', or a duration before now such as 15m, 2h, 1d; got '" + Shorten(value) + "'.");
        }

        /// <summary>
        /// Resolve a bounded window from optional start and end arguments.
        /// </summary>
        /// <param name="start">Start text, or null for <paramref name="defaultStart"/>.</param>
        /// <param name="end">End text, or null for now.</param>
        /// <param name="defaultStart">Default start, for example <c>1h</c>.</param>
        /// <param name="maxRangeHours">Longest allowed window in hours.</param>
        /// <param name="now">Current time.</param>
        /// <returns>Window.</returns>
        /// <exception cref="ConnectorException">Thrown with <c>invalid_argument</c> for unparseable or reversed times, and <c>time_range_exceeded</c> for windows over the limit.</exception>
        public static ObservabilityTimeWindow ResolveWindow(string? start, string? end, string defaultStart, int maxRangeHours, DateTimeOffset now)
        {
            DateTimeOffset e = String.IsNullOrWhiteSpace(end) ? now.ToUniversalTime() : ParsePoint(end, now, "end");
            DateTimeOffset s = String.IsNullOrWhiteSpace(start) ? e - ParseDuration(defaultStart, "start") : ParsePoint(start, now, "start");
            if (s >= e) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "The start time " + Format(s) + " must be before the end time " + Format(e) + ".");
            if ((e - s).TotalHours > maxRangeHours)
                throw new ConnectorException(ConnectorErrorCodes.TimeRangeExceeded, "The window from " + Format(s) + " to " + Format(e) + " is longer than this resource's maxRangeHours (" + maxRangeHours.ToString(CultureInfo.InvariantCulture) + "). Split it into shorter windows or narrow it.");
            return new ObservabilityTimeWindow(s, e);
        }

        /// <summary>
        /// Format as Unix seconds with millisecond precision (the Prometheus API form).
        /// </summary>
        /// <param name="value">Time.</param>
        /// <returns>Text.</returns>
        public static string UnixSeconds(DateTimeOffset value)
        {
            return (value.ToUnixTimeMilliseconds() / 1000.0).ToString("0.###", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Format as Unix nanoseconds (the Loki API form).
        /// </summary>
        /// <param name="value">Time.</param>
        /// <returns>Text.</returns>
        public static string UnixNanoseconds(DateTimeOffset value)
        {
            long ticks = value.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks;
            return (new System.Numerics.BigInteger(ticks) * 100).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Convert Unix seconds (with fraction) to a time.
        /// </summary>
        /// <param name="seconds">Seconds.</param>
        /// <returns>UTC time.</returns>
        public static DateTimeOffset FromUnixSeconds(double seconds)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Round(seconds * 1000.0));
        }

        /// <summary>
        /// Convert Unix nanoseconds text to a time. Unparseable text gives the Unix epoch.
        /// </summary>
        /// <param name="nanoseconds">Nanoseconds as text.</param>
        /// <returns>UTC time.</returns>
        public static DateTimeOffset FromUnixNanoseconds(string? nanoseconds)
        {
            if (String.IsNullOrEmpty(nanoseconds) || !Int64.TryParse(nanoseconds, NumberStyles.Integer, CultureInfo.InvariantCulture, out long ns)) return DateTimeOffset.UnixEpoch;
            return DateTimeOffset.UnixEpoch.AddTicks(ns / 100);
        }

        /// <summary>
        /// Format as ISO 8601 UTC with seconds (<c>2026-10-14T08:00:00Z</c>).
        /// </summary>
        /// <param name="value">Time.</param>
        /// <returns>Text.</returns>
        public static string Format(DateTimeOffset value)
        {
            return value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Format as ISO 8601 UTC with milliseconds (<c>2026-10-14T08:00:00.123Z</c>).
        /// </summary>
        /// <param name="value">Time.</param>
        /// <returns>Text.</returns>
        public static string FormatPrecise(DateTimeOffset value)
        {
            return value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Format a duration compactly (<c>90s</c> becomes <c>1m30s</c>).
        /// </summary>
        /// <param name="value">Duration.</param>
        /// <returns>Text.</returns>
        public static string FormatDuration(TimeSpan value)
        {
            long total = (long)Math.Round(value.TotalSeconds);
            if (total <= 0) return "0s";
            string text = String.Empty;
            long days = total / 86400;
            long hours = (total % 86400) / 3600;
            long minutes = (total % 3600) / 60;
            long seconds = total % 60;
            if (days > 0) text += days.ToString(CultureInfo.InvariantCulture) + "d";
            if (hours > 0) text += hours.ToString(CultureInfo.InvariantCulture) + "h";
            if (minutes > 0) text += minutes.ToString(CultureInfo.InvariantCulture) + "m";
            if (seconds > 0) text += seconds.ToString(CultureInfo.InvariantCulture) + "s";
            return text;
        }

        #endregion

        #region Private-Methods

        private static string Shorten(string? value)
        {
            string v = value ?? String.Empty;
            return v.Length > 60 ? v.Substring(0, 57) + "..." : v;
        }

        #endregion
    }
}
