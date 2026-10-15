namespace Hugo.Connectors.Ssh.Classification
{
    using System;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Intermediate classification of a command or segment. Treat as immutable after construction.
    /// </summary>
    public class SshVerdict
    {
        #region Public-Members

        /// <summary>
        /// Effect.
        /// </summary>
        public EffectEnum Effect { get; set; } = EffectEnum.Read;

        /// <summary>
        /// Human-readable reason.
        /// </summary>
        public string Reason { get; set; } = String.Empty;

        /// <summary>
        /// Whether the command must be rejected regardless of access mode.
        /// </summary>
        public bool Rejected { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a Read verdict with an empty reason.
        /// </summary>
        public SshVerdict()
        {
        }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="effect">Effect.</param>
        /// <param name="reason">Reason.</param>
        public SshVerdict(EffectEnum effect, string reason)
        {
            Effect = effect;
            Reason = reason ?? String.Empty;
        }

        /// <summary>
        /// Read verdict.
        /// </summary>
        /// <param name="reason">Reason.</param>
        /// <returns>Verdict.</returns>
        public static SshVerdict Read(string reason)
        {
            return new SshVerdict(EffectEnum.Read, reason);
        }

        /// <summary>
        /// Write verdict.
        /// </summary>
        /// <param name="reason">Reason.</param>
        /// <returns>Verdict.</returns>
        public static SshVerdict Write(string reason)
        {
            return new SshVerdict(EffectEnum.Write, reason);
        }

        /// <summary>
        /// Destructive verdict.
        /// </summary>
        /// <param name="reason">Reason.</param>
        /// <returns>Verdict.</returns>
        public static SshVerdict Destructive(string reason)
        {
            return new SshVerdict(EffectEnum.Destructive, reason);
        }

        /// <summary>
        /// Rejected verdict (also Destructive).
        /// </summary>
        /// <param name="reason">Reason.</param>
        /// <returns>Verdict.</returns>
        public static SshVerdict Reject(string reason)
        {
            return new SshVerdict(EffectEnum.Destructive, reason) { Rejected = true };
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Return the more severe of two verdicts. Rejected beats everything; otherwise the higher effect wins and the
        /// first verdict wins ties.
        /// </summary>
        /// <param name="a">First verdict. May be null.</param>
        /// <param name="b">Second verdict. May be null.</param>
        /// <returns>The more severe verdict, or a Read verdict when both are null.</returns>
        public static SshVerdict Max(SshVerdict? a, SshVerdict? b)
        {
            if (a == null) return b ?? Read("no command");
            if (b == null) return a;
            if (a.Rejected) return a;
            if (b.Rejected) return b;
            return b.Effect > a.Effect ? b : a;
        }

        /// <summary>
        /// Copy this verdict with a prefix on the reason.
        /// </summary>
        /// <param name="prefix">Prefix such as <c>sudo: </c>.</param>
        /// <returns>New verdict.</returns>
        public SshVerdict WithPrefix(string prefix)
        {
            return new SshVerdict(Effect, (prefix ?? String.Empty) + Reason) { Rejected = Rejected };
        }

        #endregion
    }
}
