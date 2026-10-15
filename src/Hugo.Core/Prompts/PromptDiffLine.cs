namespace Hugo.Core.Prompts
{
    using System;

    /// <summary>
    /// One line of a template diff. Not thread-safe.
    /// </summary>
    public class PromptDiffLine
    {
        #region Public-Members

        /// <summary>
        /// Operation: equal, add, or remove.
        /// </summary>
        public string Op
        {
            get
            {
                return _Op;
            }
            set
            {
                _Op = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Line text.
        /// </summary>
        public string Text
        {
            get
            {
                return _Text;
            }
            set
            {
                _Text = value ?? String.Empty;
            }
        }

        #endregion

        #region Private-Members

        private string _Op = String.Empty;
        private string _Text = String.Empty;

        #endregion
    }
}
