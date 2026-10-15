namespace Hugo.Core.Helpers
{
    using System;

    /// <summary>
    /// Timestamp normalization helpers. Thread-safe.
    /// </summary>
    public static class TimestampHelper
    {
        /// <summary>
        /// Normalize a timestamp to UTC kind. Unspecified values are assumed to already be UTC.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>UTC value.</returns>
        public static DateTime Normalize(DateTime value)
        {
            if (value.Kind == DateTimeKind.Utc) return value;
            if (value.Kind == DateTimeKind.Local) return value.ToUniversalTime();
            return DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }

        /// <summary>
        /// Normalize a nullable timestamp to UTC kind.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>UTC value or null.</returns>
        public static DateTime? Normalize(DateTime? value)
        {
            if (!value.HasValue) return null;
            return Normalize(value.Value);
        }
    }
}
