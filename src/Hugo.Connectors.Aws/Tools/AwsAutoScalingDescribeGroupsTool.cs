namespace Hugo.Connectors.Aws.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Amazon.AutoScaling.Model;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Aws.Models;

    /// <summary>
    /// <c>aws_autoscaling_describe_groups</c> (Read): autoscaling:DescribeAutoScalingGroups with capacity, instance health,
    /// and paging. Thread-safe.
    /// </summary>
    public class AwsAutoScalingDescribeGroupsTool : AwsToolBase
    {
        #region Public-Members

        /// <summary>
        /// Tool name.
        /// </summary>
        public const string ToolName = "aws_autoscaling_describe_groups";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AwsAutoScalingDescribeGroupsTool() : base(BuildDescriptor(), AwsServiceNames.AutoScaling, "autoscaling:DescribeAutoScalingGroups")
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, AwsSession session, AwsOptions options, CancellationToken token)
        {
            DescribeAutoScalingGroupsRequest request = new DescribeAutoScalingGroupsRequest();
            List<string> names = arguments.GetStringList("groupNames");
            if (names.Count > 0) request.AutoScalingGroupNames = names;
            request.MaxRecords = PageSize(context, arguments, options, 1, 100);
            string? next = arguments.GetString("nextToken");
            if (!String.IsNullOrEmpty(next)) request.NextToken = next;

            DescribeAutoScalingGroupsResponse response = await session.AutoScaling().DescribeAutoScalingGroupsAsync(request, token).ConfigureAwait(false);
            bool showInstances = arguments.GetBool("includeInstances", false);
            AwsTable table = new AwsTable("Auto Scaling groups", "group", "desired", "min", "max", "inService", "unhealthy", "launchTemplate", "zones");
            List<string> instanceLines = new List<string>();
            foreach (AutoScalingGroup g in response.AutoScalingGroups ?? new List<AutoScalingGroup>())
            {
                List<Amazon.AutoScaling.Model.Instance> instances = g.Instances ?? new List<Amazon.AutoScaling.Model.Instance>();
                int inService = instances.Count(i => String.Equals(i.LifecycleState?.Value, "InService", StringComparison.Ordinal));
                int unhealthy = instances.Count(i => !String.Equals(i.HealthStatus, "Healthy", StringComparison.OrdinalIgnoreCase));
                string? template = g.LaunchTemplate?.LaunchTemplateName ?? g.MixedInstancesPolicy?.LaunchTemplate?.LaunchTemplateSpecification?.LaunchTemplateName ?? g.LaunchConfigurationName;
                table.Add(
                    g.AutoScalingGroupName,
                    AwsResultBuilder.Number(g.DesiredCapacity),
                    AwsResultBuilder.Number(g.MinSize),
                    AwsResultBuilder.Number(g.MaxSize),
                    inService.ToString(CultureInfo.InvariantCulture) + "/" + instances.Count.ToString(CultureInfo.InvariantCulture),
                    unhealthy.ToString(CultureInfo.InvariantCulture),
                    template,
                    Join(g.AvailabilityZones));
                if (showInstances)
                {
                    foreach (Amazon.AutoScaling.Model.Instance i in instances)
                        instanceLines.Add("- " + g.AutoScalingGroupName + " " + i.InstanceId + " " + i.LifecycleState?.Value + " " + i.HealthStatus + " " + i.AvailabilityZone);
                }
            }
            table.NextToken = response.NextToken;
            if (table.Rows.Count == 0) table.Notes.Add("No Auto Scaling groups matched.");
            if (instanceLines.Count > 0) table.Trailer = "Instances:\n" + String.Join("\n", instanceLines.Take(context.Limits.MaxRows)) + "\n";
            return Render(context, session, table);
        }

        #endregion

        #region Private-Methods

        private static ToolDescriptor BuildDescriptor()
        {
            ToolDescriptor d = new ToolDescriptor();
            d.Name = ToolName;
            d.Title = "Describe Auto Scaling groups";
            d.Description =
                "List EC2 Auto Scaling groups with desired, minimum, and maximum capacity, in-service and unhealthy instance "
                + "counts, launch template, and zones. Set includeInstances to list each instance's lifecycle and health. Paged with nextToken.";
            d.InputSchemaJson =
                "{\"type\":\"object\",\"properties\":{"
                + "\"groupNames\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"maxItems\":100,\"description\":\"Group names. Empty lists every group.\"},"
                + "\"includeInstances\":{\"type\":\"boolean\",\"description\":\"Also list the instances of each group. Default false.\"},"
                + "\"maxResults\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":100,\"description\":\"Page size.\"},"
                + "\"nextToken\":{\"type\":\"string\",\"description\":\"Token from the previous page.\"}"
                + "},\"additionalProperties\":false}";
            d.BaseEffect = EffectEnum.Read;
            d.OutputDescription = "A table of groups, optional instance lines, and a nextToken when more pages exist.";
            return d;
        }

        #endregion
    }
}
