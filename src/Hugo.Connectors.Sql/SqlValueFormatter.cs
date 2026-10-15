namespace Hugo.Connectors.Sql
{
    using System;
    using System.Globalization;
    using System.Text;

    /// <summary>
    /// Renders database values as compact invariant-culture strings. Thread-safe.
    /// </summary>
    public static class SqlValueFormatter
    {
        /// <summary>
        /// Format a value. DBNull and null become null; binary values become <c>0x</c> hex, shortened after
        /// <paramref name="maxBinaryBytes"/> bytes; dates use ISO 8601.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <param name="maxBinaryBytes">Maximum binary bytes rendered. Minimum 1, default 64.</param>
        /// <returns>Text or null.</returns>
        public static string? Format(object? value, int maxBinaryBytes = 64)
        {
            if (value == null || value is DBNull) return null;
            if (maxBinaryBytes < 1) maxBinaryBytes = 1;
            switch (value)
            {
                case string s:
                    return s;
                case bool b:
                    return b ? "true" : "false";
                case byte[] bytes:
                    StringBuilder sb = new StringBuilder("0x");
                    int count = Math.Min(bytes.Length, maxBinaryBytes);
                    for (int i = 0; i < count; i++) sb.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
                    if (bytes.Length > count) sb.Append("... (" + bytes.Length + " bytes)");
                    return sb.ToString();
                case DateTime dt:
                    return dt.TimeOfDay == TimeSpan.Zero && dt.Kind != DateTimeKind.Utc
                        ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                        : dt.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture).TrimEnd('.');
                case DateTimeOffset dto:
                    return dto.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFFzzz", CultureInfo.InvariantCulture);
                case Guid g:
                    return g.ToString();
                case IFormattable formattable:
                    return formattable.ToString(null, CultureInfo.InvariantCulture);
                case Array array:
                    StringBuilder items = new StringBuilder("{");
                    bool firstItem = true;
                    foreach (object? item in array)
                    {
                        if (!firstItem) items.Append(',');
                        items.Append(Format(item, maxBinaryBytes) ?? "NULL");
                        firstItem = false;
                    }
                    return items.Append('}').ToString();
                default:
                    return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }
    }
}
