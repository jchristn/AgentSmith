namespace Hugo.Server.Settings
{
    using System;

    /// <summary>
    /// Instruction compilation settings (plan section 10.4).
    /// </summary>
    public class InstructionsSettings
    {
        #region Public-Members

        /// <summary>
        /// Characters of a resource's root knowledge entry (for SQL, the database context) included in its section of the
        /// compiled instructions (P5.21). Default 2000, minimum 0 (off), maximum 100000.
        /// </summary>
        public int MaxKnowledgeCharsPerResource
        {
            get
            {
                return _MaxKnowledgeCharsPerResource;
            }
            set
            {
                _MaxKnowledgeCharsPerResource = Math.Clamp(value, 0, 100000);
            }
        }

        /// <summary>
        /// Compiled instruction cache entries. Default 1024, minimum 0 (no cache), maximum 100000.
        /// </summary>
        public int CacheEntries
        {
            get
            {
                return _CacheEntries;
            }
            set
            {
                _CacheEntries = Math.Clamp(value, 0, 100000);
            }
        }

        #endregion

        #region Private-Members

        private int _MaxKnowledgeCharsPerResource = 2000;
        private int _CacheEntries = 1024;

        #endregion
    }
}
