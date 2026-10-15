namespace Hugo.Core.Connectors
{
    using System;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// One resource a caller may name in a tool's resource argument. Not thread-safe.
    /// </summary>
    public class CallerToolResource
    {
        #region Public-Members

        /// <summary>
        /// Binding alias.
        /// </summary>
        public string Alias
        {
            get
            {
                return _Alias;
            }
            set
            {
                _Alias = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Connector type.
        /// </summary>
        public string ConnectorType
        {
            get
            {
                return _ConnectorType;
            }
            set
            {
                _ConnectorType = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Effective access mode (after the resource ceiling).
        /// </summary>
        public AccessModeEnum AccessMode { get; set; } = AccessModeEnum.ReadOnly;

        /// <summary>
        /// Tool rule on this binding (Allow or RequireApproval), when one is set.
        /// </summary>
        public string? Rule { get; set; } = null;

        #endregion

        #region Private-Members

        private string _Alias = String.Empty;
        private string _ConnectorType = String.Empty;

        #endregion
    }
}
