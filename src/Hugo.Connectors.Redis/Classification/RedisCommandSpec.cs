namespace Hugo.Connectors.Redis.Classification
{
    using System;
    using System.Collections.Generic;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Classification of one Redis command or subcommand. Immutable after the table is built; thread-safe for reads.
    /// </summary>
    public class RedisCommandSpec
    {
        #region Public-Members

        /// <summary>
        /// Upper-case command name (for example <c>GET</c>) or subcommand name (for example <c>SET</c> under <c>CONFIG</c>).
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Effect of the command.
        /// </summary>
        public EffectEnum Effect { get; }

        /// <summary>
        /// Why the command cannot run through the escape hatch (blocking, connection state, subscriptions), or null when it
        /// can.
        /// </summary>
        public string? UnsupportedReason { get; }

        /// <summary>
        /// Whether the call needs approval even when the binding opted out of destructive approvals (flushes, shutdown).
        /// </summary>
        public bool AlwaysRequireApproval { get; }

        /// <summary>
        /// Subcommands of a container command (for example <c>CONFIG GET</c>), keyed upper case. Empty for plain commands.
        /// </summary>
        public Dictionary<string, RedisCommandSpec> Subcommands { get; } = new Dictionary<string, RedisCommandSpec>(StringComparer.Ordinal);

        /// <summary>
        /// Whether the command is a container with subcommands.
        /// </summary>
        public bool IsContainer
        {
            get
            {
                return Subcommands.Count > 0;
            }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="name">Upper-case name.</param>
        /// <param name="effect">Effect.</param>
        /// <param name="unsupportedReason">Reason the command is unsupported, or null.</param>
        /// <param name="alwaysRequireApproval">Whether approval is always required.</param>
        /// <exception cref="ArgumentNullException">Thrown when name is null or empty.</exception>
        public RedisCommandSpec(string name, EffectEnum effect, string? unsupportedReason = null, bool alwaysRequireApproval = false)
        {
            if (String.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
            Name = name.ToUpperInvariant();
            Effect = effect;
            UnsupportedReason = unsupportedReason;
            AlwaysRequireApproval = alwaysRequireApproval;
        }

        #endregion
    }
}
