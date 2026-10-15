namespace Hugo.Connectors.Aws
{
    using System;

    /// <summary>
    /// Result of sts:GetCallerIdentity: the account, principal ARN, and user ID the resource's credentials resolve to.
    /// </summary>
    public class AwsCallerIdentity
    {
        /// <summary>
        /// 12-digit account ID.
        /// </summary>
        public string Account { get; set; } = String.Empty;

        /// <summary>
        /// Principal ARN.
        /// </summary>
        public string Arn { get; set; } = String.Empty;

        /// <summary>
        /// Unique principal ID.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Region the session uses.
        /// </summary>
        public string Region { get; set; } = String.Empty;

        /// <summary>
        /// When the identity was resolved (UTC).
        /// </summary>
        public DateTime ResolvedUtc { get; set; } = DateTime.UtcNow;
    }
}
