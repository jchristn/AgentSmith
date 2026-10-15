namespace Hugo.Connectors.Aws.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Amazon.EC2.Model;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Aws.Models;

    /// <summary>
    /// <c>aws_ec2_describe_instances</c> (Read): ec2:DescribeInstances with instance IDs, states, filters, and paging.
    /// Thread-safe.
    /// </summary>
    public class AwsEc2DescribeInstancesTool : AwsToolBase
    {
        #region Public-Members

        /// <summary>
        /// Tool name.
        /// </summary>
        public const string ToolName = "aws_ec2_describe_instances";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AwsEc2DescribeInstancesTool() : base(BuildDescriptor(), AwsServiceNames.Ec2, "ec2:DescribeInstances")
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, AwsSession session, AwsOptions options, CancellationToken token)
        {
            List<string> ids = AwsArguments.InstanceIds(arguments.GetStringList("instanceIds"));
            List<string> states = arguments.GetStringList("states").Select(s => s.Trim().ToLowerInvariant()).Where(s => s.Length > 0).ToList();

            DescribeInstancesRequest request = new DescribeInstancesRequest();
            if (ids.Count > 0)
            {
                request.InstanceIds = ids;
            }
            else
            {
                request.MaxResults = PageSize(context, arguments, options, 5, 1000);
                string? next = arguments.GetString("nextToken");
                if (!String.IsNullOrEmpty(next)) request.NextToken = next;
            }

            List<Filter> filters = AwsArguments.Filters(arguments).Select(f => new Filter(f.Name, f.Values)).ToList();
            if (states.Count > 0) filters.Add(new Filter("instance-state-name", states));
            if (filters.Count > 0) request.Filters = filters;

            DescribeInstancesResponse response = await session.Ec2().DescribeInstancesAsync(request, token).ConfigureAwait(false);

            AwsTable table = new AwsTable("EC2 instances", "instanceId", "name", "state", "type", "az", "privateIp", "publicIp", "launched");
            foreach (Reservation reservation in response.Reservations ?? new List<Reservation>())
            {
                foreach (Instance i in reservation.Instances ?? new List<Instance>())
                {
                    string? name = (i.Tags ?? new List<Tag>()).FirstOrDefault(t => t.Key == "Name")?.Value;
                    table.Add(
                        i.InstanceId,
                        name,
                        i.State?.Name?.Value,
                        i.InstanceType?.Value,
                        i.Placement?.AvailabilityZone,
                        i.PrivateIpAddress,
                        i.PublicIpAddress,
                        AwsResultBuilder.Time(i.LaunchTime));
                }
            }
            table.NextToken = response.NextToken;
            if (table.Rows.Count == 0) table.Notes.Add("No instances matched.");
            return Render(context, session, table);
        }

        #endregion

        #region Private-Methods

        private static ToolDescriptor BuildDescriptor()
        {
            ToolDescriptor d = new ToolDescriptor();
            d.Name = ToolName;
            d.Title = "Describe EC2 instances";
            d.Description =
                "List EC2 instances with ID, Name tag, state, type, availability zone, IPs, and launch time. Narrow with "
                + "instanceIds, states (for example [\"running\"]), or AWS filters such as {\"name\":\"tag:env\",\"values\":[\"prod\"]}. "
                + "Results are paged: pass nextToken from the previous result to continue.";
            d.InputSchemaJson =
                "{\"type\":\"object\",\"properties\":{"
                + "\"instanceIds\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"maxItems\":200,\"description\":\"Instance IDs (i-...). Paging is not used when IDs are given.\"},"
                + "\"states\":{\"type\":\"array\",\"items\":{\"type\":\"string\",\"enum\":[\"pending\",\"running\",\"shutting-down\",\"terminated\",\"stopping\",\"stopped\"]},\"description\":\"Only instances in these states.\"},"
                + "\"filters\":{\"type\":\"array\",\"maxItems\":20,\"items\":{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\",\"description\":\"Filter name, for example tag:Name, vpc-id, or instance-type.\"},\"values\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"description\":\"Values to match; an instance matches when any value matches. Wildcards * and ? are allowed.\"}},\"required\":[\"name\",\"values\"]},\"description\":\"EC2 describe filters, for example tag:Name, vpc-id, instance-type.\"},"
                + "\"maxResults\":{\"type\":\"integer\",\"minimum\":5,\"maximum\":1000,\"description\":\"Page size. Defaults to the resource's maxResults.\"},"
                + "\"nextToken\":{\"type\":\"string\",\"description\":\"Token from the previous page.\"}"
                + "},\"additionalProperties\":false}";
            d.BaseEffect = EffectEnum.Read;
            d.OutputDescription = "A table of instances and a nextToken when more pages exist.";
            return d;
        }

        #endregion
    }
}
