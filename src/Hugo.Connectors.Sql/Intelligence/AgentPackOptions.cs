namespace Hugo.Connectors.Sql.Intelligence
{
    using System;

    /// <summary>
    /// Caps that keep an agent pack small enough for a model's context. Not thread-safe for mutation.
    /// </summary>
    public class AgentPackOptions
    {
        #region Public-Members

        /// <summary>
        /// Maximum entities listed. Minimum 1, maximum 1000, default 25.
        /// </summary>
        public int MaxEntities
        {
            get
            {
                return _MaxEntities;
            }
            set
            {
                _MaxEntities = Math.Clamp(value, 1, 1000);
            }
        }

        /// <summary>
        /// Maximum relationships listed (declared first, then inferred by confidence). Minimum 1, maximum 1000,
        /// default 40.
        /// </summary>
        public int MaxRelationships
        {
            get
            {
                return _MaxRelationships;
            }
            set
            {
                _MaxRelationships = Math.Clamp(value, 1, 1000);
            }
        }

        /// <summary>
        /// Maximum table contexts summarized. Minimum 0, maximum 1000, default 25.
        /// </summary>
        public int MaxTableContexts
        {
            get
            {
                return _MaxTableContexts;
            }
            set
            {
                _MaxTableContexts = Math.Clamp(value, 0, 1000);
            }
        }

        /// <summary>
        /// Maximum characters of each table context. Minimum 80, maximum 20000, default 400.
        /// </summary>
        public int TableContextChars
        {
            get
            {
                return _TableContextChars;
            }
            set
            {
                _TableContextChars = Math.Clamp(value, 80, 20000);
            }
        }

        /// <summary>
        /// Maximum characters of the database context. Minimum 200, maximum 100000, default 4000.
        /// </summary>
        public int DatabaseContextChars
        {
            get
            {
                return _DatabaseContextChars;
            }
            set
            {
                _DatabaseContextChars = Math.Clamp(value, 200, 100000);
            }
        }

        /// <summary>
        /// Row-limiting syntax of the engine, shown in the instructions (for example <c>LIMIT n</c> or
        /// <c>TOP (n)</c>). Default <c>LIMIT n</c>.
        /// </summary>
        public string LimitSyntax { get; set; } = "LIMIT n";

        /// <summary>
        /// Engine display name (for example <c>PostgreSQL</c>). Default empty.
        /// </summary>
        public string EngineDisplayName { get; set; } = String.Empty;

        #endregion

        #region Private-Members

        private int _MaxEntities = 25;
        private int _MaxRelationships = 40;
        private int _MaxTableContexts = 25;
        private int _TableContextChars = 400;
        private int _DatabaseContextChars = 4000;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public AgentPackOptions()
        {
        }

        #endregion
    }
}
