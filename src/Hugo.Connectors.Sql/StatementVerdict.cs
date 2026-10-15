namespace Hugo.Connectors.Sql
{
    using System;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Classification of a single statement, produced inside <see cref="StatementClassifier"/>. Not thread-safe for
    /// mutation.
    /// </summary>
    public class StatementVerdict
    {
        #region Public-Members

        /// <summary>
        /// Effect.
        /// </summary>
        public EffectEnum Effect { get; set; } = EffectEnum.Destructive;

        /// <summary>
        /// Statement type label (for example <c>SELECT</c>).
        /// </summary>
        public string Type { get; set; } = "UNKNOWN";

        /// <summary>
        /// Reason.
        /// </summary>
        public string Reason { get; set; } = String.Empty;

        /// <summary>
        /// Whether the statement was recognized. Unrecognized statements are Destructive.
        /// </summary>
        public bool Recognized { get; set; } = true;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="effect">Effect.</param>
        /// <param name="type">Statement type label.</param>
        /// <param name="reason">Reason.</param>
        public StatementVerdict(EffectEnum effect, string type, string reason)
        {
            Effect = effect;
            Type = type ?? "UNKNOWN";
            Reason = reason ?? String.Empty;
        }

        /// <summary>
        /// Build an unrecognized (Destructive) verdict.
        /// </summary>
        /// <param name="reason">Reason.</param>
        /// <returns>Verdict.</returns>
        public static StatementVerdict Unrecognized(string reason)
        {
            StatementVerdict v = new StatementVerdict(EffectEnum.Destructive, "UNKNOWN", reason);
            v.Recognized = false;
            return v;
        }

        #endregion
    }
}
