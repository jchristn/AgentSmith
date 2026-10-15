namespace Hugo.Connectors.Sql.Intelligence
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Hugo.Connectors.Sql.Vendored.Tablix;

    /// <summary>
    /// Chooses which columns to sample for context generation and masks sensitive values. Samples only ever appear
    /// inside a prompt; they are never stored. Thread-safe for concurrent use once configured.
    /// </summary>
    public class SampleMasker
    {
        #region Public-Members

        /// <summary>
        /// Maximum columns sampled per table. Minimum 1, maximum 200, default 24.
        /// </summary>
        public int MaxColumns
        {
            get
            {
                return _MaxColumns;
            }
            set
            {
                _MaxColumns = Math.Clamp(value, 1, 200);
            }
        }

        /// <summary>
        /// Maximum characters kept per value. Minimum 8, maximum 1000, default 60.
        /// </summary>
        public int MaxValueChars
        {
            get
            {
                return _MaxValueChars;
            }
            set
            {
                _MaxValueChars = Math.Clamp(value, 8, 1000);
            }
        }

        #endregion

        #region Private-Members

        private static readonly string[] _SecretWords = { "password", "passwd", "pwd", "secret", "token", "hash", "salt", "apikey", "key", "ssn", "iban", "cvv", "pin", "card", "credential", "otp" };
        private static readonly string[] _PersonalWords = { "email", "mail", "phone", "mobile", "address", "street", "birth", "dob", "ip", "passport", "license" };
        private int _MaxColumns = 24;
        private int _MaxValueChars = 60;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public SampleMasker()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Columns worth sampling: skips binary columns, keeps ordinal order, capped at <see cref="MaxColumns"/>.
        /// </summary>
        /// <param name="table">Table.</param>
        /// <returns>Column names.</returns>
        public List<string> Columns(TableDetail table)
        {
            ArgumentNullException.ThrowIfNull(table);
            return table.Columns
                .Where(c => NameHeuristics.TypeFamily(c.DataType) != "binary")
                .Select(c => c.ColumnName)
                .Take(_MaxColumns)
                .ToList();
        }

        /// <summary>
        /// Mask a sampled result in place: secret-like columns become <c>(redacted)</c>, personal-data columns become
        /// <c>(personal data)</c>, and long values are cut.
        /// </summary>
        /// <param name="sample">Sample.</param>
        /// <exception cref="ArgumentNullException">Thrown when sample is null.</exception>
        public void Mask(SqlQueryResult sample)
        {
            ArgumentNullException.ThrowIfNull(sample);
            for (int c = 0; c < sample.Columns.Count; c++)
            {
                string kind = Classify(sample.Columns[c].Name);
                foreach (List<string?> row in sample.Rows)
                {
                    if (c >= row.Count || row[c] == null) continue;
                    if (kind == "secret") row[c] = "(redacted)";
                    else if (kind == "personal") row[c] = "(personal data)";
                    else if (row[c]!.Length > _MaxValueChars) row[c] = row[c]!.Substring(0, _MaxValueChars) + "...";
                }
            }
        }

        #endregion

        #region Private-Methods

        private static string Classify(string column)
        {
            List<string> words = NameHeuristics.Words(column);
            string normalized = NameHeuristics.NormalizeIdentifier(column);
            if (words.Any(w => _SecretWords.Contains(w)) || normalized.Contains("password", StringComparison.Ordinal) || normalized.Contains("apikey", StringComparison.Ordinal)) return "secret";
            if (words.Any(w => _PersonalWords.Contains(w)) || normalized.Contains("email", StringComparison.Ordinal)) return "personal";
            return "plain";
        }

        #endregion
    }
}
