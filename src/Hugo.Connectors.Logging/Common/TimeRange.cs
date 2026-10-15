namespace Hugo.Connectors.Logging
{
    using System;
    using System.Globalization;

    /// <summary>
    /// A resolved UTC time window. Treat as immutable.
    /// </summary>
    public class TimeRange
    {
        #region Public-Members

        /// <summary>
        /// Inclusive start, UTC.
        /// </summary>
        public DateTime Start { get; }

        /// <summary>
        /// Inclusive end, UTC.
        /// </summary>
        public DateTime End { get; }

        /// <summary>
        /// Length of the window.
        /// </summary>
        public TimeSpan Duration
        {
            get
            {
                return End - Start;
            }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="start">Start. Converted to UTC.</param>
        /// <param name="end">End. Converted to UTC.</param>
        /// <exception cref="ArgumentException">Thrown when start is after end.</exception>
        public TimeRange(DateTime start, DateTime end)
        {
            DateTime s = ToUtc(start);
            DateTime e = ToUtc(end);
            if (s > e) throw new ArgumentException("Start " + Format(s) + " is after end " + Format(e) + ".", nameof(start));
            Start = s;
            End = e;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Format a UTC time as <c>yyyy-MM-ddTHH:mm:ssZ</c>.
        /// </summary>
        /// <param name="value">Time.</param>
        /// <returns>Text.</returns>
        public static string Format(DateTime value)
        {
            return ToUtc(value).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Convert to Unix epoch seconds.
        /// </summary>
        /// <param name="value">Time.</param>
        /// <returns>Seconds since 1970-01-01 UTC.</returns>
        public static long ToEpochSeconds(DateTime value)
        {
            return new DateTimeOffset(ToUtc(value)).ToUnixTimeSeconds();
        }

        /// <summary>
        /// Treat unspecified kinds as UTC and convert local times to UTC.
        /// </summary>
        /// <param name="value">Time.</param>
        /// <returns>UTC time.</returns>
        public static DateTime ToUtc(DateTime value)
        {
            if (value.Kind == DateTimeKind.Utc) return value;
            if (value.Kind == DateTimeKind.Unspecified) return DateTime.SpecifyKind(value, DateTimeKind.Utc);
            return value.ToUniversalTime();
        }

        #endregion
    }
}
