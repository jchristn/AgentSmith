namespace Hugo.Core.Database.Ado
{
    using System;

    /// <summary>
    /// A secret identifier and one of its field names, read when enumerating secrets.
    /// </summary>
    internal class SecretFieldRow
    {
        #region Public-Members

        /// <summary>
        /// Secret identifier.
        /// </summary>
        public string SecretId { get; }

        /// <summary>
        /// Field name.
        /// </summary>
        public string FieldName { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="secretId">Secret identifier.</param>
        /// <param name="fieldName">Field name.</param>
        public SecretFieldRow(string secretId, string fieldName)
        {
            SecretId = secretId ?? String.Empty;
            FieldName = fieldName ?? String.Empty;
        }

        #endregion
    }
}
