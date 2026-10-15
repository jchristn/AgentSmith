namespace Hugo.Connectors.Storage
{
    using System;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Azure Blob Storage (connector type <c>azure-blob</c>, family <c>storage</c>), through Blobject. Authenticates with
    /// the storage account key (<c>accessKey</c> secret plus <c>accountName</c>) or a connection string
    /// (<c>connectionString</c> secret); exactly one must be bound. Thread-safe.
    /// </summary>
    public class AzureBlobConnector : StorageConnectorBase
    {
        #region Public-Members

        /// <summary>
        /// Connector type.
        /// </summary>
        public const string ConnectorType = "azure-blob";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AzureBlobConnector() : base(StorageProviderEnum.AzureBlob, BuildDescriptor())
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override void ValidateProvider(ConnectorResourceContext resource, ConnectorValidationResult result)
        {
            ArgumentNullException.ThrowIfNull(resource);
            ArgumentNullException.ThrowIfNull(result);
            if (!resource.Settings.Has(StorageSettingKeys.Container)) result.Add(StorageSettingKeys.Container, "required", "The container name is required.");
            ValidateEndpoint(resource, StorageSettingKeys.Endpoint, result);

            bool hasKey = !String.IsNullOrEmpty(resource.Secrets.Get(StorageSettingKeys.SlotAccessKey, StorageSettingKeys.FieldToken));
            bool hasConnectionString = !String.IsNullOrEmpty(resource.Secrets.Get(StorageSettingKeys.SlotConnectionString, StorageSettingKeys.FieldConnectionString));
            if (hasKey && hasConnectionString)
                result.Add(StorageSettingKeys.SlotConnectionString, "conflict", "Bind either accessKey or connectionString, not both.");
            else if (!hasKey && !hasConnectionString)
                result.Add(StorageSettingKeys.SlotAccessKey, "required", "Bind the accessKey secret (with accountName) or the connectionString secret.");

            if (hasKey && !resource.Settings.Has(StorageSettingKeys.AccountName))
                result.Add(StorageSettingKeys.AccountName, "required", "The account name is required with the accessKey secret.");
            if (hasKey)
            {
                string key = resource.Secrets.Get(StorageSettingKeys.SlotAccessKey, StorageSettingKeys.FieldToken) ?? String.Empty;
                Span<byte> buffer = new byte[key.Length];
                if (!Convert.TryFromBase64String(key, buffer, out int written) || written == 0)
                    result.Add(StorageSettingKeys.SlotAccessKey, "invalid_format", "The account key must be base64, as shown in the Azure portal under Access keys.");
            }
        }

        #endregion

        #region Private-Methods

        private static ConnectorDescriptor BuildDescriptor()
        {
            ConnectorDescriptor d = new ConnectorDescriptor();
            d.Type = ConnectorType;
            d.DisplayName = "Azure Blob Storage";
            d.Description = "Azure Blob Storage containers (and Azurite): list, read, summarize, write, copy, delete, and create SAS URLs for blobs.";
            d.DocumentationUrl = "https://learn.microsoft.com/azure/storage/blobs/storage-blobs-introduction";
            d.DefaultInstructions = ReadInstructions("connector.azure-blob.instructions.md");

            d.SettingsSchema.Add(new SettingDescriptor(StorageSettingKeys.AccountName, SettingValueKindEnum.String, "Account name")
            {
                Help = "Storage account name. Required with the accessKey secret; ignored with a connection string.",
                Pattern = "^[a-z0-9]{3,24}$",
                Group = "Connection",
                ShowInSummary = true
            });
            d.SettingsSchema.Add(new SettingDescriptor(StorageSettingKeys.Container, SettingValueKindEnum.String, "Container")
            {
                Help = "Container name (3 to 63 lowercase letters, digits, and hyphens).",
                Required = true,
                Pattern = "^[a-z0-9][a-z0-9-]{1,61}[a-z0-9]$",
                Group = "Connection",
                ShowInSummary = true
            });
            d.SettingsSchema.Add(new SettingDescriptor(StorageSettingKeys.Endpoint, SettingValueKindEnum.String, "Blob endpoint")
            {
                Help = "Blob service endpoint. Default https://{accountName}.blob.core.windows.net. For Azurite use http://127.0.0.1:10000/devstoreaccount1. Ignored with a connection string.",
                Group = "Connection"
            });

            d.SecretSlots.Add(new SecretSlotDescriptor(StorageSettingKeys.SlotAccessKey, "Account key", SecretKindEnum.ApiToken)
            {
                Help = "Storage account key (the token field). Account keys grant full access to the account; prefer a dedicated account for agents.",
                Required = false
            });
            d.SecretSlots.Add(new SecretSlotDescriptor(StorageSettingKeys.SlotConnectionString, "Connection string", SecretKindEnum.ConnectionString)
            {
                Help = "Storage connection string. Use instead of accountName plus accessKey. Presigned URLs need a connection string that includes AccountKey.",
                Required = false
            });
            return d;
        }

        #endregion
    }
}
