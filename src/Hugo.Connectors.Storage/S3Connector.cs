namespace Hugo.Connectors.Storage
{
    using System;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Amazon S3 and S3-compatible object storage (connector type <c>s3</c>, family <c>storage</c>), through Blobject.
    /// <para>
    /// Blobject 6.1.2 limits: it always uses path-style addressing when <c>endpoint</c> is set and virtual-hosted style
    /// otherwise, so there is no <c>forcePathStyle</c> setting; it has no ambient (instance role) credentials, so without
    /// the <c>accessKeyPair</c> secret requests are anonymous (public buckets only) and there is no
    /// <c>useInstanceRole</c> setting; it does not accept session tokens.
    /// </para>
    /// Thread-safe.
    /// </summary>
    public class S3Connector : StorageConnectorBase
    {
        #region Public-Members

        /// <summary>
        /// Connector type.
        /// </summary>
        public const string ConnectorType = "s3";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public S3Connector() : base(StorageProviderEnum.AmazonS3, BuildDescriptor())
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override void ValidateProvider(ConnectorResourceContext resource, ConnectorValidationResult result)
        {
            ArgumentNullException.ThrowIfNull(resource);
            ArgumentNullException.ThrowIfNull(result);
            if (!resource.Settings.Has(StorageSettingKeys.Bucket)) result.Add(StorageSettingKeys.Bucket, "required", "The bucket name is required.");
            ValidateEndpoint(resource, StorageSettingKeys.Endpoint, result);

            if (resource.Secrets.HasSlot(StorageSettingKeys.SlotAccessKeyPair))
            {
                string? id = resource.Secrets.Get(StorageSettingKeys.SlotAccessKeyPair, StorageSettingKeys.FieldAccessKeyId);
                string? secret = resource.Secrets.Get(StorageSettingKeys.SlotAccessKeyPair, StorageSettingKeys.FieldSecretAccessKey);
                if (String.IsNullOrEmpty(id) || String.IsNullOrEmpty(secret))
                    result.Add(StorageSettingKeys.SlotAccessKeyPair, "invalid_secret", "The access key pair must have both accessKeyId and secretAccessKey.");
                if (!String.IsNullOrEmpty(resource.Secrets.Get(StorageSettingKeys.SlotAccessKeyPair, StorageSettingKeys.FieldSessionToken)))
                    result.Add(StorageSettingKeys.SlotAccessKeyPair, "not_supported", "Temporary credentials (sessionToken) are not supported; bind a long-lived access key pair.");
            }
        }

        #endregion

        #region Private-Methods

        private static ConnectorDescriptor BuildDescriptor()
        {
            ConnectorDescriptor d = new ConnectorDescriptor();
            d.Type = ConnectorType;
            d.DisplayName = "Amazon S3";
            d.Description = "Amazon S3 buckets and S3-compatible object stores (MinIO, Less3, Ceph, Wasabi, and others): list, read, summarize, write, copy, delete, and presign objects.";
            d.DocumentationUrl = "https://docs.aws.amazon.com/AmazonS3/latest/userguide/Welcome.html";
            d.DefaultInstructions = ReadInstructions("connector.s3.instructions.md");

            d.SettingsSchema.Add(new SettingDescriptor(StorageSettingKeys.Bucket, SettingValueKindEnum.String, "Bucket")
            {
                Help = "Bucket name.",
                Required = true,
                Pattern = "^[A-Za-z0-9][A-Za-z0-9._-]{1,254}$",
                Group = "Connection",
                ShowInSummary = true
            });
            d.SettingsSchema.Add(new SettingDescriptor(StorageSettingKeys.Region, SettingValueKindEnum.String, "Region")
            {
                Help = "AWS region of the bucket, for example us-east-1. S3-compatible stores usually accept us-east-1. Default us-east-1.",
                DefaultValue = StorageClientFactory.DefaultRegion,
                Pattern = "^[a-z0-9-]{2,32}$",
                Group = "Connection",
                ShowInSummary = true
            });
            d.SettingsSchema.Add(new SettingDescriptor(StorageSettingKeys.Endpoint, SettingValueKindEnum.String, "Endpoint")
            {
                Help = "Endpoint URL of an S3-compatible store, for example http://minio.internal:9000. Leave empty for Amazon S3. When set, path-style addressing is used and the URL scheme decides TLS.",
                Group = "Connection"
            });
            d.SettingsSchema.Add(new SettingDescriptor(StorageSettingKeys.UseSsl, SettingValueKindEnum.Boolean, "Use TLS")
            {
                Help = "Use HTTPS. Applies to Amazon S3 and to an endpoint given without a scheme. Default true.",
                DefaultValue = "true",
                Group = "Connection"
            });

            d.SecretSlots.Add(new SecretSlotDescriptor(StorageSettingKeys.SlotAccessKeyPair, "Access key pair", SecretKindEnum.AccessKeyPair)
            {
                Help = "IAM access key (accessKeyId and secretAccessKey) with the least privilege the bindings need, for example s3:ListBucket and s3:GetObject for read-only. Without it, requests are anonymous and only public buckets work.",
                Required = false
            });
            return d;
        }

        #endregion
    }
}
