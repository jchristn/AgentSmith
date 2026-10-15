// Ported from Tablix (https://github.com/jchristn/Tablix), commit 450bc764be478740db5bfc9ae9cb3fd4bb9c14c6,
// src/Tablix.Core/Models/RelationshipDetail.cs. Copyright (c) 2026 Joel Christner and Adam Wingate, MIT License.
// Modified for Hugo: namespace, nullable annotations, evidence list. See Vendored/VENDORED.md.
namespace Hugo.Connectors.Sql.Intelligence
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A relationship edge between two tables: a declared foreign key, or a join candidate inferred from naming, with
    /// a confidence score and the evidence behind it. Not thread-safe for mutation.
    /// </summary>
    public class RelationshipDetail
    {
        #region Public-Members

        /// <summary>
        /// Source value for declared foreign keys.
        /// </summary>
        public const string DeclaredSource = "declared_fk";

        /// <summary>
        /// Source value for candidates inferred from column and table names.
        /// </summary>
        public const string InferredSource = "inferred_name_match";

        /// <summary>
        /// Referencing schema.
        /// </summary>
        public string FromSchema { get; set; } = String.Empty;

        /// <summary>
        /// Referencing table or view.
        /// </summary>
        public string FromTable { get; set; } = String.Empty;

        /// <summary>
        /// Referencing column.
        /// </summary>
        public string FromColumn { get; set; } = String.Empty;

        /// <summary>
        /// Referenced schema, or null when unknown.
        /// </summary>
        public string? ToSchema { get; set; } = null;

        /// <summary>
        /// Referenced table.
        /// </summary>
        public string ToTable { get; set; } = String.Empty;

        /// <summary>
        /// Referenced column, or null when the engine did not report it and no key column could be resolved.
        /// </summary>
        public string? ToColumn { get; set; } = null;

        /// <summary>
        /// Constraint name for declared keys; null for inferred candidates.
        /// </summary>
        public string? ConstraintName { get; set; } = null;

        /// <summary>
        /// <see cref="DeclaredSource"/> or <see cref="InferredSource"/>.
        /// </summary>
        public string Source { get; set; } = DeclaredSource;

        /// <summary>
        /// Confidence from 0.0 to 1.0. Declared foreign keys are 1.0; inferred candidates are at most 0.95.
        /// </summary>
        public double Confidence { get; set; } = 1.0;

        /// <summary>
        /// Short statements explaining why the edge exists (for example "column customer_id names table customers").
        /// Never null.
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
        /// Whether the edge is a declared foreign key.
        /// </summary>
        public bool IsDeclared
        {
            get
            {
                return String.Equals(Source, DeclaredSource, StringComparison.Ordinal);
            }
        }

        #endregion

        #region Private-Members

        private List<string> _Evidence = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public RelationshipDetail()
        {
        }

        #endregion
    }
}
