namespace Hugo.Connectors.Aws.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Amazon.ECS.Model;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Aws.Models;

    /// <summary>
    /// <c>aws_ecs_describe_services</c> (Read): ecs:ListServices and ecs:DescribeServices for one cluster, with
    /// deployments and the most recent service events. Thread-safe.
    /// </summary>
    public class AwsEcsDescribeServicesTool : AwsToolBase
    {
        #region Public-Members

        /// <summary>
        /// Tool name.
        /// </summary>
        public const string ToolName = "aws_ecs_describe_services";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AwsEcsDescribeServicesTool() : base(BuildDescriptor(), AwsServiceNames.Ecs, "ecs:DescribeServices", "ecs:ListServices")
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, AwsSession session, AwsOptions options, CancellationToken token)
        {
            string cluster = (arguments.GetString("cluster") ?? "default").Trim();
            List<string> names = arguments.GetStringList("services").Select(s => s.Trim()).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).ToList();
            if (names.Count > 10) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "At most 10 services per call.");
            string? nextToken = null;

            if (names.Count == 0)
            {
                ListServicesRequest list = new ListServicesRequest();
                list.Cluster = cluster;
                list.MaxResults = PageSize(context, arguments, options, 1, 10);
                string? next = arguments.GetString("nextToken");
                if (!String.IsNullOrEmpty(next)) list.NextToken = next;
                ListServicesResponse listed = await session.Ecs().ListServicesAsync(list, token).ConfigureAwait(false);
                names = listed.ServiceArns ?? new List<string>();
                nextToken = listed.NextToken;
            }

            AwsTable table = new AwsTable("ECS services in cluster " + cluster, "service", "status", "desired", "running", "pending", "launchType", "taskDefinition", "deployments");
            if (names.Count == 0)
            {
                table.Notes.Add("The cluster has no services.");
                return Render(context, session, table);
            }

            DescribeServicesResponse response = await session.Ecs().DescribeServicesAsync(new DescribeServicesRequest { Cluster = cluster, Services = names }, token).ConfigureAwait(false);
            int eventsPerService = arguments.GetInt("eventsPerService", 5, 0, 50);
            StringBuilder events = new StringBuilder();
            foreach (Service s in response.Services ?? new List<Service>())
            {
                List<Deployment> deployments = s.Deployments ?? new List<Deployment>();
                string? rollout = Join(deployments.Select(dp => dp.Status + (dp.RolloutState == null ? String.Empty : "/" + dp.RolloutState.Value) + " " + AwsResultBuilder.Number(dp.RunningCount) + "/" + AwsResultBuilder.Number(dp.DesiredCount) + " " + ShortTaskDefinition(dp.TaskDefinition)), "; ");
                table.Add(
                    s.ServiceName,
                    s.Status,
                    AwsResultBuilder.Number(s.DesiredCount),
                    AwsResultBuilder.Number(s.RunningCount),
                    AwsResultBuilder.Number(s.PendingCount),
                    s.LaunchType?.Value ?? (s.CapacityProviderStrategy != null && s.CapacityProviderStrategy.Count > 0 ? "capacity provider" : null),
                    ShortTaskDefinition(s.TaskDefinition),
                    rollout);

                if (eventsPerService > 0)
                {
                    foreach (ServiceEvent e in (s.Events ?? new List<ServiceEvent>()).OrderByDescending(e => e.CreatedAt ?? DateTime.MinValue).Take(eventsPerService))
                        events.Append("- ").Append(s.ServiceName).Append(' ').Append(AwsResultBuilder.Time(e.CreatedAt)).Append(' ').Append(Shorten(e.Message, 300)).Append('\n');
                }
            }
            foreach (Failure f in response.Failures ?? new List<Failure>())
                table.Notes.Add("Not described: " + f.Arn + " (" + f.Reason + (String.IsNullOrEmpty(f.Detail) ? String.Empty : ": " + f.Detail) + ")");
            table.NextToken = nextToken;
            table.Details["cluster"] = cluster;
            if (events.Length > 0) table.Trailer = "Recent service events (newest first):\n" + events.ToString();
            return Render(context, session, table);
        }

        #endregion

        #region Private-Methods

        private static string? ShortTaskDefinition(string? arn)
        {
            if (String.IsNullOrEmpty(arn)) return arn;
            int slash = arn.LastIndexOf('/');
            return slash >= 0 ? arn.Substring(slash + 1) : arn;
        }

        private static ToolDescriptor BuildDescriptor()
        {
            ToolDescriptor d = new ToolDescriptor();
            d.Name = ToolName;
            d.Title = "Describe ECS services";
            d.Description =
                "Describe ECS services in one cluster: status, desired, running, and pending task counts, launch type, task "
                + "definition, active deployments with rollout state, and the most recent service events (the best place to find "
                + "placement, health check, and deployment failures). Without services, lists the cluster's services (paged).";
            d.InputSchemaJson =
                "{\"type\":\"object\",\"properties\":{"
                + "\"cluster\":{\"type\":\"string\",\"description\":\"Cluster name or ARN. Default 'default'.\"},"
                + "\"services\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"maxItems\":10,\"description\":\"Service names or ARNs. Empty lists the cluster's services.\"},"
                + "\"eventsPerService\":{\"type\":\"integer\",\"minimum\":0,\"maximum\":50,\"description\":\"Recent events per service. Default 5.\"},"
                + "\"maxResults\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":10},"
                + "\"nextToken\":{\"type\":\"string\"}"
                + "},\"additionalProperties\":false}";
            d.BaseEffect = EffectEnum.Read;
            d.OutputDescription = "A table of services, recent events, and a nextToken when listing more services.";
            return d;
        }

        #endregion
    }
}
