// Ported from Tablix (https://github.com/jchristn/Tablix), commit 450bc764be478740db5bfc9ae9cb3fd4bb9c14c6,
// src/Tablix.Core/Models/AgentPackResponse.cs. Copyright (c) 2026 Joel Christner and Adam Wingate, MIT License.
// Modified for Hugo: renamed, namespace, nullable annotations; carries the intelligence, relationships, and context
// summaries it was built from, bounded by limits, with counts of what was omitted. See Vendored/VENDORED.md.
namespace Hugo.Connectors.Sql.Intelligence
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One briefing for an agent starting work on a database: intelligence, relationships, database context, and table
    /// context summaries, rendered as Markdown and bounded by limits. Not thread-safe for mutation.
    /// </summary>
    public class AgentPack
    {
        #region Public-Members

        /// <summary>
        /// Engine.
        /// </summary>
        public string Engine { get; set; } = String.Empty;

        /// <summary>
        /// Database name, or null.
        /// </summary>
        public string? DatabaseName { get; set; } = null;

        /// <summary>
        /// Schema.
        /// </summary>
        public string Schema { get; set; } = String.Empty;

        /// <summary>
        /// Generation time, UTC.
        /// </summary>
        public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Context quality score. Never null.
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
        /// Database context (possibly shortened), or null when none is saved.
        /// </summary>
        public string? DatabaseContext { get; set; } = null;

        /// <summary>
        /// Who produced the database context, or null.
        /// </summary>
        public string? DatabaseContextSource { get; set; } = null;

        /// <summary>
        /// Domain intelligence with entities limited to the pack's entity cap. Never null.
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
        /// Relationships limited to the pack's relationship cap, declared first. Never null.
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
        /// Ambiguity signals. Never null.
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
        /// Table context summaries limited to the pack's cap. Never null.
        /// </summary>
        public List<TableContextSummary> TableContexts
        {
            get
            {
                return _TableContexts;
            }
            set
            {
                _TableContexts = value ?? new List<TableContextSummary>();
            }
        }

        /// <summary>
        /// Workflow instructions for the agent. Never null.
        /// </summary>
        public List<string> Instructions
        {
            get
            {
                return _Instructions;
            }
            set
            {
                _Instructions = value ?? new List<string>();
            }
        }

        /// <summary>
        /// Starter questions. Never null.
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

        /// <summary>
        /// Entities left out by the cap.
        /// </summary>
        public int OmittedEntities { get; set; } = 0;

        /// <summary>
        /// Relationships left out by the cap.
        /// </summary>
        public int OmittedRelationships { get; set; } = 0;

        /// <summary>
        /// Table contexts left out by the cap.
        /// </summary>
        public int OmittedTableContexts { get; set; } = 0;

        /// <summary>
        /// The briefing as Markdown.
        /// </summary>
        public string Markdown { get; set; } = String.Empty;

        #endregion

        #region Private-Members

        private ContextQualityScore _ContextQuality = new ContextQualityScore();
        private DomainIntelligence _Domain = new DomainIntelligence();
        private List<RelationshipDetail> _Relationships = new List<RelationshipDetail>();
        private List<AmbiguitySignal> _Ambiguities = new List<AmbiguitySignal>();
        private List<TableContextSummary> _TableContexts = new List<TableContextSummary>();
        private List<string> _Instructions = new List<string>();
        private List<string> _SuggestedQuestions = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AgentPack()
        {
        }

        #endregion
    }
}
