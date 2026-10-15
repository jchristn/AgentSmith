// Ported from Tablix (https://github.com/jchristn/Tablix), commit 450bc764be478740db5bfc9ae9cb3fd4bb9c14c6,
// src/Tablix.Core/Models/DomainIntelligence.cs. Copyright (c) 2026 Joel Christner and Adam Wingate, MIT License.
// Modified for Hugo: namespace, nullable annotations, metric details. See Vendored/VENDORED.md.
namespace Hugo.Connectors.Sql.Intelligence
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Heuristic domain reading of a crawled schema: entities, join paths, metrics, filters, freshness, tenancy, and
    /// soft-delete columns. Column labels are qualified as <c>schema.table.column</c>. Not thread-safe for mutation.
    /// </summary>
    public class DomainIntelligence
    {
        #region Public-Members

        /// <summary>
        /// One-paragraph summary.
        /// </summary>
        public string Summary { get; set; } = String.Empty;

        /// <summary>
        /// Entities, one per table or view. Never null.
        /// </summary>
        public List<DomainEntity> Entities
        {
            get
            {
                return _Entities;
            }
            set
            {
                _Entities = value ?? new List<DomainEntity>();
            }
        }

        /// <summary>
        /// Join paths described in words. Never null.
        /// </summary>
        public List<string> Workflows
        {
            get
            {
                return _Workflows;
            }
            set
            {
                _Workflows = value ?? new List<string>();
            }
        }

        /// <summary>
        /// Likely metric columns. Never null.
        /// </summary>
        public List<string> Metrics
        {
            get
            {
                return _Metrics;
            }
            set
            {
                _Metrics = value ?? new List<string>();
            }
        }

        /// <summary>
        /// Likely metrics with natural aggregations and caveats. Never null.
        /// </summary>
        public List<MetricCandidate> MetricDetails
        {
            get
            {
                return _MetricDetails;
            }
            set
            {
                _MetricDetails = value ?? new List<MetricCandidate>();
            }
        }

        /// <summary>
        /// Common filter columns (status, type, activity, tenancy, soft delete, dates). Never null.
        /// </summary>
        public List<string> CommonFilters
        {
            get
            {
                return _CommonFilters;
            }
            set
            {
                _CommonFilters = value ?? new List<string>();
            }
        }

        /// <summary>
        /// Timestamp columns that show how recent a row is. Never null.
        /// </summary>
        public List<string> FreshnessColumns
        {
            get
            {
                return _FreshnessColumns;
            }
            set
            {
                _FreshnessColumns = value ?? new List<string>();
            }
        }

        /// <summary>
        /// Tenant or account scoping columns. Never null.
        /// </summary>
        public List<string> TenantColumns
        {
            get
            {
                return _TenantColumns;
            }
            set
            {
                _TenantColumns = value ?? new List<string>();
            }
        }

        /// <summary>
        /// Soft-delete columns. Never null.
        /// </summary>
        public List<string> SoftDeleteColumns
        {
            get
            {
                return _SoftDeleteColumns;
            }
            set
            {
                _SoftDeleteColumns = value ?? new List<string>();
            }
        }

        #endregion

        #region Private-Members

        private List<DomainEntity> _Entities = new List<DomainEntity>();
        private List<string> _Workflows = new List<string>();
        private List<string> _Metrics = new List<string>();
        private List<MetricCandidate> _MetricDetails = new List<MetricCandidate>();
        private List<string> _CommonFilters = new List<string>();
        private List<string> _FreshnessColumns = new List<string>();
        private List<string> _TenantColumns = new List<string>();
        private List<string> _SoftDeleteColumns = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public DomainIntelligence()
        {
        }

        #endregion
    }
}
