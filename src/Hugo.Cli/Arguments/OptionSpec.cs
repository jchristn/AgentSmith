namespace Hugo.Cli.Arguments
{
    using System;

    /// <summary>
    /// One option a command accepts, for example <c>--server URL</c> or the flag <c>--json</c>. Immutable and thread-safe.
    /// </summary>
    public class OptionSpec
    {
        #region Public-Members

        /// <summary>
        /// Option name without the leading dashes, for example <c>server</c>. Never null or empty.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Whether the option takes a value (<c>--server URL</c> or <c>--server=URL</c>). False for flags.
        /// </summary>
        public bool TakesValue { get; }

        /// <summary>
        /// Placeholder shown in help for the value, for example <c>URL</c>. Empty for flags.
        /// </summary>
        public string ValueName { get; }

        /// <summary>
        /// One-line description shown in help.
        /// </summary>
        public string Description { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="name">Option name without dashes.</param>
        /// <param name="takesValue">Whether the option takes a value.</param>
        /// <param name="valueName">Placeholder for the value in help.</param>
        /// <param name="description">Description shown in help.</param>
        /// <exception cref="ArgumentNullException">Thrown when the name is null or empty.</exception>
        public OptionSpec(string name, bool takesValue, string valueName, string description)
        {
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            Name = name;
            TakesValue = takesValue;
            ValueName = valueName ?? String.Empty;
            Description = description ?? String.Empty;
        }

        /// <summary>
        /// An option that takes a value.
        /// </summary>
        /// <param name="name">Option name without dashes.</param>
        /// <param name="valueName">Placeholder for the value in help.</param>
        /// <param name="description">Description shown in help.</param>
        /// <returns>Option.</returns>
        public static OptionSpec Value(string name, string valueName, string description)
        {
            return new OptionSpec(name, true, valueName, description);
        }

        /// <summary>
        /// A flag (no value).
        /// </summary>
        /// <param name="name">Option name without dashes.</param>
        /// <param name="description">Description shown in help.</param>
        /// <returns>Option.</returns>
        public static OptionSpec Flag(string name, string description)
        {
            return new OptionSpec(name, false, String.Empty, description);
        }

        #endregion
    }
}
