// Ported from Tablix (https://github.com/jchristn/Tablix), commit 450bc764be478740db5bfc9ae9cb3fd4bb9c14c6,
// src/Tablix.Core/Models/AmbiguitySignal.cs. Copyright (c) 2026 Joel Christner and Adam Wingate, MIT License.
// Modified for Hugo: namespace, nullable annotations. See Vendored/VENDORED.md.
namespace Hugo.Connectors.Sql.Intelligence
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A term or intent that the schema can answer in more than one way (for example "latest" when several timestamp
    /// columns exist), with the clarification question to ask before running SQL. Not thread-safe for mutation.
    /// </summary>
    public class AmbiguitySignal
    {
        #region Public-Members

        /// <summary>
        /// Ambiguous concept (for example <c>revenue</c>, <c>active</c>, <c>latest</c>, <c>status</c>).
        /// </summary>
        public string Term { get; set; } = String.Empty;

        /// <summary>
        /// Why the term is ambiguous.
        /// </summary>
        public string Reason { get; set; } = String.Empty;

        /// <summary>
        /// Clarification question to ask before executing SQL.
        /// </summary>
        public string Question { get; set; } = String.Empty;

        /// <summary>
        /// Candidate interpretations (qualified columns or tables). Never null.
        /// </summary>
        public List<string> Candidates
        {
            get
            {
                return _Candidates;
            }
            set
            {
                _Candidates = value ?? new List<string>();
            }
        }

        #endregion

        #region Private-Members

        private List<string> _Candidates = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AmbiguitySignal()
        {
        }

        #endregion
    }
}
