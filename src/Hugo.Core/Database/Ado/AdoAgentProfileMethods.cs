namespace Hugo.Core.Database.Ado
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Database.Interfaces;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;

    /// <summary>
    /// Provider-neutral agent profile data access. Thread-safe.
    /// </summary>
    public class AdoAgentProfileMethods : IAgentProfileMethods
    {
        #region Private-Members

        private readonly AdoDatabaseDriver _Driver;
        private readonly string _Table;
        private static readonly string _Columns = "id, tenant_id, owner_user_id, visibility, name, display_name, persona_name, user_address, description, persona_template_key, inference_endpoint_id, fallback_inference_endpoint_id, embedding_endpoint_id, max_iterations, max_tool_calls_per_turn, tool_timeout_seconds, temperature, expose_a2a, expose_mcp, expose_openai, expose_management_tools, instructions_append, instruction_detail, is_default, active, created_utc, last_update_utc";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">Owning driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when driver is null.</exception>
        public AdoAgentProfileMethods(AdoDatabaseDriver driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Table = driver.Table("agent_profiles");
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<AgentProfile> CreateAsync(AgentProfile obj, CancellationToken token = default)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            obj.CreatedUtc = DateTime.UtcNow;
            obj.LastUpdateUtc = obj.CreatedUtc;
            string values = "@" + _Columns.Replace(", ", ", @");
            await _Driver.NonQueryAsync("INSERT INTO " + _Table + " (" + _Columns + ") VALUES (" + values + ");", command => Bind(command, obj, true), token).ConfigureAwait(false);
            return obj;
        }

        /// <inheritdoc />
        public async Task<AgentProfile?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            return await _Driver.QuerySingleAsync("SELECT * FROM " + _Table + " WHERE tenant_id = @tenant_id AND id = @id;", command =>
            {
                AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                AdoDatabaseDriver.Add(command, "id", id);
            }, Map, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<AgentProfile?> ReadByNameAsync(string tenantId, string name, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            return await _Driver.QuerySingleAsync("SELECT * FROM " + _Table + " WHERE tenant_id = @tenant_id AND LOWER(name) = @name;", command =>
            {
                AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                AdoDatabaseDriver.Add(command, "name", name.ToLowerInvariant());
            }, Map, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<AgentProfile> UpdateAsync(AgentProfile obj, CancellationToken token = default)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            obj.LastUpdateUtc = DateTime.UtcNow;
            List<string> sets = new List<string>();
            foreach (string column in _Columns.Split(", "))
            {
                if (column == "id" || column == "tenant_id" || column == "created_utc") continue;
                sets.Add(column + " = @" + column);
            }
            await _Driver.NonQueryAsync("UPDATE " + _Table + " SET " + String.Join(", ", sets) + " WHERE tenant_id = @tenant_id AND id = @id;", command => Bind(command, obj, false), token).ConfigureAwait(false);
            return obj;
        }

        /// <inheritdoc />
        public async Task<bool> DeleteCascadeAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            int affected = 0;
            string bindings = _Driver.Table("bindings");
            await _Driver.ExecuteInTransactionAsync(async (connection, transaction, ct) =>
            {
                Action<DbCommand> bind = command =>
                {
                    AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                    AdoDatabaseDriver.Add(command, "profile_id", id);
                };
                string bindingIds = "(SELECT id FROM " + bindings + " WHERE tenant_id = @tenant_id AND agent_profile_id = @profile_id)";
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "DELETE FROM " + _Driver.Table("binding_tool_rules") + " WHERE tenant_id = @tenant_id AND binding_id IN " + bindingIds + ";", bind, ct).ConfigureAwait(false);
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "DELETE FROM " + _Driver.Table("binding_deny_patterns") + " WHERE tenant_id = @tenant_id AND binding_id IN " + bindingIds + ";", bind, ct).ConfigureAwait(false);
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "DELETE FROM " + bindings + " WHERE tenant_id = @tenant_id AND agent_profile_id = @profile_id;", bind, ct).ConfigureAwait(false);
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "UPDATE " + _Driver.Table("credentials") + " SET agent_profile_id = NULL, lock_to_profile = " + _Driver.Dialect.BooleanLiteral(false) + " WHERE tenant_id = @tenant_id AND agent_profile_id = @profile_id;", bind, ct).ConfigureAwait(false);
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "UPDATE " + _Driver.Table("users") + " SET default_agent_profile_id = NULL WHERE tenant_id = @tenant_id AND default_agent_profile_id = @profile_id;", bind, ct).ConfigureAwait(false);
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "UPDATE " + _Driver.Table("tenants") + " SET default_agent_profile_id = NULL WHERE id = @tenant_id AND default_agent_profile_id = @profile_id;", bind, ct).ConfigureAwait(false);
                affected = await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "DELETE FROM " + _Table + " WHERE tenant_id = @tenant_id AND id = @profile_id;", bind, ct).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return affected > 0;
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<AgentProfile>> EnumerateAsync(string tenantId, EnumerationQuery query, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (query == null) throw new ArgumentNullException(nameof(query));
            List<string> clauses = new List<string> { "tenant_id = @tenant_id" };
            if (!String.IsNullOrEmpty(query.Search)) clauses.Add(_Driver.Dialect.CaseInsensitiveLike("name", "search"));
            if (query.ActiveOnly) clauses.Add("active = " + _Driver.Dialect.BooleanLiteral(true));
            string where = " WHERE " + String.Join(" AND ", clauses);
            Action<DbCommand> bind = command =>
            {
                AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                if (!String.IsNullOrEmpty(query.Search)) AdoDatabaseDriver.Add(command, "search", "%" + query.Search + "%");
            };
            object? countValue = await _Driver.ScalarAsync("SELECT COUNT(*) FROM " + _Table + where + ";", bind, token).ConfigureAwait(false);
            long total = countValue == null ? 0 : Convert.ToInt64(countValue, CultureInfo.InvariantCulture);
            List<AgentProfile> objects = await _Driver.QueryAsync("SELECT * FROM " + _Table + where + AdoEnumerationSql.OrderBy(query.Ordering, true) + _Driver.Dialect.LimitOffsetClause() + ";", command =>
            {
                bind(command);
                AdoDatabaseDriver.Add(command, "max", query.MaxResults);
                AdoDatabaseDriver.Add(command, "skip", query.Skip);
            }, Map, token).ConfigureAwait(false);
            return EnumerationResult<AgentProfile>.Build(query, total, objects);
        }

        /// <inheritdoc />
        public async Task<List<AgentProfile>> ListAllAsync(string tenantId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            return await _Driver.QueryAsync("SELECT * FROM " + _Table + " WHERE tenant_id = @tenant_id ORDER BY name ASC;", command => AdoDatabaseDriver.Add(command, "tenant_id", tenantId), Map, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task SetDefaultAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            await _Driver.ExecuteInTransactionAsync(async (connection, transaction, ct) =>
            {
                Action<DbCommand> bind = command =>
                {
                    AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                    AdoDatabaseDriver.Add(command, "id", id);
                };
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "UPDATE " + _Table + " SET is_default = " + _Driver.Dialect.BooleanLiteral(false) + " WHERE tenant_id = @tenant_id AND id <> @id;", bind, ct).ConfigureAwait(false);
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "UPDATE " + _Table + " SET is_default = " + _Driver.Dialect.BooleanLiteral(true) + " WHERE tenant_id = @tenant_id AND id = @id;", bind, ct).ConfigureAwait(false);
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "UPDATE " + _Driver.Table("tenants") + " SET default_agent_profile_id = @id WHERE id = @tenant_id;", bind, ct).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<AgentProfile>> ListByEndpointAsync(string tenantId, string endpointId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(endpointId)) throw new ArgumentNullException(nameof(endpointId));
            return await _Driver.QueryAsync("SELECT * FROM " + _Table + " WHERE tenant_id = @tenant_id AND (inference_endpoint_id = @e OR fallback_inference_endpoint_id = @e OR embedding_endpoint_id = @e) ORDER BY name ASC;", command =>
            {
                AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                AdoDatabaseDriver.Add(command, "e", endpointId);
            }, Map, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Map a row to a profile.
        /// </summary>
        /// <param name="reader">Reader positioned on a row.</param>
        /// <returns>Profile.</returns>
        public static AgentProfile Map(DbDataReader reader)
        {
            AgentProfile obj = new AgentProfile();
            obj.Id = AdoConverters.Str(reader, "id");
            obj.TenantId = AdoConverters.Str(reader, "tenant_id");
            obj.OwnerUserId = AdoConverters.NullableStr(reader, "owner_user_id");
            obj.Visibility = AdoConverters.Enum(reader, "visibility", VisibilityEnum.Tenant);
            obj.Name = AdoConverters.Str(reader, "name");
            obj.DisplayName = AdoConverters.NullableStr(reader, "display_name");
            obj.PersonaName = AdoConverters.Str(reader, "persona_name");
            obj.UserAddress = AdoConverters.Str(reader, "user_address");
            obj.Description = AdoConverters.NullableStr(reader, "description");
            obj.PersonaTemplateKey = AdoConverters.Str(reader, "persona_template_key");
            obj.InferenceEndpointId = AdoConverters.NullableStr(reader, "inference_endpoint_id");
            obj.FallbackInferenceEndpointId = AdoConverters.NullableStr(reader, "fallback_inference_endpoint_id");
            obj.EmbeddingEndpointId = AdoConverters.NullableStr(reader, "embedding_endpoint_id");
            obj.MaxIterations = AdoConverters.Int(reader, "max_iterations");
            obj.MaxToolCallsPerTurn = AdoConverters.Int(reader, "max_tool_calls_per_turn");
            obj.ToolTimeoutSeconds = AdoConverters.Int(reader, "tool_timeout_seconds");
            obj.Temperature = AdoConverters.NullableDouble(reader, "temperature");
            obj.ExposeA2A = AdoConverters.Bool(reader, "expose_a2a");
            obj.ExposeMcp = AdoConverters.Bool(reader, "expose_mcp");
            obj.ExposeOpenAI = AdoConverters.Bool(reader, "expose_openai");
            obj.ExposeManagementTools = AdoConverters.Bool(reader, "expose_management_tools");
            obj.InstructionsAppend = AdoConverters.NullableStr(reader, "instructions_append");
            obj.InstructionDetail = AdoConverters.Enum(reader, "instruction_detail", InstructionDetailEnum.Auto);
            obj.IsDefault = AdoConverters.Bool(reader, "is_default");
            obj.Active = AdoConverters.Bool(reader, "active");
            obj.CreatedUtc = AdoConverters.Date(reader, "created_utc");
            obj.LastUpdateUtc = AdoConverters.Date(reader, "last_update_utc");
            return obj;
        }

        #endregion

        #region Private-Methods

        private static void Bind(DbCommand command, AgentProfile obj, bool insert)
        {
            AdoDatabaseDriver.Add(command, "id", obj.Id);
            AdoDatabaseDriver.Add(command, "tenant_id", obj.TenantId);
            AdoDatabaseDriver.Add(command, "owner_user_id", obj.OwnerUserId);
            AdoDatabaseDriver.Add(command, "visibility", obj.Visibility.ToString());
            AdoDatabaseDriver.Add(command, "name", obj.Name);
            AdoDatabaseDriver.Add(command, "display_name", obj.DisplayName);
            AdoDatabaseDriver.Add(command, "persona_name", obj.PersonaName);
            AdoDatabaseDriver.Add(command, "user_address", obj.UserAddress);
            AdoDatabaseDriver.Add(command, "description", obj.Description);
            AdoDatabaseDriver.Add(command, "persona_template_key", obj.PersonaTemplateKey);
            AdoDatabaseDriver.Add(command, "inference_endpoint_id", obj.InferenceEndpointId);
            AdoDatabaseDriver.Add(command, "fallback_inference_endpoint_id", obj.FallbackInferenceEndpointId);
            AdoDatabaseDriver.Add(command, "embedding_endpoint_id", obj.EmbeddingEndpointId);
            AdoDatabaseDriver.Add(command, "max_iterations", obj.MaxIterations);
            AdoDatabaseDriver.Add(command, "max_tool_calls_per_turn", obj.MaxToolCallsPerTurn);
            AdoDatabaseDriver.Add(command, "tool_timeout_seconds", obj.ToolTimeoutSeconds);
            AdoDatabaseDriver.Add(command, "temperature", obj.Temperature);
            AdoDatabaseDriver.Add(command, "expose_a2a", obj.ExposeA2A);
            AdoDatabaseDriver.Add(command, "expose_mcp", obj.ExposeMcp);
            AdoDatabaseDriver.Add(command, "expose_openai", obj.ExposeOpenAI);
            AdoDatabaseDriver.Add(command, "expose_management_tools", obj.ExposeManagementTools);
            AdoDatabaseDriver.Add(command, "instructions_append", obj.InstructionsAppend);
            AdoDatabaseDriver.Add(command, "instruction_detail", obj.InstructionDetail.ToString());
            AdoDatabaseDriver.Add(command, "is_default", obj.IsDefault);
            AdoDatabaseDriver.Add(command, "active", obj.Active);
            if (insert) AdoDatabaseDriver.Add(command, "created_utc", obj.CreatedUtc);
            AdoDatabaseDriver.Add(command, "last_update_utc", obj.LastUpdateUtc);
        }

        #endregion
    }
}
