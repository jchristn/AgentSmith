namespace Hugo.Connectors.Aws.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Amazon.RDS;
    using Amazon.RDS.Model;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Aws.Models;

    /// <summary>
    /// <c>aws_rds_describe</c> (Read): rds:DescribeDBInstances, rds:DescribeDBClusters, or rds:DescribeEvents (recent
    /// events in a bounded window), with Marker paging. Thread-safe.
    /// </summary>
    public class AwsRdsDescribeTool : AwsToolBase
    {
        #region Public-Members

        /// <summary>
        /// Tool name.
        /// </summary>
        public const string ToolName = "aws_rds_describe";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AwsRdsDescribeTool() : base(BuildDescriptor(), AwsServiceNames.Rds, "rds:DescribeDBInstances", "rds:DescribeDBClusters", "rds:DescribeEvents")
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, AwsSession session, AwsOptions options, CancellationToken token)
        {
            string kind = (arguments.GetString("kind") ?? "instances").Trim().ToLowerInvariant();
            string? identifier = arguments.GetString("identifier");
            if (identifier != null) identifier = identifier.Trim();
            int pageSize = PageSize(context, arguments, options, 20, 100);
            string? marker = arguments.GetString("nextToken");
            AwsTable table;

            switch (kind)
            {
                case "instances":
                    DescribeDBInstancesRequest ir = new DescribeDBInstancesRequest { MaxRecords = pageSize };
                    if (!String.IsNullOrEmpty(identifier)) ir.DBInstanceIdentifier = identifier;
                    if (!String.IsNullOrEmpty(marker)) ir.Marker = marker;
                    DescribeDBInstancesResponse instances = await session.Rds().DescribeDBInstancesAsync(ir, token).ConfigureAwait(false);
                    table = new AwsTable("RDS DB instances", "instance", "status", "engine", "class", "multiAz", "az", "endpoint", "storageGb", "cluster");
                    foreach (DBInstance i in instances.DBInstances ?? new List<DBInstance>())
                    {
                        string? endpoint = i.Endpoint == null ? null : i.Endpoint.Address + ":" + AwsResultBuilder.Number(i.Endpoint.Port);
                        table.Add(i.DBInstanceIdentifier, i.DBInstanceStatus, i.Engine + " " + i.EngineVersion, i.DBInstanceClass, Bool(i.MultiAZ), i.AvailabilityZone, endpoint, AwsResultBuilder.Number(i.AllocatedStorage), i.DBClusterIdentifier);
                    }
                    table.NextToken = instances.Marker;
                    break;

                case "clusters":
                    DescribeDBClustersRequest cr = new DescribeDBClustersRequest { MaxRecords = pageSize };
                    if (!String.IsNullOrEmpty(identifier)) cr.DBClusterIdentifier = identifier;
                    if (!String.IsNullOrEmpty(marker)) cr.Marker = marker;
                    DescribeDBClustersResponse clusters = await session.Rds().DescribeDBClustersAsync(cr, token).ConfigureAwait(false);
                    table = new AwsTable("RDS DB clusters", "cluster", "status", "engine", "multiAz", "writer", "readers", "endpoint", "readerEndpoint");
                    foreach (DBCluster c in clusters.DBClusters ?? new List<DBCluster>())
                    {
                        List<DBClusterMember> members = c.DBClusterMembers ?? new List<DBClusterMember>();
                        table.Add(
                            c.DBClusterIdentifier,
                            c.Status,
                            c.Engine + " " + c.EngineVersion,
                            Bool(c.MultiAZ),
                            Join(members.Where(m => m.IsClusterWriter == true).Select(m => m.DBInstanceIdentifier)),
                            Join(members.Where(m => m.IsClusterWriter != true).Select(m => m.DBInstanceIdentifier)),
                            c.Endpoint,
                            c.ReaderEndpoint);
                    }
                    table.NextToken = clusters.Marker;
                    break;

                case "events":
                    AwsTimeWindow window = AwsTimeWindow.Resolve(arguments, options, DateTime.UtcNow);
                    DescribeEventsRequest er = new DescribeEventsRequest { MaxRecords = pageSize, StartTime = window.StartUtc, EndTime = window.EndUtc };
                    if (!String.IsNullOrEmpty(identifier))
                    {
                        er.SourceIdentifier = identifier;
                        string sourceType = (arguments.GetString("sourceType") ?? "db-instance").Trim().ToLowerInvariant();
                        er.SourceType = new SourceType(sourceType);
                    }
                    if (!String.IsNullOrEmpty(marker)) er.Marker = marker;
                    DescribeEventsResponse events = await session.Rds().DescribeEventsAsync(er, token).ConfigureAwait(false);
                    table = new AwsTable("RDS events " + window.ToString(), "time", "source", "type", "categories", "message");
                    foreach (Event e in (events.Events ?? new List<Event>()).OrderByDescending(e => e.Date ?? DateTime.MinValue))
                        table.Add(AwsResultBuilder.Time(e.Date), e.SourceIdentifier, e.SourceType?.Value, Join(e.EventCategories), Shorten(e.Message, 300));
                    table.NextToken = events.Marker;
                    break;

                default:
                    throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "kind must be instances, clusters, or events.");
            }

            if (table.Rows.Count == 0) table.Notes.Add("Nothing matched.");
            return Render(context, session, table);
        }

        #endregion

        #region Private-Methods

        private static string? Bool(bool? value)
        {
            if (value == null) return null;
            return value.Value ? "yes" : "no";
        }

        private static ToolDescriptor BuildDescriptor()
        {
            ToolDescriptor d = new ToolDescriptor();
            d.Name = ToolName;
            d.Title = "Describe RDS";
            d.Description =
                "Describe Amazon RDS and Aurora: kind=instances (status, engine, class, Multi-AZ, endpoint, storage), "
                + "kind=clusters (status, writer and reader members, endpoints), or kind=events (recent RDS events such as "
                + "failovers, reboots, backups, and maintenance within the window). Narrow with identifier. Paged with nextToken.";
            d.InputSchemaJson =
                "{\"type\":\"object\",\"properties\":{"
                + "\"kind\":{\"type\":\"string\",\"enum\":[\"instances\",\"clusters\",\"events\"],\"description\":\"Default instances.\"},"
                + "\"identifier\":{\"type\":\"string\",\"description\":\"DB instance or cluster identifier (for events, the event source).\"},"
                + "\"sourceType\":{\"type\":\"string\",\"enum\":[\"db-instance\",\"db-cluster\",\"db-parameter-group\",\"db-security-group\",\"db-snapshot\",\"db-cluster-snapshot\"],\"description\":\"events with identifier: the source type. Default db-instance.\"},"
                + "\"lookbackMinutes\":{\"type\":\"integer\",\"minimum\":1,\"description\":\"events: window length. RDS keeps 14 days of events.\"},"
                + "\"startTime\":{\"type\":\"string\",\"description\":\"events: ISO 8601 start (UTC when no offset). Overrides lookbackMinutes.\"},\"endTime\":{\"type\":\"string\",\"description\":\"events: ISO 8601 end. Default now.\"},"
                + "\"maxResults\":{\"type\":\"integer\",\"minimum\":20,\"maximum\":100,\"description\":\"Rows per page, 20 to 100 (the RDS API refuses fewer than 20). Defaults to the resource's maxResults.\"},"
                + "\"nextToken\":{\"type\":\"string\",\"description\":\"Marker from the previous page.\"}"
                + "},\"additionalProperties\":false}";
            d.BaseEffect = EffectEnum.Read;
            d.OutputDescription = "A table of instances, clusters, or events and a nextToken when more pages exist.";
            return d;
        }

        #endregion
    }
}
