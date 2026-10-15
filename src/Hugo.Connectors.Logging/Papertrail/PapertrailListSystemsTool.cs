namespace Hugo.Connectors.Logging
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>logs_list_systems</c>: Papertrail systems (log senders) with IDs and last event times. Read. Thread-safe.
    /// </summary>
    public class PapertrailListSystemsTool : LoggingToolBase
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="options">Connector options.</param>
        /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
        public PapertrailListSystemsTool(LoggingConnectorOptions options)
            : base(new ToolDescriptor
            {
                Name = "logs_list_systems",
                Title = "List log systems",
                Description = "List Papertrail systems (hosts and apps that send logs) with ID, name, IP, and last event time. Use a system name or ID in "
                    + "logs_search providerOptions.system to search one sender. A stale lastEventAt means the sender stopped logging.",
                InputSchemaJson = ListToolHelper.NameFilterSchema,
                BaseEffect = EffectEnum.Read,
                OutputDescription = "Table id, name, ipAddress, lastEventAt plus structured JSON {systems[]}."
            }, options)
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token)
        {
            PapertrailClient client = PapertrailClient.FromResource(context.Resource);
            string? filter = arguments.GetString("nameContains");
            List<PapertrailSystem> systems = (await client.ListSystemsAsync(token).ConfigureAwait(false))
                .Where(s => ListToolHelper.Matches(s.Name, filter))
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            List<IList<string?>> rows = systems
                .Select(s => (IList<string?>)new List<string?>
                {
                    s.Id.ToString(CultureInfo.InvariantCulture),
                    s.Name,
                    s.IpAddress,
                    ListToolHelper.FormatTime(s.LastEventAt)
                })
                .ToList();
            List<PapertrailSystem> shown = systems.Take(context.Limits.MaxRows).ToList();
            return ListToolHelper.Render("Papertrail systems", new List<string> { "id", "name", "ipAddress", "lastEventAt" }, rows, new Dictionary<string, object> { { "systems", shown } }, context.Limits);
        }

        #endregion
    }
}
