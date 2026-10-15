namespace Hugo.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;

    /// <summary>
    /// Token validation result.
    /// </summary>
    public class TokenValidResponse
    {
        /// <summary>
        /// Whether the token is valid.
        /// </summary>
        public bool Valid { get; set; } = false;
    }
}
