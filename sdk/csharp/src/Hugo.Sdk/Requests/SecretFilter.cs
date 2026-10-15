namespace Hugo.Sdk.Requests
{
    using System;

    /// <summary>
    /// Secret list filter.
    /// </summary>
    public class SecretFilter
    {
        #region Public-Members

        /// <summary>
        /// Secret kind, for example ApiToken or BearerToken.
        /// </summary>
        public string? Kind { get; set; } = null;

        #endregion
    }
}
