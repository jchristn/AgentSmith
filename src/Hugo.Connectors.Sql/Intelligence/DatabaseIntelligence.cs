// Ported from Tablix (https://github.com/jchristn/Tablix), commit 450bc764be478740db5bfc9ae9cb3fd4bb9c14c6,
// src/Tablix.Core/Models/DatabaseIntelligenceResponse.cs. Copyright (c) 2026 Joel Christner and Adam Wingate, MIT
// License. Modified for Hugo: renamed, namespace, nullable annotations, engine and schema instead of the Tablix
// database ID, suggested questions moved here from the agent pack. See Vendored/VENDORED.md.
namespace Hugo.Connectors.Sql.Intelligence
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Whole-schema intelligence derived from the crawl and the durable context. Not thread-safe for mutation.
    /// </summary>
    public class DatabaseIntelligence
    {
        #region Public-Members

        /// <summary>
        /// Engine (for example <c>postgresql</c>).
        /// </summary>
        public string Engine { get; set; } = String.Empty;

        /// <summary>
        /// Database name (file name for SQLite), or null.
        /// </summary>
        public string? DatabaseName { get; set; } = null;

        /// <summary>
        /// Schema the intelligence describes.
        /// </summary>
        public string Schema { get; set; } = String.Empty;

        /// <summary>
        /// UTC time of the crawl the intelligence was derived from.
        /// </summary>
        public DateTime CrawledUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Domain reading. Never null.
        /// </summary>
        public DomainIntelligence Domain
        {
            get
            {
                return _Domain;
            }
            set
            {
                _Domain = value ?? new DomainIntelligence();
            }
        }

        /// <summary>
        /// Declared and inferred relationships. Never null.
        /// </summary>
        public List<RelationshipDetail> Relationships
        {
            get
            {
                return _Relationships;
            }
            set
            {
                _Relationships = value ?? new List<RelationshipDetail>();
            }
        }

        /// <summary>
        /// Terms agents should clarify before running SQL. Never null.
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
        /// Context quality score and gaps. Never null.
        /// </summary>
        public ContextQualityScore ContextQuality
        {
            get
            {
                return _ContextQuality;
            }
            set
            {
                _ContextQuality = value ?? new ContextQualityScore();
            }
        }

        /// <summary>
        /// Starter questions the schema can answer. Never null.
        /// </summary>
        public List<string> SuggestedQuestions
        {
            get
            {
                return _SuggestedQuestions;
            }
            set
            {
                _SuggestedQuestions = value ?? new List<string>();
            }
        }

        #endregion

        #region Private-Members

        private DomainIntelligence _Domain = new DomainIntelligence();
        private List<RelationshipDetail> _Relationships = new List<RelationshipDetail>();
        private List<AmbiguitySignal> _Ambiguities = new List<AmbiguitySignal>();
        private ContextQualityScore _ContextQuality = new ContextQualityScore();
        private List<string> _SuggestedQuestions = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public DatabaseIntelligence()
        {
        }

        #endregion
    }
}
