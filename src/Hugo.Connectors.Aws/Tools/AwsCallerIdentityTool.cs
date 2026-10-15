namespace Hugo.Connectors.Aws.Tools
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Aws.Models;

    /// <summary>
    /// <c>aws_caller_identity</c> (Read): sts:GetCallerIdentity. Confirms the account, principal, and region before any
    /// other call, and refreshes the identity used in approval summaries. Always allowed, whatever
    /// <c>allowedServices</c> says. Thread-safe.
    /// </summary>
    public class AwsCallerIdentityTool : AwsToolBase
    {
        #region Public-Members

        /// <summary>
        /// Tool name.
        /// </summary>
        public const string ToolName = "aws_caller_identity";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AwsCallerIdentityTool() : base(BuildDescriptor(), AwsServiceNames.Sts, "sts:GetCallerIdentity")
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, AwsSession session, AwsOptions options, CancellationToken token)
        {
            AwsCallerIdentity identity = await AwsSessionFactory.ResolveIdentityAsync(context.Resource, session, token).ConfigureAwait(false);
            AwsTable table = new AwsTable("AWS caller identity");
            table.Details["account"] = identity.Account;
            table.Details["arn"] = identity.Arn;
            table.Details["userId"] = identity.UserId;
            table.Details["region"] = options.Region;
            table.Details["credentialSource"] = !String.IsNullOrEmpty(options.AssumeRoleArn) ? "assumed role" : (context.Resource.Secrets.HasSlot(AwsSettingKeys.SlotAccessKeyPair) ? "access key pair" : "ambient credential chain");
            if (!String.IsNullOrEmpty(options.EndpointOverride)) table.Details["endpointOverride"] = options.EndpointOverride;
            table.Details["allowedServices"] = options.AllowedServices.Count == 0 ? "all" : String.Join(", ", options.AllowedServices);
            if (session.ExpiresUtc != null) table.Details["credentialsExpire"] = AwsResultBuilder.Time(session.ExpiresUtc);
            return Render(context, session, table);
        }

        #endregion

        #region Private-Methods

        private static ToolDescriptor BuildDescriptor()
        {
            ToolDescriptor d = new ToolDescriptor();
            d.Name = ToolName;
            d.Title = "Confirm AWS account and identity";
            d.Description =
                "Return the AWS account ID, principal ARN, and region this resource's credentials resolve to (sts:GetCallerIdentity), "
                + "plus the services this resource may call. Call it first to confirm you are in the intended account before "
                + "investigating or changing anything.";
            d.InputSchemaJson = "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}";
            d.BaseEffect = EffectEnum.Read;
            d.OutputDescription = "Account, ARN, user ID, region, credential source, and allowed services.";
            return d;
        }

        #endregion
    }
}
