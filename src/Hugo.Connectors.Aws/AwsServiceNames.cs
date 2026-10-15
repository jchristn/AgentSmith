namespace Hugo.Connectors.Aws
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Service names accepted by the <c>allowedServices</c> setting. Each tool belongs to exactly one service. Thread-safe.
    /// </summary>
    public static class AwsServiceNames
    {
        #region Public-Members

        /// <summary>
        /// AWS Security Token Service. <c>aws_caller_identity</c> is always allowed so the account can be confirmed.
        /// </summary>
        public const string Sts = "sts";

        /// <summary>
        /// Amazon EC2.
        /// </summary>
        public const string Ec2 = "ec2";

        /// <summary>
        /// EC2 Auto Scaling.
        /// </summary>
        public const string AutoScaling = "autoscaling";

        /// <summary>
        /// Amazon CloudWatch (metrics and alarms).
        /// </summary>
        public const string CloudWatch = "cloudwatch";

        /// <summary>
        /// Amazon CloudWatch Logs.
        /// </summary>
        public const string Logs = "logs";

        /// <summary>
        /// Amazon ECS.
        /// </summary>
        public const string Ecs = "ecs";

        /// <summary>
        /// Amazon RDS.
        /// </summary>
        public const string Rds = "rds";

        /// <summary>
        /// AWS Lambda.
        /// </summary>
        public const string Lambda = "lambda";

        /// <summary>
        /// Elastic Load Balancing v2 (ALB and NLB).
        /// </summary>
        public const string Elb = "elb";

        /// <summary>
        /// Amazon Route 53.
        /// </summary>
        public const string Route53 = "route53";

        /// <summary>
        /// Amazon SQS.
        /// </summary>
        public const string Sqs = "sqs";

        /// <summary>
        /// Amazon SNS.
        /// </summary>
        public const string Sns = "sns";

        /// <summary>
        /// Every known service name.
        /// </summary>
        public static IReadOnlyList<string> All
        {
            get
            {
                return _All;
            }
        }

        #endregion

        #region Private-Members

        private static readonly List<string> _All = new List<string>
        {
            Sts, Ec2, AutoScaling, CloudWatch, Logs, Ecs, Rds, Lambda, Elb, Route53, Sqs, Sns
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a name is a known service (case-insensitive).
        /// </summary>
        /// <param name="name">Name.</param>
        /// <returns>True when known.</returns>
        public static bool IsKnown(string? name)
        {
            if (String.IsNullOrWhiteSpace(name)) return false;
            string n = name.Trim();
            return _All.Any(s => String.Equals(s, n, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Whether a service is allowed by an allow list. An empty list allows every service, and STS is always allowed.
        /// </summary>
        /// <param name="allowed">Allow list (case-insensitive).</param>
        /// <param name="service">Service name.</param>
        /// <returns>True when allowed.</returns>
        /// <exception cref="ArgumentNullException">Thrown when allowed is null.</exception>
        public static bool IsAllowed(IReadOnlyCollection<string> allowed, string service)
        {
            ArgumentNullException.ThrowIfNull(allowed);
            if (String.Equals(service, Sts, StringComparison.OrdinalIgnoreCase)) return true;
            if (!allowed.Any()) return true;
            return allowed.Any(s => String.Equals(s.Trim(), service, StringComparison.OrdinalIgnoreCase));
        }

        #endregion
    }
}
