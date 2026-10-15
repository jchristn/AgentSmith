// Ported from Tablix (https://github.com/jchristn/Tablix), commit 450bc764be478740db5bfc9ae9cb3fd4bb9c14c6,
// src/Tablix.Server/Handlers/FallbackQueryPlan.cs. Copyright (c) 2026 Joel Christner and Adam Wingate, MIT License.
// Modified for Hugo: namespace, nullable annotations; Intent dropped (sql_ask always wants data); added tables,
// assumptions, an ambiguity flag with a clarification question, and a schema-only answer. See Vendored/VENDORED.md.
namespace Hugo.Connectors.Sql.Intelligence
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The JSON plan a model returns for a question: SQL to run, or a clarification, or a schema-only answer.
    /// Not thread-safe for mutation.
    /// </summary>
    public class QueryPlan
    {
        #region Public-Members

        /// <summary>
        /// Whether a query should run.
        /// </summary>
        public bool Execute { get; set; } = false;

        /// <summary>
        /// One read-only SQL statement, or null.
        /// </summary>
        public string? Sql { get; set; } = null;

        /// <summary>
        /// Why this SQL answers the question.
        /// </summary>
        public string? Reason { get; set; } = null;

        /// <summary>
        /// Tables the SQL uses, or null.
        /// </summary>
        public List<string>? Tables { get; set; } = null;

        /// <summary>
        /// Assumptions the planner made, or null.
        /// </summary>
        public List<string>? Assumptions { get; set; } = null;

        /// <summary>
        /// Whether the planner found the question ambiguous.
        /// </summary>
        public bool Ambiguous { get; set; } = false;

        /// <summary>
        /// Clarification question when <see cref="Ambiguous"/> is true, or null.
        /// </summary>
        public string? Clarification { get; set; } = null;

        /// <summary>
        /// Answer from schema and context alone (no query), or null.
        /// </summary>
        public string? Answer { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public QueryPlan()
        {
        }

        #endregion
    }
}
