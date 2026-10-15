namespace Hugo.Core.Helpers
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using Hugo.Core.Models;

    /// <summary>
    /// Clamps a numeric value into its allowed range and records a <see cref="FieldWarning"/> with the code
    /// ValueClamped when the value changed (I-08). Model setters use it so the stored value stays compatible (out of
    /// range values are still accepted and clamped) while create, update, and manifest responses can say what happened.
    /// A later assignment of the same field replaces the earlier warning, or removes it when the new value is in range.
    /// Not thread-safe for one warnings list.
    /// </summary>
    public static class ValueClamp
    {
        #region Public-Methods

        /// <summary>
        /// Clamp an integer.
        /// </summary>
        /// <param name="value">Given value.</param>
        /// <param name="minimum">Minimum allowed value.</param>
        /// <param name="maximum">Maximum allowed value; must not be below minimum.</param>
        /// <param name="field">Field name for the warning.</param>
        /// <param name="warnings">List that receives the warning.</param>
        /// <returns>The clamped value.</returns>
        /// <exception cref="ArgumentNullException">Thrown when warnings is null.</exception>
        public static int Int(int value, int minimum, int maximum, string field, List<FieldWarning> warnings)
        {
            if (warnings == null) throw new ArgumentNullException(nameof(warnings));
            int clamped = Math.Clamp(value, minimum, maximum);
            Record(field, value != clamped, Text(value), Text(clamped), Text(minimum), Text(maximum), warnings);
            return clamped;
        }

        /// <summary>
        /// Clamp an optional integer; null stays null.
        /// </summary>
        /// <param name="value">Given value, or null.</param>
        /// <param name="minimum">Minimum allowed value.</param>
        /// <param name="maximum">Maximum allowed value; must not be below minimum.</param>
        /// <param name="field">Field name for the warning.</param>
        /// <param name="warnings">List that receives the warning.</param>
        /// <returns>The clamped value, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when warnings is null.</exception>
        public static int? NullableInt(int? value, int minimum, int maximum, string field, List<FieldWarning> warnings)
        {
            if (warnings == null) throw new ArgumentNullException(nameof(warnings));
            if (!value.HasValue)
            {
                Record(field, false, null, null, null, null, warnings);
                return null;
            }
            return Int(value.Value, minimum, maximum, field, warnings);
        }

        /// <summary>
        /// Clamp a long integer.
        /// </summary>
        /// <param name="value">Given value.</param>
        /// <param name="minimum">Minimum allowed value.</param>
        /// <param name="maximum">Maximum allowed value; must not be below minimum.</param>
        /// <param name="field">Field name for the warning.</param>
        /// <param name="warnings">List that receives the warning.</param>
        /// <returns>The clamped value.</returns>
        /// <exception cref="ArgumentNullException">Thrown when warnings is null.</exception>
        public static long Long(long value, long minimum, long maximum, string field, List<FieldWarning> warnings)
        {
            if (warnings == null) throw new ArgumentNullException(nameof(warnings));
            long clamped = Math.Clamp(value, minimum, maximum);
            Record(field, value != clamped, Text(value), Text(clamped), Text(minimum), Text(maximum), warnings);
            return clamped;
        }

        /// <summary>
        /// Clamp an optional long integer; null stays null.
        /// </summary>
        /// <param name="value">Given value, or null.</param>
        /// <param name="minimum">Minimum allowed value.</param>
        /// <param name="maximum">Maximum allowed value; must not be below minimum.</param>
        /// <param name="field">Field name for the warning.</param>
        /// <param name="warnings">List that receives the warning.</param>
        /// <returns>The clamped value, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when warnings is null.</exception>
        public static long? NullableLong(long? value, long minimum, long maximum, string field, List<FieldWarning> warnings)
        {
            if (warnings == null) throw new ArgumentNullException(nameof(warnings));
            if (!value.HasValue)
            {
                Record(field, false, null, null, null, null, warnings);
                return null;
            }
            return Long(value.Value, minimum, maximum, field, warnings);
        }

        #endregion

        #region Private-Methods

        private static void Record(string field, bool clamped, string? given, string? stored, string? minimum, string? maximum, List<FieldWarning> warnings)
        {
            warnings.RemoveAll(w => String.Equals(w.Field, field, StringComparison.Ordinal) && w.Code == "ValueClamped");
            if (!clamped) return;
            string message = field + " " + given + " is outside the allowed range " + minimum + " to " + maximum + "; " + stored + " was stored.";
            warnings.Add(new FieldWarning(field, "ValueClamped", given, stored, message));
        }

        private static string Text(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        #endregion
    }
}
