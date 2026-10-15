namespace Test.Shared.Connectors.Sql
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// A deterministic <see cref="IConnectorModel"/> for tests. Rules are matched in order on the request purpose and
    /// prompt content; the first match produces the response. Unmatched context requests get a generic context, and
    /// unmatched answer requests echo the first result cell, so answers are always derived from the rows in the prompt.
    /// Records every request. Thread-safe.
    /// </summary>
    public class ScriptedConnectorModel : IConnectorModel
    {
        #region Public-Members

        /// <summary>
        /// Model name reported in responses.
        /// </summary>
        public string ModelName { get; set; } = "scripted-test-model";

        #endregion

        #region Private-Members

        private readonly List<ScriptedRule> _Rules = new List<ScriptedRule>();
        private readonly List<ConnectorModelRequest> _Requests = new List<ConnectorModelRequest>();
        private readonly object _Lock = new object();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Add a rule.
        /// </summary>
        /// <param name="purpose">Purpose to match (for example <c>sql.nl2sql</c>).</param>
        /// <param name="contains">Text the user prompt must contain, or null for any.</param>
        /// <param name="respond">Response factory.</param>
        /// <returns>This model.</returns>
        public ScriptedConnectorModel On(string purpose, string? contains, Func<ConnectorModelRequest, string> respond)
        {
            lock (_Lock)
            {
                _Rules.Add(new ScriptedRule(purpose, contains, respond));
            }
            return this;
        }

        /// <summary>
        /// Add a rule with a fixed response.
        /// </summary>
        /// <param name="purpose">Purpose.</param>
        /// <param name="contains">Text the prompt must contain, or null.</param>
        /// <param name="response">Response text.</param>
        /// <returns>This model.</returns>
        public ScriptedConnectorModel On(string purpose, string? contains, string response)
        {
            return On(purpose, contains, r => response);
        }

        /// <summary>
        /// Requests received so far, optionally filtered by purpose.
        /// </summary>
        /// <param name="purpose">Purpose, or null for all.</param>
        /// <returns>Requests.</returns>
        public List<ConnectorModelRequest> Requests(string? purpose = null)
        {
            lock (_Lock)
            {
                return _Requests.Where(r => purpose == null || r.Purpose == purpose).ToList();
            }
        }

        /// <inheritdoc />
        public Task<ConnectorModelResponse> CompleteAsync(ConnectorModelRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            token.ThrowIfCancellationRequested();
            ScriptedRule? rule;
            lock (_Lock)
            {
                _Requests.Add(request);
                rule = _Rules.FirstOrDefault(r => r.Purpose == request.Purpose && (r.Contains == null || request.UserPrompt.Contains(r.Contains, StringComparison.OrdinalIgnoreCase)));
            }
            string text = rule != null ? rule.Respond(request) : Default(request);
            return Task.FromResult(new ConnectorModelResponse { Text = text, Model = ModelName, InputTokens = request.UserPrompt.Length / 4, OutputTokens = text.Length / 4 });
        }

        /// <summary>
        /// First data cell of the result table in an answer prompt, or null.
        /// </summary>
        /// <param name="prompt">Answer prompt.</param>
        /// <returns>Cell text.</returns>
        public static string? FirstCell(string prompt)
        {
            List<string> lines = prompt.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("|", StringComparison.Ordinal)).ToList();
            if (lines.Count < 3) return null;
            string row = lines[2];
            string[] cells = row.Trim('|').Split('|');
            return cells.Length == 0 ? null : cells[0].Trim();
        }

        #endregion

        #region Private-Methods

        private static string Default(ConnectorModelRequest request)
        {
            switch (request.Purpose)
            {
                case "sql.generate-database-context":
                    return "```markdown\nShop database: customers place orders of products. Revenue means SUM(orders.total) for orders whose status is not cancelled.\n```";
                case "sql.generate-table-context":
                    Match m = Regex.Match(request.UserPrompt, "Selected table: (\\S+)");
                    return "Table " + (m.Success ? m.Groups[1].Value : "unknown") + ": generated context describing its purpose and joins.";
                case "sql.answer":
                    return "The result shows " + (FirstCell(request.UserPrompt) ?? "no rows") + ".";
                default:
                    throw new ConnectorException("upstream_error", "No scripted response for purpose " + request.Purpose + ".");
            }
        }

        #endregion
    }
}
