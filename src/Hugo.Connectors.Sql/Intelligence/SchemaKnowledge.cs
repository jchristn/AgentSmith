namespace Hugo.Connectors.Sql.Intelligence
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// A snapshot of the durable SQL context for one resource: the database entry and the table entries keyed by
    /// <c>schema.table</c>. Replaces the <c>Context</c> fields Tablix kept on its crawl models. Not thread-safe for
    /// mutation.
    /// </summary>
    public class SchemaKnowledge
    {
        #region Public-Members

        /// <summary>
        /// Database-level entry, or null.
        /// </summary>
        public ResourceKnowledgeEntry? Database { get; set; } = null;

        /// <summary>
        /// Table entries keyed by <c>schema.table</c>, case-insensitive. Never null.
        /// </summary>
        public Dictionary<string, ResourceKnowledgeEntry> Tables
        {
            get
            {
                return _Tables;
            }
            set
            {
                _Tables = value == null
                    ? new Dictionary<string, ResourceKnowledgeEntry>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, ResourceKnowledgeEntry>(value, StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Whether non-empty database context exists.
        /// </summary>
        public bool HasDatabaseContext
        {
            get
            {
                return Database != null && !String.IsNullOrWhiteSpace(Database.Content);
            }
        }

        #endregion

        #region Private-Members

        private Dictionary<string, ResourceKnowledgeEntry> _Tables = new Dictionary<string, ResourceKnowledgeEntry>(StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty snapshot.
        /// </summary>
        public SchemaKnowledge()
        {
        }

        /// <summary>
        /// Load the SQL scopes from a store.
        /// </summary>
        /// <param name="store">Store.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Snapshot.</returns>
        /// <exception cref="ArgumentNullException">Thrown when store is null.</exception>
        public static async Task<SchemaKnowledge> LoadAsync(IResourceKnowledgeStore store, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(store);
            token.ThrowIfCancellationRequested();
            SchemaKnowledge knowledge = new SchemaKnowledge();
            knowledge.Database = await store.GetAsync(SqlKnowledgeScopes.Database, String.Empty, token).ConfigureAwait(false);
            List<ResourceKnowledgeEntry> tables = await store.ListAsync(SqlKnowledgeScopes.Table, token).ConfigureAwait(false);
            foreach (ResourceKnowledgeEntry entry in tables)
            {
                if (!String.IsNullOrWhiteSpace(entry.Key) && !String.IsNullOrWhiteSpace(entry.Content)) knowledge._Tables[entry.Key] = entry;
            }
            return knowledge;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Table entry for a schema and table, or null.
        /// </summary>
        /// <param name="schemaName">Schema.</param>
        /// <param name="tableName">Table.</param>
        /// <returns>Entry or null.</returns>
        public ResourceKnowledgeEntry? Table(string? schemaName, string? tableName)
        {
            return _Tables.TryGetValue(TableIdentity.Key(schemaName, tableName), out ResourceKnowledgeEntry? entry) ? entry : null;
        }

        /// <summary>
        /// Every non-empty context text (database first, then tables).
        /// </summary>
        /// <returns>Texts.</returns>
        public List<string> AllContent()
        {
            List<string> texts = new List<string>();
            if (HasDatabaseContext) texts.Add(Database!.Content);
            texts.AddRange(_Tables.Values.Select(e => e.Content).Where(c => !String.IsNullOrWhiteSpace(c)));
            return texts;
        }

        #endregion
    }
}
