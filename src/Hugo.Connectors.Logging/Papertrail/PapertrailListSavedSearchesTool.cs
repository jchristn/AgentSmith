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
    /// <c>logs_list_saved_searches</c>: Papertrail saved searches with their queries and group scope. Read.
    /// Thread-safe.
    /// </summary>
    public class PapertrailListSavedSearchesTool : LoggingToolBase
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="options">Connector options.</param>
        /// <exception cref="ArgumentNullException">Thrown when options is null.</exception>
        public PapertrailListSavedSearchesTool(LoggingConnectorOptions options)
            : base(new ToolDescriptor
            {
                Name = "logs_list_saved_searches",
                Title = "List saved log searches",
                Description = "List Papertrail saved searches with name, query, and group. Saved searches show what the team already watches; reuse their "
                    + "query and group in logs_search or logs_volume.",
                InputSchemaJson = ListToolHelper.NameFilterSchema,
                BaseEffect = EffectEnum.Read,
                OutputDescription = "Table id, name, query, groupId, groupName plus structured JSON {searches[]}."
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
            List<PapertrailSavedSearch> searches = (await client.ListSavedSearchesAsync(token).ConfigureAwait(false))
                .Where(s => ListToolHelper.Matches(s.Name, filter))
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            List<IList<string?>> rows = searches
                .Select(s => (IList<string?>)new List<string?>
                {
                    s.Id.ToString(CultureInfo.InvariantCulture),
                    s.Name,
                    s.Query,
                    s.Group == null ? null : s.Group.Id.ToString(CultureInfo.InvariantCulture),
                    s.Group == null ? null : s.Group.Name
                })
                .ToList();
            List<PapertrailSavedSearch> shown = searches.Take(context.Limits.MaxRows).ToList();
            return ListToolHelper.Render("Papertrail saved searches", new List<string> { "id", "name", "query", "groupId", "groupName" }, rows, new Dictionary<string, object> { { "searches", shown } }, context.Limits);
        }

        #endregion
    }
}
