namespace Hugo.Core.Responses
{
    using System;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// A connector tool as returned by the connectors routes. Not thread-safe.
    /// </summary>
    public class ConnectorToolView
    {
        #region Public-Members

        /// <summary>
        /// Name.
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
        /// Title.
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
        /// Description.
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
        /// Input schema JSON (without the resource argument).
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
        /// Whether each call is classified.
        /// </summary>
        public bool ClassifiedPerCall { get; set; } = false;

        /// <summary>
        /// Whether it writes resource knowledge.
        /// </summary>
        public bool UpdatesKnowledge { get; set; } = false;

        /// <summary>
        /// Whether it needs the profile's model.
        /// </summary>
        public bool RequiresModel { get; set; } = false;

        /// <summary>
        /// Output description.
        /// </summary>
        public string OutputDescription
        {
            get
            {
                return _OutputDescription;
            }
            set
            {
                _OutputDescription = value ?? String.Empty;
            }
        }

        #endregion

        #region Private-Members

        private string _Name = String.Empty;
        private string _Title = String.Empty;
        private string _Description = String.Empty;
        private string _InputSchemaJson = String.Empty;
        private string _OutputDescription = String.Empty;

        #endregion
    }
}
