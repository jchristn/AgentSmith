namespace Hugo.Connectors.Aws
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Amazon;
    using Amazon.Runtime;
    using Amazon.Runtime.Credentials;
    using Amazon.SecurityToken;
    using Amazon.SecurityToken.Model;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Builds <see cref="AwsSession"/> instances: resolves the access key pair or the ambient credential chain, assumes
    /// the configured role (with external ID) through STS at the resource's endpoint, and applies region, endpoint
    /// override, and retry settings to every client. Plaintext secrets are read from the accessor only while building
    /// credentials and are never logged. Thread-safe.
    /// </summary>
    public static class AwsSessionFactory
    {
        #region Public-Methods

        /// <summary>
        /// Get the pooled session of a resource, replacing it when its assumed role credentials are about to expire.
        /// </summary>
        /// <param name="resource">Resource.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Session; owned by the pool, do not dispose.</returns>
        /// <exception cref="ArgumentNullException">Thrown when resource is null.</exception>
        /// <exception cref="ConnectorException">Thrown when credentials are missing or the role cannot be assumed.</exception>
        public static async Task<AwsSession> GetSessionAsync(ConnectorResourceContext resource, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(resource);
            string key = resource.SessionKey();
            AwsSession session = await resource.Sessions.GetOrCreateAsync(key, ct => CreateAsync(resource, ct), token).ConfigureAwait(false);
            if (!session.IsExpiring) return session;
            resource.Sessions.Invalidate(key);
            return await resource.Sessions.GetOrCreateAsync(key, ct => CreateAsync(resource, ct), token).ConfigureAwait(false);
        }

        /// <summary>
        /// Drop the pooled session of a resource (after authentication failures).
        /// </summary>
        /// <param name="resource">Resource.</param>
        public static void InvalidateSession(ConnectorResourceContext resource)
        {
            if (resource == null) return;
            resource.Sessions.Invalidate(resource.SessionKey());
        }

        /// <summary>
        /// Build a new session. The caller owns and disposes it.
        /// </summary>
        /// <param name="resource">Resource.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Session.</returns>
        /// <exception cref="ArgumentNullException">Thrown when resource is null.</exception>
        /// <exception cref="ConnectorException">Thrown when credentials are missing or the role cannot be assumed.</exception>
        public static async Task<AwsSession> CreateAsync(ConnectorResourceContext resource, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(resource);
            token.ThrowIfCancellationRequested();
            AwsOptions options = AwsOptions.FromSettings(resource.Settings);
            AWSCredentials? source = await ResolveSourceCredentialsAsync(resource, options).ConfigureAwait(false);

            if (String.IsNullOrEmpty(options.AssumeRoleArn)) return new AwsSession(options, source, null);

            AssumeRoleRequest request = new AssumeRoleRequest();
            request.RoleArn = options.AssumeRoleArn;
            request.RoleSessionName = options.SessionName;
            request.DurationSeconds = options.AssumeRoleDurationSeconds;
            if (!String.IsNullOrEmpty(options.ExternalId)) request.ExternalId = options.ExternalId;

            AssumeRoleResponse response;
            AmazonSecurityTokenServiceConfig config = Configure(new AmazonSecurityTokenServiceConfig(), options);
            using (AmazonSecurityTokenServiceClient sts = source == null ? new AmazonSecurityTokenServiceClient(config) : new AmazonSecurityTokenServiceClient(source, config))
            {
                try
                {
                    response = await sts.AssumeRoleAsync(request, token).ConfigureAwait(false);
                }
                catch (Exception e) when (!(e is OperationCanceledException))
                {
                    ConnectorException mapped = AwsErrorMapper.Map(e, "sts:AssumeRole on " + options.AssumeRoleArn);
                    if (mapped.Code == ConnectorErrorCodes.PermissionDenied) throw new ConnectorException(ConnectorErrorCodes.AuthFailed, "Could not assume role " + options.AssumeRoleArn + ": " + mapped.Message, e);
                    throw mapped;
                }
            }

            Credentials? c = response.Credentials;
            if (c == null || String.IsNullOrEmpty(c.AccessKeyId))
                throw new ConnectorException(ConnectorErrorCodes.AuthFailed, "sts:AssumeRole on " + options.AssumeRoleArn + " returned no credentials.");
            DateTime? expires = c.Expiration == null ? null : c.Expiration.Value.ToUniversalTime();
            return new AwsSession(options, new SessionAWSCredentials(c.AccessKeyId, c.SecretAccessKey, c.SessionToken), expires);
        }

        /// <summary>
        /// Resolve and cache the caller identity of a session.
        /// </summary>
        /// <param name="resource">Resource.</param>
        /// <param name="session">Session.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Identity.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static async Task<AwsCallerIdentity> ResolveIdentityAsync(ConnectorResourceContext resource, AwsSession session, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(resource);
            ArgumentNullException.ThrowIfNull(session);
            GetCallerIdentityResponse response = await session.Sts().GetCallerIdentityAsync(new GetCallerIdentityRequest(), token).ConfigureAwait(false);
            AwsCallerIdentity identity = new AwsCallerIdentity();
            identity.Account = response.Account ?? String.Empty;
            identity.Arn = response.Arn ?? String.Empty;
            identity.UserId = response.UserId ?? String.Empty;
            identity.Region = session.Options.Region;
            identity.ResolvedUtc = DateTime.UtcNow;
            session.Identity = identity;
            AwsIdentityCache.Set(resource, identity);
            return identity;
        }

        /// <summary>
        /// Apply region, endpoint override, and retry settings to a client configuration.
        /// </summary>
        /// <typeparam name="T">Configuration type.</typeparam>
        /// <param name="config">Configuration.</param>
        /// <param name="options">Options.</param>
        /// <returns>The same configuration.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        /// <exception cref="ConnectorException">Thrown when the endpoint override is not an absolute HTTP or HTTPS URL.</exception>
        public static T Configure<T>(T config, AwsOptions options) where T : ClientConfig
        {
            ArgumentNullException.ThrowIfNull(config);
            ArgumentNullException.ThrowIfNull(options);
            if (!String.IsNullOrEmpty(options.EndpointOverride))
            {
                Uri endpoint = ParseEndpoint(options.EndpointOverride);
                config.ServiceURL = endpoint.ToString().TrimEnd('/');
                config.AuthenticationRegion = options.Region;
            }
            else
            {
                config.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);
            }
            config.MaxErrorRetry = options.MaxRetries;
            return config;
        }

        /// <summary>
        /// Parse an endpoint override.
        /// </summary>
        /// <param name="value">URL text.</param>
        /// <returns>URI.</returns>
        /// <exception cref="ConnectorException">Thrown when the value is not an absolute HTTP or HTTPS URL.</exception>
        public static Uri ParseEndpoint(string? value)
        {
            if (String.IsNullOrWhiteSpace(value)
                || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                || !String.IsNullOrEmpty(uri.Query)
                || !String.IsNullOrEmpty(uri.UserInfo))
            {
                throw new ConnectorException(ConnectorErrorCodes.InvalidSetting, "endpointOverride must be an absolute http:// or https:// URL without credentials or a query string, for example http://127.0.0.1:4566.");
            }
            return uri;
        }

        #endregion

        #region Private-Methods

        private static async Task<AWSCredentials?> ResolveSourceCredentialsAsync(ConnectorResourceContext resource, AwsOptions options)
        {
            string? id = resource.Secrets.Get(AwsSettingKeys.SlotAccessKeyPair, AwsSettingKeys.FieldAccessKeyId);
            string? secret = resource.Secrets.Get(AwsSettingKeys.SlotAccessKeyPair, AwsSettingKeys.FieldSecretAccessKey);
            string? sessionToken = resource.Secrets.Get(AwsSettingKeys.SlotAccessKeyPair, AwsSettingKeys.FieldSessionToken);

            if (!String.IsNullOrEmpty(id) && !String.IsNullOrEmpty(secret))
            {
                if (!String.IsNullOrEmpty(sessionToken)) return new SessionAWSCredentials(id, secret, sessionToken);
                return new BasicAWSCredentials(id, secret);
            }

            if (options.UseInstanceRole)
            {
                try
                {
                    return await DefaultAWSCredentialsIdentityResolver.GetCredentialsAsync(null).ConfigureAwait(false);
                }
                catch (AmazonClientException e)
                {
                    throw new ConnectorException(ConnectorErrorCodes.AuthFailed, "useInstanceRole is on, but no ambient AWS credentials were found (environment, shared profile, container role, or instance role): " + FirstLine(e.Message), e);
                }
            }

            throw new ConnectorException(ConnectorErrorCodes.SecretMissing, "Bind an AccessKeyPair secret (accessKeyId and secretAccessKey) to the 'accessKeyPair' slot, or turn on useInstanceRole.");
        }

        private static string FirstLine(string? message)
        {
            if (String.IsNullOrEmpty(message)) return String.Empty;
            int newline = message.IndexOfAny(new char[] { '\r', '\n' });
            return newline >= 0 ? message.Substring(0, newline) : message;
        }

        #endregion
    }
}
