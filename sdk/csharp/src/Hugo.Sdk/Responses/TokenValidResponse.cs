namespace Hugo.Sdk.Responses
{
    /// <summary>
    /// Token validity.
    /// </summary>
    public class TokenValidResponse
    {
        #region Public-Members

        /// <summary>
        /// Whether the token is valid.
        /// </summary>
        public bool Valid { get; set; } = false;

        #endregion
    }
}
