namespace Hugo.Connectors.Aws.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Amazon.ElasticLoadBalancingV2.Model;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Aws.Models;

    /// <summary>
    /// <c>aws_elb_target_health</c> (Read): elasticloadbalancing:DescribeTargetHealth for a target group, the target groups
    /// of a load balancer, or the first target groups in the region. Thread-safe.
    /// </summary>
    public class AwsElbTargetHealthTool : AwsToolBase
    {
        #region Public-Members

        /// <summary>
        /// Tool name.
        /// </summary>
        public const string ToolName = "aws_elb_target_health";

        /// <summary>
        /// Most target groups checked in one call. Default 10, minimum 1, maximum 100.
        /// </summary>
        public int MaxTargetGroups
        {
            get
            {
                return _MaxTargetGroups;
            }
            set
            {
                _MaxTargetGroups = Math.Clamp(value, 1, 100);
            }
        }

        #endregion

        #region Private-Members

        private int _MaxTargetGroups = 10;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AwsElbTargetHealthTool() : base(
            BuildDescriptor(),
            AwsServiceNames.Elb,
            "elasticloadbalancing:DescribeTargetHealth",
            "elasticloadbalancing:DescribeTargetGroups",
            "elasticloadbalancing:DescribeLoadBalancers")
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, AwsSession session, AwsOptions options, CancellationToken token)
        {
            string? groupArn = arguments.GetString("targetGroupArn");
            string? groupName = arguments.GetString("targetGroupName");
            string? lbName = arguments.GetString("loadBalancerName");

            List<TargetGroup> groups = new List<TargetGroup>();
            bool more = false;
            if (!String.IsNullOrWhiteSpace(groupArn))
            {
                groups.Add(new TargetGroup { TargetGroupArn = groupArn.Trim(), TargetGroupName = NameFromArn(groupArn.Trim()) });
            }
            else
            {
                DescribeTargetGroupsRequest tg = new DescribeTargetGroupsRequest();
                if (!String.IsNullOrWhiteSpace(groupName))
                {
                    tg.Names = new List<string> { groupName.Trim() };
                }
                else if (!String.IsNullOrWhiteSpace(lbName))
                {
                    DescribeLoadBalancersResponse lbs = await session.Elb().DescribeLoadBalancersAsync(new DescribeLoadBalancersRequest { Names = new List<string> { lbName.Trim() } }, token).ConfigureAwait(false);
                    LoadBalancer? lb = (lbs.LoadBalancers ?? new List<LoadBalancer>()).FirstOrDefault();
                    if (lb == null) throw new ConnectorException(ConnectorErrorCodes.NotFound, "Load balancer '" + lbName + "' was not found.");
                    tg.LoadBalancerArn = lb.LoadBalancerArn;
                }
                tg.PageSize = Math.Min(_MaxTargetGroups, 400);
                DescribeTargetGroupsResponse found = await session.Elb().DescribeTargetGroupsAsync(tg, token).ConfigureAwait(false);
                groups = found.TargetGroups ?? groups;
                more = !String.IsNullOrEmpty(found.NextMarker) || groups.Count > _MaxTargetGroups;
                groups = groups.Take(_MaxTargetGroups).ToList();
            }

            AwsTable table = new AwsTable("ELB target health", "targetGroup", "target", "port", "az", "state", "reason", "description");
            int unhealthy = 0;
            foreach (TargetGroup g in groups)
            {
                token.ThrowIfCancellationRequested();
                DescribeTargetHealthResponse health = await session.Elb().DescribeTargetHealthAsync(new DescribeTargetHealthRequest { TargetGroupArn = g.TargetGroupArn }, token).ConfigureAwait(false);
                List<TargetHealthDescription> targets = health.TargetHealthDescriptions ?? new List<TargetHealthDescription>();
                if (targets.Count == 0) table.Add(g.TargetGroupName, null, null, null, "(no targets)", null, null);
                foreach (TargetHealthDescription t in targets)
                {
                    string? state = t.TargetHealth?.State?.Value;
                    if (!String.Equals(state, "healthy", StringComparison.OrdinalIgnoreCase)) unhealthy++;
                    table.Add(g.TargetGroupName, t.Target?.Id, AwsResultBuilder.Number(t.Target?.Port), t.Target?.AvailabilityZone, state, t.TargetHealth?.Reason?.Value, Shorten(t.TargetHealth?.Description, 200));
                }
            }
            table.Details["targetGroups"] = groups.Count.ToString(CultureInfo.InvariantCulture);
            table.Details["notHealthy"] = unhealthy.ToString(CultureInfo.InvariantCulture);
            if (more) table.Notes.Add("Only the first " + _MaxTargetGroups.ToString(CultureInfo.InvariantCulture) + " target groups were checked; name a target group or load balancer to narrow.");
            if (groups.Count == 0) table.Notes.Add("No target groups matched.");
            return Render(context, session, table);
        }

        #endregion

        #region Private-Methods

        private static string NameFromArn(string arn)
        {
            string[] parts = arn.Split('/');
            return parts.Length >= 2 ? parts[1] : arn;
        }

        private static ToolDescriptor BuildDescriptor()
        {
            ToolDescriptor d = new ToolDescriptor();
            d.Name = ToolName;
            d.Title = "ELB target health";
            d.Description =
                "Show the health of load balancer targets (ALB and NLB, Elastic Load Balancing v2): each target's state "
                + "(healthy, unhealthy, initial, draining, unused), reason code, and description. Name a targetGroupArn, a "
                + "targetGroupName, or a loadBalancerName to check all its target groups; with none, the first target groups in "
                + "the region are checked.";
            d.InputSchemaJson =
                "{\"type\":\"object\",\"properties\":{"
                + "\"targetGroupArn\":{\"type\":\"string\",\"description\":\"ARN of one target group to check. Takes precedence over targetGroupName and loadBalancerName.\"},"
                + "\"targetGroupName\":{\"type\":\"string\",\"description\":\"Name of one target group to check, used when targetGroupArn is not set.\"},"
                + "\"loadBalancerName\":{\"type\":\"string\",\"description\":\"Name of a load balancer whose target groups (the first 10) are checked, used when neither targetGroupArn nor targetGroupName is set. With none of the three, the first 10 target groups in the region are checked.\"}"
                + "},\"additionalProperties\":false}";
            d.BaseEffect = EffectEnum.Read;
            d.OutputDescription = "A table of targets with health state and reason.";
            return d;
        }

        #endregion
    }
}
