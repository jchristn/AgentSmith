namespace Hugo.Connectors.Aws.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One AWS describe filter passed as a tool argument, for example
    /// <c>{"name": "tag:env", "values": ["prod"]}</c>.
    /// </summary>
    public class AwsFilterArgument
    {
        #region Public-Members

        /// <summary>
        /// Filter name, for example <c>instance-state-name</c> or <c>tag:env</c>.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Values; a resource matches when any value matches.
        /// </summary>
        public List<string> Values
        {
            get
            {
                return _Values;
            }
            set
            {
                _Values = value ?? new List<string>();
            }
        }

        #endregion

        #region Private-Members

        private List<string> _Values = new List<string>();

        #endregion
    }
}
