namespace Hugo.Connectors.Observability
{
    using System;
    using System.Collections.Generic;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Settings of an <c>elasticsearch</c> resource. Immutable after construction and thread-safe.
    /// </summary>
    public class ElasticsearchSettings
    {
        #region Public-Members

        /// <summary>
        /// Connection settings.
        /// </summary>
        public ObservabilityEndpoint Endpoint { get; }

        /// <summary>
        /// Flavor. Default <see cref="ElasticsearchFlavorEnum.Elasticsearch"/>.
        /// </summary>
        public ElasticsearchFlavorEnum Flavor { get; }

        /// <summary>
        /// Most hits one search returns. Minimum 1, maximum 10000, default 100.
        /// </summary>
        public int MaxHits { get; }

        /// <summary>
        /// Index globs calls may address. Empty means every index whose name does not start with a dot.
        /// </summary>
        public List<string> AllowedIndexPatterns { get; }

        /// <summary>
        /// Default maximum hits.
        /// </summary>
        public static readonly int DefaultMaxHits = 100;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Parse from resource settings.
        /// </summary>
        /// <param name="settings">Settings.</param>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        /// <exception cref="ConnectorException">Thrown with <c>invalid_setting</c> for an invalid baseUrl, authType, or flavor.</exception>
        public ElasticsearchSettings(ResourceSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            Endpoint = new ObservabilityEndpoint(settings);
            string flavor = settings.GetString(ObservabilitySettingKeys.Flavor, "Elasticsearch") ?? "Elasticsearch";
            if (!Enum.TryParse(flavor, true, out ElasticsearchFlavorEnum parsed) || !Enum.IsDefined(typeof(ElasticsearchFlavorEnum), parsed))
                throw new ConnectorException(ConnectorErrorCodes.InvalidSetting, "Setting 'flavor' must be Elasticsearch or OpenSearch.");
            Flavor = parsed;
            MaxHits = settings.GetInt(ObservabilitySettingKeys.MaxHits, DefaultMaxHits, 1, 10000);
            AllowedIndexPatterns = settings.GetStringList(ObservabilitySettingKeys.AllowedIndexPatterns);
        }

        #endregion
    }
}
