namespace Hugo.Core.Database.Ado
{
    using System;
    using System.Data.Common;
    using System.Globalization;

    /// <summary>
    /// Column readers that tolerate the small type differences between providers. Thread-safe.
    /// </summary>
    public static class AdoConverters
    {
        /// <summary>
        /// Read a string column; null becomes empty.
        /// </summary>
        /// <param name="reader">Reader.</param>
        /// <param name="column">Column.</param>
        /// <returns>Value.</returns>
        public static string Str(DbDataReader reader, string column)
        {
            int ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? String.Empty : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture) ?? String.Empty;
        }

        /// <summary>
        /// Read a nullable string column.
        /// </summary>
        /// <param name="reader">Reader.</param>
        /// <param name="column">Column.</param>
        /// <returns>Value or null.</returns>
        public static string? NullableStr(DbDataReader reader, string column)
        {
            int ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? null : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Read a 32-bit integer column; null becomes 0.
        /// </summary>
        /// <param name="reader">Reader.</param>
        /// <param name="column">Column.</param>
        /// <returns>Value.</returns>
        public static int Int(DbDataReader reader, string column)
        {
            int ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Read a nullable 32-bit integer column.
        /// </summary>
        /// <param name="reader">Reader.</param>
        /// <param name="column">Column.</param>
        /// <returns>Value or null.</returns>
        public static int? NullableInt(DbDataReader reader, string column)
        {
            int ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? null : Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Read a 64-bit integer column; null becomes 0.
        /// </summary>
        /// <param name="reader">Reader.</param>
        /// <param name="column">Column.</param>
        /// <returns>Value.</returns>
        public static long Long(DbDataReader reader, string column)
        {
            int ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? 0 : Convert.ToInt64(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Read a nullable 64-bit integer column.
        /// </summary>
        /// <param name="reader">Reader.</param>
        /// <param name="column">Column.</param>
        /// <returns>Value or null.</returns>
        public static long? NullableLong(DbDataReader reader, string column)
        {
            int ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? null : Convert.ToInt64(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Read a double column; null becomes 0.
        /// </summary>
        /// <param name="reader">Reader.</param>
        /// <param name="column">Column.</param>
        /// <returns>Value.</returns>
        public static double Double(DbDataReader reader, string column)
        {
            int ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? 0 : Convert.ToDouble(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Read a nullable double column.
        /// </summary>
        /// <param name="reader">Reader.</param>
        /// <param name="column">Column.</param>
        /// <returns>Value or null.</returns>
        public static double? NullableDouble(DbDataReader reader, string column)
        {
            int ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? null : Convert.ToDouble(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Read a boolean column stored as a boolean or an integer; null becomes false.
        /// </summary>
        /// <param name="reader">Reader.</param>
        /// <param name="column">Column.</param>
        /// <returns>Value.</returns>
        public static bool Bool(DbDataReader reader, string column)
        {
            return NullableBool(reader, column) ?? false;
        }

        /// <summary>
        /// Read a nullable boolean column.
        /// </summary>
        /// <param name="reader">Reader.</param>
        /// <param name="column">Column.</param>
        /// <returns>Value or null.</returns>
        public static bool? NullableBool(DbDataReader reader, string column)
        {
            int ordinal = reader.GetOrdinal(column);
            if (reader.IsDBNull(ordinal)) return null;
            object value = reader.GetValue(ordinal);
            if (value is bool b) return b;
            return Convert.ToInt64(value, CultureInfo.InvariantCulture) != 0;
        }

        /// <summary>
        /// Read a UTC timestamp column; null becomes DateTime.UtcNow.
        /// </summary>
        /// <param name="reader">Reader.</param>
        /// <param name="column">Column.</param>
        /// <returns>Value.</returns>
        public static DateTime Date(DbDataReader reader, string column)
        {
            return NullableDate(reader, column) ?? DateTime.UtcNow;
        }

        /// <summary>
        /// Read a nullable UTC timestamp column.
        /// </summary>
        /// <param name="reader">Reader.</param>
        /// <param name="column">Column.</param>
        /// <returns>Value or null.</returns>
        public static DateTime? NullableDate(DbDataReader reader, string column)
        {
            int ordinal = reader.GetOrdinal(column);
            if (reader.IsDBNull(ordinal)) return null;
            object value = reader.GetValue(ordinal);
            if (value is DateTime dt) return dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt.Kind == DateTimeKind.Local ? dt.ToUniversalTime() : dt, DateTimeKind.Utc);
            if (value is DateTimeOffset dto) return dto.UtcDateTime;
            string? text = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (String.IsNullOrEmpty(text)) return null;
            return DateTime.SpecifyKind(DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal), DateTimeKind.Utc);
        }

        /// <summary>
        /// Read an enum stored by name; unknown or null values become the fallback.
        /// </summary>
        /// <typeparam name="T">Enum type.</typeparam>
        /// <param name="reader">Reader.</param>
        /// <param name="column">Column.</param>
        /// <param name="fallback">Fallback value.</param>
        /// <returns>Value.</returns>
        public static T Enum<T>(DbDataReader reader, string column, T fallback) where T : struct, System.Enum
        {
            return NullableEnum<T>(reader, column) ?? fallback;
        }

        /// <summary>
        /// Read a nullable enum stored by name.
        /// </summary>
        /// <typeparam name="T">Enum type.</typeparam>
        /// <param name="reader">Reader.</param>
        /// <param name="column">Column.</param>
        /// <returns>Value or null.</returns>
        public static T? NullableEnum<T>(DbDataReader reader, string column) where T : struct, System.Enum
        {
            string? text = NullableStr(reader, column);
            if (String.IsNullOrEmpty(text)) return null;
            if (System.Enum.TryParse<T>(text, true, out T parsed)) return parsed;
            return null;
        }
    }
}
