namespace Hugo.Connectors.Aws.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Amazon.Route53;
    using Amazon.Route53.Model;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Aws.Models;

    /// <summary>
    /// <c>aws_route53_list_records</c> (Read): route53:ListResourceRecordSets for a hosted zone given by ID or name, or
    /// route53:ListHostedZones when no zone is named. Thread-safe.
    /// </summary>
    public class AwsRoute53ListRecordsTool : AwsToolBase
    {
        #region Public-Members

        /// <summary>
        /// Tool name.
        /// </summary>
        public const string ToolName = "aws_route53_list_records";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AwsRoute53ListRecordsTool() : base(BuildDescriptor(), AwsServiceNames.Route53, "route53:ListResourceRecordSets", "route53:ListHostedZones", "route53:ListHostedZonesByName")
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Encode a record set paging position as one token (<c>name|type|identifier</c>).
        /// </summary>
        /// <param name="name">Next record name.</param>
        /// <param name="type">Next record type.</param>
        /// <param name="identifier">Next set identifier, or null.</param>
        /// <returns>Token, or null when there is no next page.</returns>
        public static string? EncodeToken(string? name, string? type, string? identifier)
        {
            if (String.IsNullOrEmpty(name)) return null;
            return name + "|" + (type ?? String.Empty) + "|" + (identifier ?? String.Empty);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, AwsSession session, AwsOptions options, CancellationToken token)
        {
            IAmazonRoute53 r53 = session.Route53();
            string? zoneId = arguments.GetString("hostedZoneId");
            string? zoneName = arguments.GetString("zoneName");
            int pageSize = PageSize(context, arguments, options, 1, 300);
            string? next = arguments.GetString("nextToken");

            if (String.IsNullOrWhiteSpace(zoneId) && String.IsNullOrWhiteSpace(zoneName))
            {
                ListHostedZonesRequest zr = new ListHostedZonesRequest { MaxItems = Math.Min(pageSize, 100).ToString(System.Globalization.CultureInfo.InvariantCulture) };
                if (!String.IsNullOrEmpty(next)) zr.Marker = next;
                ListHostedZonesResponse zones = await r53.ListHostedZonesAsync(zr, token).ConfigureAwait(false);
                AwsTable zt = new AwsTable("Route 53 hosted zones", "zoneId", "name", "private", "records", "comment");
                foreach (HostedZone z in zones.HostedZones ?? new List<HostedZone>())
                    zt.Add(ShortId(z.Id), z.Name, z.Config?.PrivateZone == true ? "yes" : "no", AwsResultBuilder.Number(z.ResourceRecordSetCount), Shorten(z.Config?.Comment, 120));
                zt.NextToken = zones.IsTruncated == true ? zones.NextMarker : null;
                zt.Notes.Add("Pass hostedZoneId or zoneName to list a zone's records.");
                return Render(context, session, zt);
            }

            string id;
            if (!String.IsNullOrWhiteSpace(zoneId))
            {
                id = ShortId(zoneId.Trim()) ?? zoneId.Trim();
            }
            else
            {
                string wanted = zoneName!.Trim().TrimEnd('.') + ".";
                ListHostedZonesByNameResponse byName = await r53.ListHostedZonesByNameAsync(new ListHostedZonesByNameRequest { DNSName = wanted, MaxItems = "10" }, token).ConfigureAwait(false);
                HostedZone? zone = (byName.HostedZones ?? new List<HostedZone>()).FirstOrDefault(z => String.Equals(z.Name, wanted, StringComparison.OrdinalIgnoreCase));
                if (zone == null) throw new ConnectorException(ConnectorErrorCodes.NotFound, "No hosted zone named '" + wanted + "' was found.");
                id = ShortId(zone.Id) ?? zone.Id;
            }

            ListResourceRecordSetsRequest request = new ListResourceRecordSetsRequest { HostedZoneId = id, MaxItems = pageSize.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            if (!String.IsNullOrEmpty(next))
            {
                string[] parts = next.Split('|');
                request.StartRecordName = parts[0];
                if (parts.Length > 1 && parts[1].Length > 0) request.StartRecordType = new RRType(parts[1]);
                if (parts.Length > 2 && parts[2].Length > 0) request.StartRecordIdentifier = parts[2];
            }
            else
            {
                string? startName = arguments.GetString("startRecordName");
                if (!String.IsNullOrWhiteSpace(startName)) request.StartRecordName = startName.Trim();
                string? startType = arguments.GetString("startRecordType");
                if (!String.IsNullOrWhiteSpace(startType)) request.StartRecordType = new RRType(startType.Trim().ToUpperInvariant());
            }
            string? typeFilter = arguments.GetString("type");

            ListResourceRecordSetsResponse response = await r53.ListResourceRecordSetsAsync(request, token).ConfigureAwait(false);
            AwsTable table = new AwsTable("Route 53 records in zone " + id, "name", "type", "ttl", "values", "routing");
            foreach (ResourceRecordSet r in response.ResourceRecordSets ?? new List<ResourceRecordSet>())
            {
                if (!String.IsNullOrWhiteSpace(typeFilter) && !String.Equals(r.Type?.Value, typeFilter.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                string? values = r.AliasTarget != null
                    ? "ALIAS " + r.AliasTarget.DNSName
                    : Join((r.ResourceRecords ?? new List<ResourceRecord>()).Select(v => v.Value), " ");
                List<string> routing = new List<string>();
                if (!String.IsNullOrEmpty(r.SetIdentifier)) routing.Add("id=" + r.SetIdentifier);
                if (r.Weight != null) routing.Add("weight=" + r.Weight.Value);
                if (r.Region != null) routing.Add("region=" + r.Region.Value);
                if (r.Failover != null) routing.Add("failover=" + r.Failover.Value);
                if (!String.IsNullOrEmpty(r.HealthCheckId)) routing.Add("healthCheck=" + r.HealthCheckId);
                table.Add(r.Name, r.Type?.Value, AwsResultBuilder.Number(r.TTL), Shorten(values, 300), Join(routing, " "));
            }
            table.NextToken = response.IsTruncated == true ? EncodeToken(response.NextRecordName, response.NextRecordType?.Value, response.NextRecordIdentifier) : null;
            table.Details["hostedZoneId"] = id;
            if (table.Rows.Count == 0) table.Notes.Add("No records matched on this page.");
            return Render(context, session, table);
        }

        #endregion

        #region Private-Methods

        private static string? ShortId(string? id)
        {
            if (String.IsNullOrEmpty(id)) return id;
            int slash = id.LastIndexOf('/');
            return slash >= 0 ? id.Substring(slash + 1) : id;
        }

        private static ToolDescriptor BuildDescriptor()
        {
            ToolDescriptor d = new ToolDescriptor();
            d.Name = ToolName;
            d.Title = "List Route 53 records";
            d.Description =
                "List DNS records of a Route 53 hosted zone (name, type, TTL, values or alias target, and routing policy). Name "
                + "the zone by hostedZoneId or zoneName; with neither, lists the hosted zones. Start at a record with "
                + "startRecordName and startRecordType, filter a page by type, and continue with nextToken.";
            d.InputSchemaJson =
                "{\"type\":\"object\",\"properties\":{"
                + "\"hostedZoneId\":{\"type\":\"string\",\"description\":\"Zone ID, for example Z123ABC (or /hostedzone/Z123ABC).\"},"
                + "\"zoneName\":{\"type\":\"string\",\"description\":\"Zone name, for example example.com.\"},"
                + "\"startRecordName\":{\"type\":\"string\",\"description\":\"Start listing at this record name (in Route 53 order) instead of at the top of the zone. Ignored when nextToken is set.\"},"
                + "\"startRecordType\":{\"type\":\"string\",\"description\":\"With startRecordName, the record type to start at, for example A or CNAME. Ignored when nextToken is set.\"},"
                + "\"type\":{\"type\":\"string\",\"description\":\"Show only this record type on the page, for example A or CNAME.\"},"
                + "\"maxResults\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":300,\"description\":\"Records per page, 1 to 300. Defaults to the resource's maxResults and is capped by the call's row limit.\"},"
                + "\"nextToken\":{\"type\":\"string\",\"description\":\"The nextToken from the previous page, to fetch the next page. Send the same filters again.\"}"
                + "},\"additionalProperties\":false}";
            d.BaseEffect = EffectEnum.Read;
            d.OutputDescription = "A table of records (or hosted zones) and a nextToken when more pages exist.";
            return d;
        }

        #endregion
    }
}
