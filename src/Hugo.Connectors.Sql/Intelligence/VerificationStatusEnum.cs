namespace Hugo.Connectors.Sql.Intelligence
{
    /// <summary>
    /// Verification state of an answer from <c>sql_ask</c> (Tablix <c>VerifiedAnswer.State</c>).
    /// </summary>
    public enum VerificationStatusEnum
    {
        /// <summary>
        /// The answer was written from the complete result of a read-only query that ran.
        /// </summary>
        Verified = 0,

        /// <summary>
        /// The answer rests on a truncated result, or on schema and context without a query, or the query ran but no
        /// answer text could be produced from it.
        /// </summary>
        Partial = 1,

        /// <summary>
        /// No trustworthy answer: the generated SQL was not a single read, failed after a retry, timed out, or no plan
        /// was produced.
        /// </summary>
        Blocked = 2,

        /// <summary>
        /// The question uses a term the schema can read several ways; a clarification question is returned instead of
        /// guessing.
        /// </summary>
        Ambiguous = 3
    }
}
