namespace Hugo.Connectors.Aws
{
    using System;
    using System.Collections.Generic;
    using Amazon;
    using Amazon.AutoScaling;
    using Amazon.CloudWatch;
    using Amazon.CloudWatchLogs;
    using Amazon.EC2;
    using Amazon.ECS;
    using Amazon.ElasticLoadBalancingV2;
    using Amazon.Lambda;
    using Amazon.RDS;
    using Amazon.Route53;
    using Amazon.Runtime;
    using Amazon.SecurityToken;
    using Amazon.SimpleNotificationService;
    using Amazon.SQS;

    /// <summary>
    /// Resolved credentials plus lazily created service clients for one resource configuration. Pooled through
    /// <see cref="Hugo.Connectors.Abstractions.IConnectorSessionPool"/>; assumed role sessions report
    /// <see cref="IsExpiring"/> so the tool base can replace them before the temporary credentials lapse. Thread-safe.
    /// </summary>
    public class AwsSession : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Options the session was built from.
        /// </summary>
        public AwsOptions Options { get; }

        /// <summary>
        /// When the credentials expire (UTC), or null for long-lived credentials.
        /// </summary>
        public DateTime? ExpiresUtc { get; }

        /// <summary>
        /// Margin before <see cref="ExpiresUtc"/> at which the session counts as expiring. Default 5 minutes, minimum 0,
        /// maximum 1 hour.
        /// </summary>
        public TimeSpan ExpiryMargin
        {
            get
            {
                return _ExpiryMargin;
            }
            set
            {
                if (value < TimeSpan.Zero) value = TimeSpan.Zero;
                if (value > TimeSpan.FromHours(1)) value = TimeSpan.FromHours(1);
                _ExpiryMargin = value;
            }
        }

        /// <summary>
        /// Whether the credentials expire within <see cref="ExpiryMargin"/>.
        /// </summary>
        public bool IsExpiring
        {
            get
            {
                return ExpiresUtc != null && DateTime.UtcNow + _ExpiryMargin >= ExpiresUtc.Value;
            }
        }

        /// <summary>
        /// Caller identity resolved for this session, or null.
        /// </summary>
        public AwsCallerIdentity? Identity { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly AWSCredentials? _Credentials;
        private readonly Dictionary<System.Type, IDisposable> _Clients = new Dictionary<System.Type, IDisposable>();
        private readonly object _Lock = new object();
        private TimeSpan _ExpiryMargin = TimeSpan.FromMinutes(5);
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="options">Options.</param>
        /// <param name="credentials">Credentials, or null to let the SDK resolve its default chain.</param>
        /// <param name="expiresUtc">Credential expiry, or null.</param>
        /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
        public AwsSession(AwsOptions options, AWSCredentials? credentials, DateTime? expiresUtc)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
            _Credentials = credentials;
            ExpiresUtc = expiresUtc;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// STS client.
        /// </summary>
        /// <returns>Client.</returns>
        public IAmazonSecurityTokenService Sts()
        {
            return Get(() => _Credentials == null
                ? new AmazonSecurityTokenServiceClient(Configure(new AmazonSecurityTokenServiceConfig()))
                : new AmazonSecurityTokenServiceClient(_Credentials, Configure(new AmazonSecurityTokenServiceConfig())));
        }

        /// <summary>
        /// EC2 client.
        /// </summary>
        /// <returns>Client.</returns>
        public IAmazonEC2 Ec2()
        {
            return Get(() => _Credentials == null
                ? new AmazonEC2Client(Configure(new AmazonEC2Config()))
                : new AmazonEC2Client(_Credentials, Configure(new AmazonEC2Config())));
        }

        /// <summary>
        /// Auto Scaling client.
        /// </summary>
        /// <returns>Client.</returns>
        public IAmazonAutoScaling AutoScaling()
        {
            return Get(() => _Credentials == null
                ? new AmazonAutoScalingClient(Configure(new AmazonAutoScalingConfig()))
                : new AmazonAutoScalingClient(_Credentials, Configure(new AmazonAutoScalingConfig())));
        }

        /// <summary>
        /// CloudWatch client.
        /// </summary>
        /// <returns>Client.</returns>
        public IAmazonCloudWatch CloudWatch()
        {
            return Get(() => _Credentials == null
                ? new AmazonCloudWatchClient(Configure(new AmazonCloudWatchConfig()))
                : new AmazonCloudWatchClient(_Credentials, Configure(new AmazonCloudWatchConfig())));
        }

        /// <summary>
        /// CloudWatch Logs client.
        /// </summary>
        /// <returns>Client.</returns>
        public IAmazonCloudWatchLogs Logs()
        {
            return Get(() => _Credentials == null
                ? new AmazonCloudWatchLogsClient(Configure(new AmazonCloudWatchLogsConfig()))
                : new AmazonCloudWatchLogsClient(_Credentials, Configure(new AmazonCloudWatchLogsConfig())));
        }

        /// <summary>
        /// ECS client.
        /// </summary>
        /// <returns>Client.</returns>
        public IAmazonECS Ecs()
        {
            return Get(() => _Credentials == null
                ? new AmazonECSClient(Configure(new AmazonECSConfig()))
                : new AmazonECSClient(_Credentials, Configure(new AmazonECSConfig())));
        }

        /// <summary>
        /// RDS client.
        /// </summary>
        /// <returns>Client.</returns>
        public IAmazonRDS Rds()
        {
            return Get(() => _Credentials == null
                ? new AmazonRDSClient(Configure(new AmazonRDSConfig()))
                : new AmazonRDSClient(_Credentials, Configure(new AmazonRDSConfig())));
        }

        /// <summary>
        /// Lambda client.
        /// </summary>
        /// <returns>Client.</returns>
        public IAmazonLambda Lambda()
        {
            return Get(() => _Credentials == null
                ? new AmazonLambdaClient(Configure(new AmazonLambdaConfig()))
                : new AmazonLambdaClient(_Credentials, Configure(new AmazonLambdaConfig())));
        }

        /// <summary>
        /// Elastic Load Balancing v2 client.
        /// </summary>
        /// <returns>Client.</returns>
        public IAmazonElasticLoadBalancingV2 Elb()
        {
            return Get(() => _Credentials == null
                ? new AmazonElasticLoadBalancingV2Client(Configure(new AmazonElasticLoadBalancingV2Config()))
                : new AmazonElasticLoadBalancingV2Client(_Credentials, Configure(new AmazonElasticLoadBalancingV2Config())));
        }

        /// <summary>
        /// Route 53 client.
        /// </summary>
        /// <returns>Client.</returns>
        public IAmazonRoute53 Route53()
        {
            return Get(() => _Credentials == null
                ? new AmazonRoute53Client(Configure(new AmazonRoute53Config()))
                : new AmazonRoute53Client(_Credentials, Configure(new AmazonRoute53Config())));
        }

        /// <summary>
        /// SQS client.
        /// </summary>
        /// <returns>Client.</returns>
        public IAmazonSQS Sqs()
        {
            return Get(() => _Credentials == null
                ? new AmazonSQSClient(Configure(new AmazonSQSConfig()))
                : new AmazonSQSClient(_Credentials, Configure(new AmazonSQSConfig())));
        }

        /// <summary>
        /// SNS client.
        /// </summary>
        /// <returns>Client.</returns>
        public IAmazonSimpleNotificationService Sns()
        {
            return Get(() => _Credentials == null
                ? new AmazonSimpleNotificationServiceClient(Configure(new AmazonSimpleNotificationServiceConfig()))
                : new AmazonSimpleNotificationServiceClient(_Credentials, Configure(new AmazonSimpleNotificationServiceConfig())));
        }

        /// <summary>
        /// Apply the region, endpoint override, and retry settings to a client configuration.
        /// </summary>
        /// <typeparam name="T">Configuration type.</typeparam>
        /// <param name="config">Configuration.</param>
        /// <returns>The same configuration.</returns>
        /// <exception cref="ArgumentNullException">Thrown when config is null.</exception>
        public T Configure<T>(T config) where T : ClientConfig
        {
            ArgumentNullException.ThrowIfNull(config);
            return AwsSessionFactory.Configure(config, Options);
        }

        /// <summary>
        /// Dispose every client.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Dispose every client.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing)
            {
                lock (_Lock)
                {
                    foreach (IDisposable client in _Clients.Values)
                    {
                        try
                        {
                            client.Dispose();
                        }
                        catch (ObjectDisposedException)
                        {
                        }
                    }
                    _Clients.Clear();
                }
            }
            _Disposed = true;
        }

        #endregion

        #region Private-Methods

        private T Get<T>(Func<T> factory) where T : class, IDisposable
        {
            lock (_Lock)
            {
                if (_Disposed) throw new ObjectDisposedException(nameof(AwsSession));
                if (_Clients.TryGetValue(typeof(T), out IDisposable? existing) && existing is T typed) return typed;
                T created = factory();
                _Clients[typeof(T)] = created;
                return created;
            }
        }

        #endregion
    }
}
