namespace Hugo.Core.Requests
{
    using System;

    /// <summary>
    /// Save template content at the Global or Tenant layer.
    /// </summary>
    public class PromptLayerRequest
    {
        #region Public-Members

        /// <summary>
        /// Template content.
        /// </summary>
        public string Content
        {
            get
            {
                return _Content;
            }
            set
            {
                _Content = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Change note recorded on the version.
        /// </summary>
        public string? ChangeNote { get; set; } = null;

        #endregion

        #region Private-Members

        private string _Content = String.Empty;

        #endregion
    }
}
