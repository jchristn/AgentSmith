namespace Hugo.Connectors.Aws.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Amazon.ECS.Model;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Aws.Models;

    /// <summary>
    /// <c>aws_ecs_update_service</c> (Write, classified per call): ecs:UpdateService limited to the desired count and a
    /// forced new deployment. Scaling a service to zero stops every task, so a desired count of 0 is Destructive.
    /// Thread-safe.
    /// </summary>
    public class AwsEcsUpdateServiceTool : AwsToolBase
    {
        #region Public-Members

        /// <summary>
        /// Tool name.
        /// </summary>
        public const string ToolName = "aws_ecs_update_service";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AwsEcsUpdateServiceTool() : base(BuildDescriptor(), AwsServiceNames.Ecs, "ecs:UpdateService")
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override EffectClassification ClassifyCore(ConnectorInvocationContext context, ToolArguments arguments, AwsOptions options)
        {
            string service = (arguments.GetString("service") ?? String.Empty).Trim();
            string cluster = (arguments.GetString("cluster") ?? "default").Trim();
            if (service.Length == 0) return EffectClassification.Reject("service is required");
            bool hasCount = arguments.Has("desiredCount");
            bool force = arguments.GetBool("forceNewDeployment", false);
            if (!hasCount && !force) return EffectClassification.Reject("nothing to change: set desiredCount or forceNewDeployment");

            long count = arguments.GetLong("desiredCount", -1);
            if (hasCount && count < 0) return EffectClassification.Reject("desiredCount must be zero or more");

            List<string> changes = new List<string>();
            if (hasCount) changes.Add("desired count " + count.ToString(CultureInfo.InvariantCulture));
            if (force) changes.Add("force a new deployment (replaces every running task)");
            string summary = "Update ECS service " + service + " in cluster " + cluster + ": " + String.Join(", ", changes) + "; " + Where(context);

            if (hasCount && count == 0)
                return new EffectClassification(EffectEnum.Destructive, "ecs:UpdateService with desiredCount 0 stops every task of the service", summary);
            return new EffectClassification(EffectEnum.Write, "ecs:UpdateService is Write", summary);
        }

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, AwsSession session, AwsOptions options, CancellationToken token)
        {
            string service = arguments.GetRequiredString("service").Trim();
            string cluster = (arguments.GetString("cluster") ?? "default").Trim();
            bool hasCount = arguments.Has("desiredCount");
            bool force = arguments.GetBool("forceNewDeployment", false);
            if (!hasCount && !force) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Set desiredCount, forceNewDeployment, or both.");

            UpdateServiceRequest request = new UpdateServiceRequest();
            request.Cluster = cluster;
            request.Service = service;
            if (hasCount)
            {
                long count = arguments.GetLong("desiredCount", -1);
                if (count < 0 || count > 100000) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "desiredCount must be between 0 and 100000.");
                request.DesiredCount = (int)count;
            }
            if (force) request.ForceNewDeployment = true;

            UpdateServiceResponse response = await session.Ecs().UpdateServiceAsync(request, token).ConfigureAwait(false);
            Service? s = response.Service;
            AwsTable table = new AwsTable("ECS service updated in cluster " + cluster, "service", "status", "desired", "running", "pending", "taskDefinition", "deployments");
            if (s != null)
            {
                table.Add(
                    s.ServiceName,
                    s.Status,
                    AwsResultBuilder.Number(s.DesiredCount),
                    AwsResultBuilder.Number(s.RunningCount),
                    AwsResultBuilder.Number(s.PendingCount),
                    s.TaskDefinition,
                    AwsResultBuilder.Number((s.Deployments ?? new List<Deployment>()).Count));
            }
            table.Details["cluster"] = cluster;
            table.Details["service"] = service;
            if (hasCount) table.Details["desiredCount"] = AwsResultBuilder.Number(request.DesiredCount);
            table.Details["forceNewDeployment"] = force ? "true" : "false";
            table.Notes.Add("Deployments roll out asynchronously; follow progress with aws_ecs_describe_services.");
            return Render(context, session, table);
        }

        #endregion

        #region Private-Methods

        private static ToolDescriptor BuildDescriptor()
        {
            ToolDescriptor d = new ToolDescriptor();
            d.Name = ToolName;
            d.Title = "Update ECS service";
            d.Description =
                "Change an ECS service's desired task count and/or force a new deployment (restarts every task with the current "
                + "task definition, the usual fix for stuck or unhealthy tasks). This is Write and usually needs approval; "
                + "a desiredCount of 0 stops the service and is Destructive. Nothing else about the service can be changed.";
            d.InputSchemaJson =
                "{\"type\":\"object\",\"properties\":{"
                + "\"cluster\":{\"type\":\"string\",\"description\":\"Cluster name or ARN. Default 'default'.\"},"
                + "\"service\":{\"type\":\"string\",\"minLength\":1,\"description\":\"Service name or ARN.\"},"
                + "\"desiredCount\":{\"type\":\"integer\",\"minimum\":0,\"maximum\":100000},"
                + "\"forceNewDeployment\":{\"type\":\"boolean\"}"
                + "},\"required\":[\"service\"],\"additionalProperties\":false}";
            d.BaseEffect = EffectEnum.Write;
            d.ClassifiedPerCall = true;
            d.OutputDescription = "The service after the update.";
            return d;
        }

        #endregion
    }
}
