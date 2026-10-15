namespace Hugo.Connectors.Abstractions
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    /// Renders rows as a compact pipe-delimited table that LLMs read reliably. Thread-safe.
    /// </summary>
    public static class TextTable
    {
        /// <summary>
        /// Render a table. Cell newlines become spaces and pipes are escaped. Cells longer than
        /// <paramref name="maxCellChars"/> are shortened with an ellipsis marker.
        /// </summary>
        /// <param name="columns">Column names.</param>
        /// <param name="rows">Rows; each row is aligned with the columns. Null cells render as <c>NULL</c>.</param>
        /// <param name="maxCellChars">Maximum characters per cell. Minimum 8, default 500.</param>
        /// <returns>Table text.</returns>
        /// <exception cref="ArgumentNullException">Thrown when columns or rows is null.</exception>
        public static string Render(IList<string> columns, IList<IList<string?>> rows, int maxCellChars = 500)
        {
            ArgumentNullException.ThrowIfNull(columns);
            ArgumentNullException.ThrowIfNull(rows);
            if (maxCellChars < 8) maxCellChars = 8;

            StringBuilder sb = new StringBuilder();
            sb.Append("| ").Append(String.Join(" | ", Clean(columns, maxCellChars))).Append(" |\n");
            sb.Append('|');
            for (int i = 0; i < columns.Count; i++) sb.Append(" --- |");
            sb.Append('\n');
            foreach (IList<string?> row in rows)
            {
                List<string> cells = new List<string>();
                for (int i = 0; i < columns.Count; i++)
                {
                    string? cell = i < row.Count ? row[i] : null;
                    cells.Add(cell == null ? "NULL" : CleanCell(cell, maxCellChars));
                }
                sb.Append("| ").Append(String.Join(" | ", cells)).Append(" |\n");
            }
            return sb.ToString();
        }

        private static List<string> Clean(IList<string> values, int maxCellChars)
        {
            List<string> cleaned = new List<string>();
            foreach (string v in values) cleaned.Add(CleanCell(v ?? String.Empty, maxCellChars));
            return cleaned;
        }

        private static string CleanCell(string value, int maxCellChars)
        {
            string v = value.Replace("\r", " ").Replace("\n", " ").Replace("|", "\\|");
            if (v.Length > maxCellChars) v = v.Substring(0, maxCellChars - 3) + "...";
            return v;
        }
    }
}
