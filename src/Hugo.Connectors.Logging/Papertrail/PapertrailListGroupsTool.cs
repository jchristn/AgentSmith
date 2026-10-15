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
    /// <c>logs_list_groups</c>: Papertrail groups with their member systems. Read. Thread-safe.
    /// </summary>
    public class PapertrailListGroupsTool : LoggingToolBase
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="options">Connector options.</param>
        /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
        public PapertrailListGroupsTool(LoggingConnectorOptions options)
            : base(new ToolDescriptor
            {
                Name = "logs_list_groups",
                Title = "List log groups",
                Description = "List Papertrail groups (named sets of systems) with ID, name, wildcard, and member systems. Pass a group name or ID in "
                    + "logs_search providerOptions.group to search a whole tier, such as all web servers.",
                InputSchemaJson = ListToolHelper.NameFilterSchema,
                BaseEffect = EffectEnum.Read,
                OutputDescription = "Table id, name, systemWildcard, systems plus structured JSON {groups[]}."
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
            List<PapertrailGroup> groups = (await client.ListGroupsAsync(token).ConfigureAwait(false))
                .Where(g => ListToolHelper.Matches(g.Name, filter))
                .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            List<IList<string?>> rows = groups
                .Select(g => (IList<string?>)new List<string?>
                {
                    g.Id.ToString(CultureInfo.InvariantCulture),
                    g.Name,
                    g.SystemWildcard,
                    g.Systems == null ? String.Empty : String.Join(", ", g.Systems.Select(s => s.Name))
                })
                .ToList();
            List<PapertrailGroup> shown = groups.Take(context.Limits.MaxRows).ToList();
            return ListToolHelper.Render("Papertrail groups", new List<string> { "id", "name", "systemWildcard", "systems" }, rows, new Dictionary<string, object> { { "groups", shown } }, context.Limits);
        }

        #endregion
    }
}
