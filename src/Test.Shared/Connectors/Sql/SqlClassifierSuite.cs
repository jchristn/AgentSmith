namespace Test.Shared.Connectors.Sql
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Sql;
    using Touchstone.Core;

    /// <summary>
    /// Table-driven suite for <see cref="StatementClassifier"/> and <see cref="SqlTokenizer"/>.
    /// </summary>
    public static class SqlClassifierSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Build()
        {
            const string suite = "connectors.sql.classifier";
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            List<ClassifierCase> table = ClassifierCases.Build();

            cases.Add(new TestCaseDescriptor(suite, "table-size", "Classifier table has at least 200 cases across all four dialects", ct =>
            {
                SqlTestSupport.Check(table.Count >= 200, "expected at least 200 cases, found " + table.Count);
                foreach (SqlEngineEnum engine in Enum.GetValues<SqlEngineEnum>())
                    SqlTestSupport.Check(table.Count(t => t.Engine == engine) >= 25, "at least 25 cases for " + engine);
                return Task.CompletedTask;
            }));

            for (int i = 0; i < table.Count; i++)
            {
                ClassifierCase row = table[i];
                string id = "case-" + (i + 1).ToString("D3", CultureInfo.InvariantCulture) + "-" + row.Engine.ToString().ToLowerInvariant();
                string expected = row.Rejected ? "rejected" : row.Expected.ToString();
                string display = row.Engine + (row.AllowMultiple ? " (batches allowed)" : String.Empty) + ": " + Shorten(row.Sql) + " => " + expected;
                cases.Add(new TestCaseDescriptor(suite, id, display, ct =>
                {
                    StatementClassification result = StatementClassifier.Classify(row.Sql, row.Engine, row.AllowMultiple);
                    SqlTestSupport.Check(result.Rejected == row.Rejected, "rejected expected " + row.Rejected + " but was " + result.Rejected + " (" + result.Reason + ")");
                    SqlTestSupport.Check(result.Effect == row.Expected, "effect expected " + row.Expected + " but was " + result.Effect + " (" + result.Reason + ")");
                    SqlTestSupport.Check(!String.IsNullOrEmpty(result.Reason), "reason present");
                    return Task.CompletedTask;
                }));
            }

            cases.Add(new TestCaseDescriptor(suite, "tokenizer-dialects", "Tokenizer honors dialect quoting and comment rules", ct =>
            {
                List<SqlToken> mysql = SqlTokenizer.Tokenize("SELECT 'a\\'b' # tail", SqlEngineEnum.Mysql);
                SqlTestSupport.Check(mysql.Count == 2 && mysql[1].Value == "a'b", "mysql backslash escape and hash comment");
                List<SqlToken> pg = SqlTokenizer.Tokenize("SELECT $q$x;y$q$, \"Col\"", SqlEngineEnum.Postgresql);
                SqlTestSupport.Check(pg[1].Kind == SqlTokenKindEnum.String && pg[1].Value == "x;y", "dollar quote content");
                SqlTestSupport.Check(pg[3].Kind == SqlTokenKindEnum.QuotedIdentifier && pg[3].Value == "Col", "double-quoted identifier");
                List<SqlToken> ms = SqlTokenizer.Tokenize("SELECT [a]]b] FROM #t", SqlEngineEnum.SqlServer);
                SqlTestSupport.Check(ms[1].Value == "a]b" && ms[3].Kind == SqlTokenKindEnum.Variable, "bracket identifier and temp table");
                List<SqlToken> lite = SqlTokenizer.Tokenize("select `a`, [b] from t", SqlEngineEnum.Sqlite);
                SqlTestSupport.Check(lite[0].Text == "SELECT" && lite[1].Kind == SqlTokenKindEnum.QuotedIdentifier && lite[3].Kind == SqlTokenKindEnum.QuotedIdentifier, "sqlite quoting and upper-cased words");
                bool threw = false;
                try
                {
                    SqlTokenizer.Tokenize("SELECT 'open", SqlEngineEnum.Postgresql);
                }
                catch (SqlParseException)
                {
                    threw = true;
                }
                SqlTestSupport.Check(threw, "unterminated string throws");
                return Task.CompletedTask;
            }));

            cases.Add(new TestCaseDescriptor(suite, "classification-details", "Classification reports types, counts, and reasons", ct =>
            {
                StatementClassification batch = StatementClassifier.Classify("SELECT 1; UPDATE t SET a = 1; DELETE FROM t", SqlEngineEnum.Postgresql, true);
                SqlTestSupport.Check(batch.StatementCount == 3 && batch.Parsed, "three parsed statements");
                SqlTestSupport.Check(batch.StatementTypes.SequenceEqual(new[] { "SELECT", "UPDATE", "DELETE" }), "types " + String.Join(",", batch.StatementTypes));
                SqlTestSupport.Check(batch.Effect == EffectEnum.Destructive && batch.Reason.Contains("DELETE", StringComparison.Ordinal), "most severe reason");
                StatementClassification cte = StatementClassifier.Classify("WITH d AS (DELETE FROM t RETURNING *) SELECT * FROM d", SqlEngineEnum.Postgresql);
                SqlTestSupport.Check(cte.PrimaryType == "DELETE" && cte.Reason.Contains("CTE", StringComparison.Ordinal), "cte classified as the modification: " + cte.PrimaryType + " " + cte.Reason);
                StatementClassification analyze = StatementClassifier.Classify("EXPLAIN ANALYZE UPDATE t SET a = 1", SqlEngineEnum.Postgresql);
                SqlTestSupport.Check(analyze.PrimaryType == "EXPLAIN ANALYZE UPDATE", "explain analyze type " + analyze.PrimaryType);
                StatementClassification unknown = StatementClassifier.Classify("FROBNICATE", SqlEngineEnum.Mysql);
                SqlTestSupport.Check(!unknown.Parsed && unknown.Effect == EffectEnum.Destructive, "unknown fails closed");
                return Task.CompletedTask;
            }));

            return new TestSuiteDescriptor(suite, "SQL statement classifier", cases);
        }

        #endregion

        #region Private-Methods

        private static string Shorten(string sql)
        {
            string s = sql.Replace("\n", " ", StringComparison.Ordinal);
            return s.Length > 70 ? s.Substring(0, 67) + "..." : s;
        }

        #endregion
    }
}
