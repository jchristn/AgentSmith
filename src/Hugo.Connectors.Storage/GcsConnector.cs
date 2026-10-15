namespace Hugo.Connectors.Storage
{
    using System;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Google Cloud Storage (connector type <c>gcs</c>, family <c>storage</c>), through Blobject. Authenticates with a
    /// service account key. Blobject 6.1.2 downloads whole objects for reads and copies on this provider, so reads are
    /// refused for very large objects. Thread-safe.
    /// </summary>
    public class GcsConnector : StorageConnectorBase
    {
        #region Public-Members

        /// <summary>
        /// Connector type.
        /// </summary>
        public const string ConnectorType = "gcs";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public GcsConnector() : base(StorageProviderEnum.GoogleCloud, BuildDescriptor())
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override void ValidateProvider(ConnectorResourceContext resource, ConnectorValidationResult result)
        {
            ArgumentNullException.ThrowIfNull(resource);
            ArgumentNullException.ThrowIfNull(result);
            if (!resource.Settings.Has(StorageSettingKeys.ProjectId)) result.Add(StorageSettingKeys.ProjectId, "required", "The project ID is required.");
            if (!resource.Settings.Has(StorageSettingKeys.Bucket)) result.Add(StorageSettingKeys.Bucket, "required", "The bucket name is required.");
            ValidateEndpoint(resource, StorageSettingKeys.CustomEndpoint, result);

            string? json = resource.Secrets.Get(StorageSettingKeys.SlotServiceAccountJson, StorageSettingKeys.FieldJson);
            if (String.IsNullOrEmpty(json))
            {
                result.Add(StorageSettingKeys.SlotServiceAccountJson, "required", "Bind the serviceAccountJson secret (a service account key file).");
                return;
            }

            GcsServiceAccountKey? key = GcsServiceAccountKey.TryParse(json);
            if (key == null)
                result.Add(StorageSettingKeys.SlotServiceAccountJson, "invalid_format", "The service account key is not valid JSON.");
            else if (!String.Equals(key.Type, "service_account", StringComparison.Ordinal)
                || String.IsNullOrEmpty(key.ClientEmail)
                || String.IsNullOrEmpty(key.PrivateKey))
                result.Add(StorageSettingKeys.SlotServiceAccountJson, "invalid_format", "The key must be a service account key with type service_account, client_email, and private_key.");
        }

        #endregion

        #region Private-Methods

        private static ConnectorDescriptor BuildDescriptor()
        {
            ConnectorDescriptor d = new ConnectorDescriptor();
            d.Type = ConnectorType;
            d.DisplayName = "Google Cloud Storage";
            d.Description = "Google Cloud Storage buckets (and fake-gcs-server): list, read, summarize, write, copy, delete, and create signed URLs for objects.";
            d.DocumentationUrl = "https://cloud.google.com/storage/docs";
            d.DefaultInstructions = ReadInstructions("connector.gcs.instructions.md");

            d.SettingsSchema.Add(new SettingDescriptor(StorageSettingKeys.ProjectId, SettingValueKindEnum.String, "Project ID")
            {
                Help = "Google Cloud project ID that owns the bucket.",
                Required = true,
                Pattern = "^[a-z][a-z0-9-]{4,61}[a-z0-9]$",
                Group = "Connection",
                ShowInSummary = true
            });
            d.SettingsSchema.Add(new SettingDescriptor(StorageSettingKeys.Bucket, SettingValueKindEnum.String, "Bucket")
            {
                Help = "Bucket name.",
                Required = true,
                Pattern = "^[a-z0-9][a-z0-9._-]{1,220}[a-z0-9]$",
                Group = "Connection",
                ShowInSummary = true
            });
            d.SettingsSchema.Add(new SettingDescriptor(StorageSettingKeys.CustomEndpoint, SettingValueKindEnum.String, "Custom endpoint")
            {
                Help = "JSON API endpoint for emulators such as fake-gcs-server, for example http://127.0.0.1:4443. '/storage/v1/' is appended when missing. Leave empty for Google Cloud Storage.",
                Group = "Connection"
            });

            d.SecretSlots.Add(new SecretSlotDescriptor(StorageSettingKeys.SlotServiceAccountJson, "Service account key", SecretKindEnum.ServiceAccountJson)
            {
                Help = "Service account key JSON (the json field). Grant the account only the roles the bindings need, for example roles/storage.objectViewer for read-only.",
                Required = true
            });
            return d;
        }

        #endregion
    }
}
