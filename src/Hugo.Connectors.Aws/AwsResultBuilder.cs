namespace Hugo.Connectors.Aws
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Aws.Models;

    /// <summary>
    /// Renders an <see cref="AwsTable"/> into a compact text result (heading, markdown table, notes, paging hint) plus
    /// structured JSON, applying the call's row, line, and byte limits. Thread-safe.
    /// </summary>
    public static class AwsResultBuilder
    {
        #region Public-Methods

        /// <summary>
        /// Render a table.
        /// </summary>
        /// <param name="context">Invocation context (limits).</param>
        /// <param name="tool">Tool name.</param>
        /// <param name="region">Region.</param>
        /// <param name="account">Account ID, or null.</param>
        /// <param name="table">Table.</param>
        /// <returns>Result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when context or table is null.</exception>
        public static ToolResult Render(ConnectorInvocationContext context, string tool, string region, string? account, AwsTable table)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(table);

            int maxRows = context.Limits.MaxRows;
            bool truncated = table.Truncated;
            List<IList<string?>> rows = table.Rows;
            if (rows.Count > maxRows)
            {
                rows = rows.Take(maxRows).ToList();
                truncated = true;
            }

            StringBuilder sb = new StringBuilder();
            string where = "region " + region + (String.IsNullOrEmpty(account) ? String.Empty : ", account " + account);
            if (table.Columns.Count > 0)
            {
                sb.Append(table.Heading).Append(" (").Append(where).Append("): ").Append(rows.Count.ToString(CultureInfo.InvariantCulture)).Append(rows.Count == 1 ? " row" : " rows");
                if (truncated) sb.Append(", truncated to the output limit");
                sb.Append(".\n");
                if (rows.Count > 0 && !table.RowsInTrailerOnly) sb.Append(TextTable.Render(table.Columns, rows, 300));
            }
            else
            {
                sb.Append(table.Heading).Append(" (").Append(where).Append(").\n");
            }

            foreach (KeyValuePair<string, string?> detail in table.Details)
            {
                if (table.Columns.Count > 0) break;
                sb.Append(detail.Key).Append(": ").Append(detail.Value ?? "NULL").Append('\n');
            }

            foreach (string note in table.Notes) sb.Append(note).Append('\n');
            if (!String.IsNullOrEmpty(table.NextToken))
                sb.Append("More results are available: call again with ").Append(table.NextTokenArgument).Append(" = \"").Append(table.NextToken).Append("\".\n");
            if (!String.IsNullOrEmpty(table.Trailer)) sb.Append(table.Trailer);

            ToolResult result = new ToolResult();
            result.Text = OutputLimiter.Head(sb.ToString(), context.Limits.MaxBytes, context.Limits.MaxLines, result);
            truncated = truncated || result.Truncated;
            result.Truncated = truncated;

            AwsOutput output = new AwsOutput();
            output.Tool = tool;
            output.Region = region;
            output.Account = String.IsNullOrEmpty(account) ? null : account;
            output.NextToken = String.IsNullOrEmpty(table.NextToken) ? null : table.NextToken;
            output.Truncated = truncated;
            output.Details = table.Details.Count > 0 ? table.Details : null;
            output.Notes = table.Notes.Count > 0 ? table.Notes : null;
            if (table.Columns.Count > 0)
            {
                output.Columns = table.Columns;
                output.Items = rows.Select(r => ToItem(table.Columns, r)).ToList();
                output.Count = output.Items.Count;
            }

            string json = AwsJson.Serialize(output);
            while (output.Items != null && output.Items.Count > 0 && Encoding.UTF8.GetByteCount(json) > context.Limits.MaxBytes)
            {
                output.Items = output.Items.Take(output.Items.Count / 2).ToList();
                output.Count = output.Items.Count;
                output.Truncated = true;
                result.Truncated = true;
                json = AwsJson.Serialize(output);
            }
            result.StructuredJson = json;
            result.Metrics["rows"] = rows.Count;
            return result;
        }

        /// <summary>
        /// Format a timestamp as ISO 8601 UTC with seconds, or null.
        /// </summary>
        /// <param name="value">Timestamp.</param>
        /// <returns>Text or null.</returns>
        public static string? Time(DateTime? value)
        {
            if (value == null || value.Value == DateTime.MinValue) return null;
            DateTime utc = value.Value.Kind == DateTimeKind.Local ? value.Value.ToUniversalTime() : value.Value;
            return utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Format a number with invariant culture, or null.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>Text or null.</returns>
        public static string? Number(double? value)
        {
            if (value == null) return null;
            return value.Value.ToString("0.####", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Format an integer with invariant culture, or null.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>Text or null.</returns>
        public static string? Number(long? value)
        {
            if (value == null) return null;
            return value.Value.ToString(CultureInfo.InvariantCulture);
        }

        #endregion

        #region Private-Methods

        private static Dictionary<string, string?> ToItem(List<string> columns, IList<string?> row)
        {
            Dictionary<string, string?> item = new Dictionary<string, string?>(StringComparer.Ordinal);
            for (int i = 0; i < columns.Count; i++) item[columns[i]] = i < row.Count ? row[i] : null;
            return item;
        }

        #endregion
    }
}
