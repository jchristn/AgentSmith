namespace Hugo.Server.Services
{
    using System;

    /// <summary>Encrypted, expiring continuation state; authorization is checked independently on every use.</summary>
    public class PromptDocumentCursor
    {
        /// <summary>list or read.</summary>
        public string Kind { get; set; } = String.Empty;
        /// <summary>Document identifier for read.</summary>
        public string DocumentId { get; set; } = String.Empty;
        /// <summary>Selected immutable revision.</summary>
        public int Revision { get; set; }
        /// <summary>Byte position or list position.</summary>
        public int Offset { get; set; }
        /// <summary>Selected revision or manifest fingerprint.</summary>
        public string Hash { get; set; } = String.Empty;
        /// <summary>UTC expiry.</summary>
        public DateTime ExpiresUtc { get; set; }
    }
}
