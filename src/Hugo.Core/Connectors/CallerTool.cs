namespace Hugo.Core.Connectors
{
    using System;
    using System.Collections.Generic;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// A tool as one caller of one profile sees it (D-017, plan section 11.3): the family tool with a resource argument
    /// whose enum lists the bindings it may use. Built by <see cref="ToolVisibilityFilter"/>. Not thread-safe.
    /// </summary>
    public class CallerTool
    {
        #region Public-Members

        /// <summary>
        /// Tool name (family_operation).
        /// </summary>
        public string Name
        {
            get
            {
                return _Name;
            }
            set
            {
                _Name = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Connector family.
        /// </summary>
        public string Family
        {
            get
            {
                return _Family;
            }
            set
            {
                _Family = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Short title.
        /// </summary>
        public string Title
        {
            get
            {
                return _Title;
            }
            set
            {
                _Title = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Description for the LLM, including the resources and their access modes.
        /// </summary>
        public string Description
        {
            get
            {
                return _Description;
            }
            set
            {
                _Description = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Input schema JSON with the resource argument added (required, enum of aliases).
        /// </summary>
        public string InputSchemaJson
        {
            get
            {
                return _InputSchemaJson;
            }
            set
            {
                _InputSchemaJson = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Base effect.
        /// </summary>
        public EffectEnum BaseEffect { get; set; } = EffectEnum.Read;

        /// <summary>
        /// Whether each call is classified (C tools such as sql_query and ssh_exec).
        /// </summary>
        public bool ClassifiedPerCall { get; set; } = false;

        /// <summary>
        /// Whether the tool writes resource knowledge.
        /// </summary>
        public bool UpdatesKnowledge { get; set; } = false;

        /// <summary>
        /// Whether the tool needs the profile's inference endpoint.
        /// </summary>
        public bool RequiresModel { get; set; } = false;

        /// <summary>
        /// Read when every call is a read (base Read and not classified per call); null otherwise.
        /// </summary>
        public EffectEnum? KnownEffect { get; set; } = null;

        /// <summary>
        /// Whether this is a Hugo meta tool (hugo_*) rather than a connector tool.
        /// </summary>
        public bool IsMetaTool { get; set; } = false;

        /// <summary>
        /// Resources the caller may name.
        /// </summary>
        public List<CallerToolResource> Resources
        {
            get
            {
                return _Resources;
            }
            set
            {
                _Resources = value ?? new List<CallerToolResource>();
            }
        }

        #endregion

        #region Private-Members

        private string _Name = String.Empty;
        private string _Family = String.Empty;
        private string _Title = String.Empty;
        private string _Description = String.Empty;
        private string _InputSchemaJson = String.Empty;
        private List<CallerToolResource> _Resources = new List<CallerToolResource>();

        #endregion
    }
}
