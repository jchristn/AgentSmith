// Ported from Tablix (https://github.com/jchristn/Tablix), commit 450bc764be478740db5bfc9ae9cb3fd4bb9c14c6,
// src/Tablix.Core/Models/VerifiedAnswer.cs. Copyright (c) 2026 Joel Christner and Adam Wingate, MIT License.
// Modified for Hugo: namespace, nullable annotations, status enum, answer text, bounded evidence rows, clarification,
// ambiguity signals, assumptions, attempts, and the tables used. See Vendored/VENDORED.md.
namespace Hugo.Connectors.Sql.Intelligence
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The verification envelope <c>sql_ask</c> returns: status, answer, SQL, row count, and the evidence behind it.
    /// Not thread-safe for mutation.
    /// </summary>
    public class VerifiedAnswer
    {
        #region Public-Members

        /// <summary>
        /// Verification status.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public VerificationStatusEnum Status { get; set; } = VerificationStatusEnum.Blocked;

        /// <summary>
        /// The question asked.
        /// </summary>
        public string Question { get; set; } = String.Empty;

        /// <summary>
        /// Answer text written by the model from the actual result rows, or null when there is none.
        /// </summary>
        public string? Answer { get; set; } = null;

        /// <summary>
        /// One-line summary of the verification state.
        /// </summary>
        public string Summary { get; set; } = String.Empty;

        /// <summary>
        /// Clarification question when the status is Ambiguous, or null.
        /// </summary>
        public string? Clarification { get; set; } = null;

        /// <summary>
        /// Ambiguity signals that applied. Never null.
        /// </summary>
        public List<AmbiguitySignal> Ambiguities
        {
            get
            {
                return _Ambiguities;
            }
            set
            {
                _Ambiguities = value ?? new List<AmbiguitySignal>();
            }
        }

        /// <summary>
        /// Assumptions and clarifications the caller supplied, or the planner stated. Never null.
        /// </summary>
        public List<string> Assumptions
        {
            get
            {
                return _Assumptions;
            }
            set
            {
                _Assumptions = value ?? new List<string>();
            }
        }

        /// <summary>
        /// SQL that was executed (or that was blocked), or null.
        /// </summary>
        public string? Sql { get; set; } = null;

        /// <summary>
        /// Statement classification of <see cref="Sql"/> (for example <c>Read (SELECT)</c>), or null.
        /// </summary>
        public string? Classification { get; set; } = null;

        /// <summary>
        /// Tables the plan used. Never null.
        /// </summary>
        public List<string> Tables
        {
            get
            {
                return _Tables;
            }
            set
            {
                _Tables = value ?? new List<string>();
            }
        }

        /// <summary>
        /// Rows returned by the query, or null when nothing ran.
        /// </summary>
        public int? RowCount { get; set; } = null;

        /// <summary>
        /// Whether the result hit the row cap (more rows existed).
        /// </summary>
        public bool Truncated { get; set; } = false;

        /// <summary>
        /// Result columns. Never null.
        /// </summary>
        public List<string> Columns
        {
            get
            {
                return _Columns;
            }
            set
            {
                _Columns = value ?? new List<string>();
            }
        }

        /// <summary>
        /// The first result rows (bounded by the evidence row cap). Never null.
        /// </summary>
        public List<List<string?>> EvidenceRows
        {
            get
            {
                return _EvidenceRows;
            }
            set
            {
                _EvidenceRows = value ?? new List<List<string?>>();
            }
        }

        /// <summary>
        /// Evidence statements (what ran, how many rows, runtime, retries). Never null.
        /// </summary>
        public List<string> Evidence
        {
            get
            {
                return _Evidence;
            }
            set
            {
                _Evidence = value ?? new List<string>();
            }
        }

        /// <summary>
        /// Error or blocker detail, or null.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Query executions attempted (0, 1, or 2).
        /// </summary>
        public int Attempts { get; set; } = 0;

        /// <summary>
        /// Model that planned and answered, or null.
        /// </summary>
        public string? Model { get; set; } = null;

        #endregion

        #region Private-Members

        private List<AmbiguitySignal> _Ambiguities = new List<AmbiguitySignal>();
        private List<string> _Assumptions = new List<string>();
        private List<string> _Tables = new List<string>();
        private List<string> _Columns = new List<string>();
        private List<List<string?>> _EvidenceRows = new List<List<string?>>();
        private List<string> _Evidence = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VerifiedAnswer()
        {
        }

        #endregion
    }
}
