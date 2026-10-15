namespace Hugo.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;

    /// <summary>
    /// Bulk delete result.
    /// </summary>
    public class DeleteCountResponse
    {
        /// <summary>
        /// Records deleted.
        /// </summary>
        public long DeletedCount { get; set; } = 0;
    }
}
