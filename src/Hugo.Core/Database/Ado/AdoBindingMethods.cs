namespace Hugo.Core.Database.Ado
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Database.Interfaces;
    using Hugo.Core.Enums;
    using Hugo.Core.Helpers;
    using Hugo.Core.Models;

    /// <summary>
    /// Provider-neutral binding data access over bindings, binding_tool_rules, and binding_deny_patterns. Thread-safe.
    /// </summary>
    public class AdoBindingMethods : IBindingMethods
    {
        #region Private-Members

        private readonly AdoDatabaseDriver _Driver;
        private readonly string _Table;
        private readonly string _Rules;
        private readonly string _Patterns;
        private readonly string _Resources;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">Owning driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when driver is null.</exception>
        public AdoBindingMethods(AdoDatabaseDriver driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Table = driver.Table("bindings");
            _Rules = driver.Table("binding_tool_rules");
            _Patterns = driver.Table("binding_deny_patterns");
            _Resources = driver.Table("resources");
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<Binding> CreateAsync(Binding obj, CancellationToken token = default)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            obj.CreatedUtc = DateTime.UtcNow;
            obj.LastUpdateUtc = obj.CreatedUtc;
            await _Driver.ExecuteInTransactionAsync(async (connection, transaction, ct) =>
            {
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction,
                    "INSERT INTO " + _Table + " (id, tenant_id, agent_profile_id, resource_id, alias, access_mode, require_approval_for_destructive, instruction_mode, instruction_text, max_output_bytes, max_rows, enabled, allow_knowledge_updates, created_utc, last_update_utc) VALUES (@id, @tenant_id, @agent_profile_id, @resource_id, @alias, @access_mode, @require_approval_for_destructive, @instruction_mode, @instruction_text, @max_output_bytes, @max_rows, @enabled, @allow_knowledge_updates, @created_utc, @last_update_utc);",
                    command => Bind(command, obj, true), ct).ConfigureAwait(false);
                await InsertChildrenAsync(connection, transaction, obj, ct).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return obj;
        }

        /// <inheritdoc />
        public async Task<Binding?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            List<Binding> list = await QueryAsync("b.tenant_id = @tenant_id AND b.id = @id", command =>
            {
                AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                AdoDatabaseDriver.Add(command, "id", id);
            }, token).ConfigureAwait(false);
            return list.FirstOrDefault();
        }

        /// <inheritdoc />
        public async Task<Binding> UpdateAsync(Binding obj, CancellationToken token = default)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            obj.LastUpdateUtc = DateTime.UtcNow;
            await _Driver.ExecuteInTransactionAsync(async (connection, transaction, ct) =>
            {
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction,
                    "UPDATE " + _Table + " SET alias = @alias, access_mode = @access_mode, require_approval_for_destructive = @require_approval_for_destructive, instruction_mode = @instruction_mode, instruction_text = @instruction_text, max_output_bytes = @max_output_bytes, max_rows = @max_rows, enabled = @enabled, allow_knowledge_updates = @allow_knowledge_updates, last_update_utc = @last_update_utc WHERE tenant_id = @tenant_id AND id = @id AND agent_profile_id = @agent_profile_id AND resource_id = @resource_id;",
                    command => Bind(command, obj, false), ct).ConfigureAwait(false);
                await DeleteChildrenAsync(connection, transaction, obj.TenantId, obj.Id, ct).ConfigureAwait(false);
                await InsertChildrenAsync(connection, transaction, obj, ct).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return obj;
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            int affected = 0;
            await _Driver.ExecuteInTransactionAsync(async (connection, transaction, ct) =>
            {
                await DeleteChildrenAsync(connection, transaction, tenantId, id, ct).ConfigureAwait(false);
                affected = await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "DELETE FROM " + _Table + " WHERE tenant_id = @tenant_id AND id = @id;", command =>
                {
                    AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                    AdoDatabaseDriver.Add(command, "id", id);
                }, ct).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return affected > 0;
        }

        /// <inheritdoc />
        public async Task<List<Binding>> ListByProfileAsync(string tenantId, string profileId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(profileId)) throw new ArgumentNullException(nameof(profileId));
            return await QueryAsync("b.tenant_id = @tenant_id AND b.agent_profile_id = @profile_id", command =>
            {
                AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                AdoDatabaseDriver.Add(command, "profile_id", profileId);
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<Binding>> ListByResourceAsync(string tenantId, string resourceId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(resourceId)) throw new ArgumentNullException(nameof(resourceId));
            return await QueryAsync("b.tenant_id = @tenant_id AND b.resource_id = @resource_id", command =>
            {
                AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                AdoDatabaseDriver.Add(command, "resource_id", resourceId);
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<Dictionary<string, int>> CountByProfileAsync(string tenantId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            List<KeyValuePair<string, int>> rows = await _Driver.QueryAsync(
                "SELECT agent_profile_id, COUNT(*) AS binding_count FROM " + _Table + " WHERE tenant_id = @tenant_id GROUP BY agent_profile_id;",
                command => AdoDatabaseDriver.Add(command, "tenant_id", tenantId),
                reader => new KeyValuePair<string, int>(AdoConverters.Str(reader, "agent_profile_id"), AdoConverters.Int(reader, "binding_count")),
                token).ConfigureAwait(false);
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, int> row in rows) counts[row.Key] = row.Value;
            return counts;
        }

        #endregion

        #region Private-Methods

        private async Task<List<Binding>> QueryAsync(string where, Action<DbCommand> bind, CancellationToken token)
        {
            List<Binding> bindings = await _Driver.QueryAsync(
                "SELECT b.*, r.name AS resource_name, r.connector_type AS resource_connector_type FROM " + _Table + " b LEFT JOIN " + _Resources + " r ON r.id = b.resource_id AND r.tenant_id = b.tenant_id WHERE " + where + " ORDER BY b.alias ASC;",
                bind, Map, token).ConfigureAwait(false);
            if (bindings.Count == 0) return bindings;
            List<string> parameters = new List<string>();
            for (int i = 0; i < bindings.Count; i++) parameters.Add("@b" + i);
            string inList = "(" + String.Join(", ", parameters) + ")";
            Action<DbCommand> bindIds = command =>
            {
                for (int i = 0; i < bindings.Count; i++) AdoDatabaseDriver.Add(command, "b" + i, bindings[i].Id);
            };
            List<BindingToolRule> rules = await _Driver.QueryAsync("SELECT * FROM " + _Rules + " WHERE binding_id IN " + inList + " ORDER BY tool_name ASC;", bindIds, reader =>
            {
                BindingToolRule rule = new BindingToolRule();
                rule.Id = AdoConverters.Str(reader, "id");
                rule.BindingId = AdoConverters.Str(reader, "binding_id");
                rule.TenantId = AdoConverters.Str(reader, "tenant_id");
                rule.ToolName = AdoConverters.Str(reader, "tool_name");
                rule.Rule = AdoConverters.Enum(reader, "rule", ToolRuleEnum.Allow);
                return rule;
            }, token).ConfigureAwait(false);
            List<KeyValuePair<string, string>> patterns = await _Driver.QueryAsync("SELECT binding_id, pattern FROM " + _Patterns + " WHERE binding_id IN " + inList + " ORDER BY ordinal ASC;", bindIds,
                reader => new KeyValuePair<string, string>(AdoConverters.Str(reader, "binding_id"), AdoConverters.Str(reader, "pattern")), token).ConfigureAwait(false);
            foreach (Binding binding in bindings)
            {
                binding.ToolRules = rules.Where(r => r.BindingId == binding.Id).ToList();
                binding.DenyPatterns = patterns.Where(p => p.Key == binding.Id).Select(p => p.Value).ToList();
            }
            return bindings;
        }

        private static Binding Map(DbDataReader reader)
        {
            Binding obj = new Binding();
            obj.Id = AdoConverters.Str(reader, "id");
            obj.TenantId = AdoConverters.Str(reader, "tenant_id");
            obj.AgentProfileId = AdoConverters.Str(reader, "agent_profile_id");
            obj.ResourceId = AdoConverters.Str(reader, "resource_id");
            obj.Alias = AdoConverters.Str(reader, "alias");
            obj.AccessMode = AdoConverters.Enum(reader, "access_mode", AccessModeEnum.ReadOnly);
            obj.RequireApprovalForDestructive = AdoConverters.Bool(reader, "require_approval_for_destructive");
            obj.InstructionMode = AdoConverters.Enum(reader, "instruction_mode", InstructionModeEnum.Inherit);
            obj.InstructionText = AdoConverters.NullableStr(reader, "instruction_text");
            obj.MaxOutputBytes = AdoConverters.NullableInt(reader, "max_output_bytes");
            obj.MaxRows = AdoConverters.NullableInt(reader, "max_rows");
            obj.Enabled = AdoConverters.Bool(reader, "enabled");
            obj.AllowKnowledgeUpdates = AdoConverters.Bool(reader, "allow_knowledge_updates");
            obj.ResourceName = AdoConverters.NullableStr(reader, "resource_name");
            obj.ConnectorType = AdoConverters.NullableStr(reader, "resource_connector_type");
            obj.CreatedUtc = AdoConverters.Date(reader, "created_utc");
            obj.LastUpdateUtc = AdoConverters.Date(reader, "last_update_utc");
            return obj;
        }

        private static void Bind(DbCommand command, Binding obj, bool insert)
        {
            AdoDatabaseDriver.Add(command, "id", obj.Id);
            AdoDatabaseDriver.Add(command, "tenant_id", obj.TenantId);
            AdoDatabaseDriver.Add(command, "agent_profile_id", obj.AgentProfileId);
            AdoDatabaseDriver.Add(command, "resource_id", obj.ResourceId);
            AdoDatabaseDriver.Add(command, "alias", obj.Alias);
            AdoDatabaseDriver.Add(command, "access_mode", obj.AccessMode.ToString());
            AdoDatabaseDriver.Add(command, "require_approval_for_destructive", obj.RequireApprovalForDestructive);
            AdoDatabaseDriver.Add(command, "instruction_mode", obj.InstructionMode.ToString());
            AdoDatabaseDriver.Add(command, "instruction_text", obj.InstructionText);
            AdoDatabaseDriver.Add(command, "max_output_bytes", obj.MaxOutputBytes);
            AdoDatabaseDriver.Add(command, "max_rows", obj.MaxRows);
            AdoDatabaseDriver.Add(command, "enabled", obj.Enabled);
            AdoDatabaseDriver.Add(command, "allow_knowledge_updates", obj.AllowKnowledgeUpdates);
            if (insert) AdoDatabaseDriver.Add(command, "created_utc", obj.CreatedUtc);
            AdoDatabaseDriver.Add(command, "last_update_utc", obj.LastUpdateUtc);
        }

        private async Task InsertChildrenAsync(DbConnection connection, DbTransaction transaction, Binding obj, CancellationToken token)
        {
            foreach (BindingToolRule rule in obj.ToolRules)
            {
                rule.BindingId = obj.Id;
                rule.TenantId = obj.TenantId;
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "INSERT INTO " + _Rules + " (id, binding_id, tenant_id, tool_name, rule) VALUES (@id, @binding_id, @tenant_id, @tool_name, @rule);", command =>
                {
                    AdoDatabaseDriver.Add(command, "id", rule.Id);
                    AdoDatabaseDriver.Add(command, "binding_id", rule.BindingId);
                    AdoDatabaseDriver.Add(command, "tenant_id", rule.TenantId);
                    AdoDatabaseDriver.Add(command, "tool_name", rule.ToolName);
                    AdoDatabaseDriver.Add(command, "rule", rule.Rule.ToString());
                }, token).ConfigureAwait(false);
            }
            for (int i = 0; i < obj.DenyPatterns.Count; i++)
            {
                int ordinal = i;
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "INSERT INTO " + _Patterns + " (id, binding_id, tenant_id, ordinal, pattern) VALUES (@id, @binding_id, @tenant_id, @ordinal, @pattern);", command =>
                {
                    AdoDatabaseDriver.Add(command, "id", IdGenerator.NewChildRowId());
                    AdoDatabaseDriver.Add(command, "binding_id", obj.Id);
                    AdoDatabaseDriver.Add(command, "tenant_id", obj.TenantId);
                    AdoDatabaseDriver.Add(command, "ordinal", ordinal);
                    AdoDatabaseDriver.Add(command, "pattern", obj.DenyPatterns[ordinal]);
                }, token).ConfigureAwait(false);
            }
        }

        private async Task DeleteChildrenAsync(DbConnection connection, DbTransaction transaction, string tenantId, string bindingId, CancellationToken token)
        {
            Action<DbCommand> bind = command =>
            {
                AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                AdoDatabaseDriver.Add(command, "binding_id", bindingId);
            };
            await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "DELETE FROM " + _Rules + " WHERE tenant_id = @tenant_id AND binding_id = @binding_id;", bind, token).ConfigureAwait(false);
            await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "DELETE FROM " + _Patterns + " WHERE tenant_id = @tenant_id AND binding_id = @binding_id;", bind, token).ConfigureAwait(false);
        }

        #endregion
    }
}
