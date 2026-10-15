namespace Hugo.Connectors.Logging
{
    using System;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// The <c>apiToken</c> secret slot shared by the logging connectors. Thread-safe.
    /// </summary>
    public static class ApiTokenSlot
    {
        #region Public-Members

        /// <summary>
        /// Slot name.
        /// </summary>
        public static readonly string Name = "apiToken";

        /// <summary>
        /// Field read from the bound <see cref="SecretKindEnum.ApiToken"/> secret.
        /// </summary>
        public static readonly string Field = "token";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the slot descriptor.
        /// </summary>
        /// <param name="help">English help text.</param>
        /// <param name="acceptBearerToken">Whether the service sends the token as Authorization: Bearer, so BearerToken
        /// secrets are accepted too. Default false.</param>
        /// <returns>Descriptor.</returns>
        public static SecretSlotDescriptor Descriptor(string help, bool acceptBearerToken = false)
        {
            SecretSlotDescriptor slot = acceptBearerToken
                ? new SecretSlotDescriptor(Name, "API token", SecretKindEnum.ApiToken, SecretKindEnum.BearerToken)
                : new SecretSlotDescriptor(Name, "API token", SecretKindEnum.ApiToken);
            slot.Required = true;
            slot.Help = help ?? String.Empty;
            return slot;
        }

        /// <summary>
        /// Whether a non-empty token is bound.
        /// </summary>
        /// <param name="resource">Resource.</param>
        /// <returns>True when present.</returns>
        /// <exception cref="ArgumentNullException">Thrown when resource is null.</exception>
        public static bool IsPresent(ConnectorResourceContext resource)
        {
            ArgumentNullException.ThrowIfNull(resource);
            return !String.IsNullOrEmpty(resource.Secrets.Get(Name, Field));
        }

        /// <summary>
        /// Read the token.
        /// </summary>
        /// <param name="resource">Resource.</param>
        /// <returns>Token.</returns>
        /// <exception cref="ArgumentNullException">Thrown when resource is null.</exception>
        /// <exception cref="ConnectorException">Thrown with code <c>secret_missing</c> when no token is bound.</exception>
        public static string Read(ConnectorResourceContext resource)
        {
            ArgumentNullException.ThrowIfNull(resource);
            string? token = resource.Secrets.Get(Name, Field);
            if (String.IsNullOrEmpty(token))
                throw new ConnectorException("secret_missing", "No API token is bound to the apiToken slot of resource '" + resource.Name + "'.");
            return token;
        }

        #endregion
    }
}
