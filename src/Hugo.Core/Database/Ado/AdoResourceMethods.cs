namespace Hugo.Core.Database.Ado
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Database.Interfaces;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;

    /// <summary>
    /// Provider-neutral resource data access over resources, resource_settings, and resource_secret_bindings. Settings
    /// are stored in typed columns by value kind (D-016). Thread-safe.
    /// </summary>
    public class AdoResourceMethods : IResourceMethods
    {
        #region Private-Members

        private readonly AdoDatabaseDriver _Driver;
        private readonly string _Table;
        private readonly string _Settings;
        private readonly string _Slots;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">Owning driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when driver is null.</exception>
        public AdoResourceMethods(AdoDatabaseDriver driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Table = driver.Table("resources");
            _Settings = driver.Table("resource_settings");
            _Slots = driver.Table("resource_secret_bindings");
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<Resource> CreateAsync(Resource obj, CancellationToken token = default)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            obj.CreatedUtc = DateTime.UtcNow;
            obj.LastUpdateUtc = obj.CreatedUtc;
            await _Driver.ExecuteInTransactionAsync(async (connection, transaction, ct) =>
            {
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction,
                    "INSERT INTO " + _Table + " (id, tenant_id, owner_user_id, visibility, name, connector_type, display_name, description, endpoint, environment, notes, default_access_mode_ceiling, last_test_utc, last_test_result, last_test_message, active, created_utc, last_update_utc) VALUES (@id, @tenant_id, @owner_user_id, @visibility, @name, @connector_type, @display_name, @description, @endpoint, @environment, @notes, @default_access_mode_ceiling, @last_test_utc, @last_test_result, @last_test_message, @active, @created_utc, @last_update_utc);",
                    command => Bind(command, obj, true), ct).ConfigureAwait(false);
                await InsertChildrenAsync(connection, transaction, obj, ct).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return obj;
        }

        /// <inheritdoc />
        public async Task<Resource?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            Resource? obj = await _Driver.QuerySingleAsync("SELECT * FROM " + _Table + " WHERE tenant_id = @tenant_id AND id = @id;", command =>
            {
                AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                AdoDatabaseDriver.Add(command, "id", id);
            }, Map, token).ConfigureAwait(false);
            if (obj != null) await LoadChildrenAsync(new List<Resource> { obj }, token).ConfigureAwait(false);
            return obj;
        }

        /// <inheritdoc />
        public async Task<Resource?> ReadByNameAsync(string tenantId, string name, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            Resource? obj = await _Driver.QuerySingleAsync("SELECT * FROM " + _Table + " WHERE tenant_id = @tenant_id AND name = @name;", command =>
            {
                AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                AdoDatabaseDriver.Add(command, "name", name);
            }, Map, token).ConfigureAwait(false);
            if (obj != null) await LoadChildrenAsync(new List<Resource> { obj }, token).ConfigureAwait(false);
            return obj;
        }

        /// <inheritdoc />
        public async Task<Resource> UpdateAsync(Resource obj, CancellationToken token = default)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            obj.LastUpdateUtc = DateTime.UtcNow;
            await _Driver.ExecuteInTransactionAsync(async (connection, transaction, ct) =>
            {
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction,
                    "UPDATE " + _Table + " SET owner_user_id = @owner_user_id, visibility = @visibility, name = @name, connector_type = @connector_type, display_name = @display_name, description = @description, endpoint = @endpoint, environment = @environment, notes = @notes, default_access_mode_ceiling = @default_access_mode_ceiling, last_test_utc = @last_test_utc, last_test_result = @last_test_result, last_test_message = @last_test_message, active = @active, last_update_utc = @last_update_utc WHERE tenant_id = @tenant_id AND id = @id;",
                    command => Bind(command, obj, false), ct).ConfigureAwait(false);
                await DeleteChildrenAsync(connection, transaction, obj.TenantId, obj.Id, ct).ConfigureAwait(false);
                await InsertChildrenAsync(connection, transaction, obj, ct).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
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
                    AdoDatabaseDriver.Add(command, "resource_id", id);
                };
                string bindingIds = "(SELECT id FROM " + bindings + " WHERE tenant_id = @tenant_id AND resource_id = @resource_id)";
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "DELETE FROM " + _Driver.Table("binding_tool_rules") + " WHERE tenant_id = @tenant_id AND binding_id IN " + bindingIds + ";", bind, ct).ConfigureAwait(false);
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "DELETE FROM " + _Driver.Table("binding_deny_patterns") + " WHERE tenant_id = @tenant_id AND binding_id IN " + bindingIds + ";", bind, ct).ConfigureAwait(false);
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "DELETE FROM " + bindings + " WHERE tenant_id = @tenant_id AND resource_id = @resource_id;", bind, ct).ConfigureAwait(false);
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "DELETE FROM " + _Driver.Table("connector_schema_cache") + " WHERE tenant_id = @tenant_id AND resource_id = @resource_id;", bind, ct).ConfigureAwait(false);
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "DELETE FROM " + _Driver.Table("resource_knowledge_versions") + " WHERE tenant_id = @tenant_id AND resource_id = @resource_id;", bind, ct).ConfigureAwait(false);
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "DELETE FROM " + _Driver.Table("resource_knowledge") + " WHERE tenant_id = @tenant_id AND resource_id = @resource_id;", bind, ct).ConfigureAwait(false);
                await DeleteChildrenAsync(connection, transaction, tenantId, id, ct).ConfigureAwait(false);
                affected = await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "DELETE FROM " + _Table + " WHERE tenant_id = @tenant_id AND id = @resource_id;", bind, ct).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return affected > 0;
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<Resource>> EnumerateAsync(string tenantId, EnumerationQuery query, string? connectorType = null, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (query == null) throw new ArgumentNullException(nameof(query));
            List<string> clauses = new List<string> { "tenant_id = @tenant_id" };
            if (!String.IsNullOrEmpty(query.Search)) clauses.Add(_Driver.Dialect.CaseInsensitiveLike("name", "search"));
            if (!String.IsNullOrEmpty(connectorType)) clauses.Add("connector_type = @connector_type");
            if (query.ActiveOnly) clauses.Add("active = " + _Driver.Dialect.BooleanLiteral(true));
            string where = " WHERE " + String.Join(" AND ", clauses);
            Action<DbCommand> bind = command =>
            {
                AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                if (!String.IsNullOrEmpty(query.Search)) AdoDatabaseDriver.Add(command, "search", "%" + query.Search + "%");
                if (!String.IsNullOrEmpty(connectorType)) AdoDatabaseDriver.Add(command, "connector_type", connectorType);
            };
            object? countValue = await _Driver.ScalarAsync("SELECT COUNT(*) FROM " + _Table + where + ";", bind, token).ConfigureAwait(false);
            long total = countValue == null ? 0 : Convert.ToInt64(countValue, CultureInfo.InvariantCulture);
            List<Resource> objects = await _Driver.QueryAsync("SELECT * FROM " + _Table + where + AdoEnumerationSql.OrderBy(query.Ordering, true) + _Driver.Dialect.LimitOffsetClause() + ";", command =>
            {
                bind(command);
                AdoDatabaseDriver.Add(command, "max", query.MaxResults);
                AdoDatabaseDriver.Add(command, "skip", query.Skip);
            }, Map, token).ConfigureAwait(false);
            await LoadChildrenAsync(objects, token).ConfigureAwait(false);
            return EnumerationResult<Resource>.Build(query, total, objects);
        }

        /// <inheritdoc />
        public async Task<List<Resource>> ListAllAsync(string tenantId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            List<Resource> objects = await _Driver.QueryAsync("SELECT * FROM " + _Table + " WHERE tenant_id = @tenant_id ORDER BY name ASC;", command => AdoDatabaseDriver.Add(command, "tenant_id", tenantId), Map, token).ConfigureAwait(false);
            await LoadChildrenAsync(objects, token).ConfigureAwait(false);
            return objects;
        }

        /// <inheritdoc />
        public async Task<List<Resource>> ListBySecretAsync(string tenantId, string secretId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(secretId)) throw new ArgumentNullException(nameof(secretId));
            return await _Driver.QueryAsync(
                "SELECT * FROM " + _Table + " WHERE tenant_id = @tenant_id AND id IN (SELECT resource_id FROM " + _Slots + " WHERE tenant_id = @tenant_id AND secret_id = @secret_id) ORDER BY name ASC;",
                command =>
                {
                    AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                    AdoDatabaseDriver.Add(command, "secret_id", secretId);
                }, Map, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task UpdateTestResultAsync(string tenantId, string id, bool success, string? message, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            await _Driver.NonQueryAsync("UPDATE " + _Table + " SET last_test_utc = @now, last_test_result = @result, last_test_message = @message WHERE tenant_id = @tenant_id AND id = @id;", command =>
            {
                AdoDatabaseDriver.Add(command, "now", DateTime.UtcNow);
                AdoDatabaseDriver.Add(command, "result", success);
                AdoDatabaseDriver.Add(command, "message", message);
                AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                AdoDatabaseDriver.Add(command, "id", id);
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task UpsertSettingAsync(string tenantId, string resourceId, string key, SettingValueKindEnum kind, string value, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(resourceId)) throw new ArgumentNullException(nameof(resourceId));
            if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
            ResourceSetting setting = new ResourceSetting();
            setting.TenantId = tenantId;
            setting.ResourceId = resourceId;
            setting.Key = key;
            setting.ValueKind = kind;
            setting.Value = value ?? String.Empty;
            await _Driver.ExecuteInTransactionAsync(async (connection, transaction, ct) =>
            {
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "DELETE FROM " + _Settings + " WHERE tenant_id = @tenant_id AND resource_id = @resource_id AND setting_key = @key;", command =>
                {
                    AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                    AdoDatabaseDriver.Add(command, "resource_id", resourceId);
                    AdoDatabaseDriver.Add(command, "key", key);
                }, ct).ConfigureAwait(false);
                await InsertSettingAsync(connection, transaction, setting, ct).ConfigureAwait(false);
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "UPDATE " + _Table + " SET last_update_utc = @now WHERE tenant_id = @tenant_id AND id = @resource_id;", command =>
                {
                    AdoDatabaseDriver.Add(command, "now", DateTime.UtcNow);
                    AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                    AdoDatabaseDriver.Add(command, "resource_id", resourceId);
                }, ct).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Map a row to a resource (children not loaded).
        /// </summary>
        /// <param name="reader">Reader positioned on a row.</param>
        /// <returns>Resource.</returns>
        public static Resource Map(DbDataReader reader)
        {
            Resource obj = new Resource();
            obj.Id = AdoConverters.Str(reader, "id");
            obj.TenantId = AdoConverters.Str(reader, "tenant_id");
            obj.OwnerUserId = AdoConverters.NullableStr(reader, "owner_user_id");
            obj.Visibility = AdoConverters.Enum(reader, "visibility", VisibilityEnum.Tenant);
            obj.Name = AdoConverters.Str(reader, "name");
            obj.ConnectorType = AdoConverters.Str(reader, "connector_type");
            obj.DisplayName = AdoConverters.NullableStr(reader, "display_name");
            obj.Description = AdoConverters.NullableStr(reader, "description");
            obj.Endpoint = AdoConverters.NullableStr(reader, "endpoint");
            obj.Environment = AdoConverters.NullableStr(reader, "environment");
            obj.Notes = AdoConverters.NullableStr(reader, "notes");
            obj.DefaultAccessModeCeiling = AdoConverters.NullableEnum<AccessModeEnum>(reader, "default_access_mode_ceiling");
            obj.LastTestUtc = AdoConverters.NullableDate(reader, "last_test_utc");
            obj.LastTestResult = AdoConverters.NullableBool(reader, "last_test_result");
            obj.LastTestMessage = AdoConverters.NullableStr(reader, "last_test_message");
            obj.Active = AdoConverters.Bool(reader, "active");
            obj.CreatedUtc = AdoConverters.Date(reader, "created_utc");
            obj.LastUpdateUtc = AdoConverters.Date(reader, "last_update_utc");
            return obj;
        }

        #endregion

        #region Private-Methods

        private static void Bind(DbCommand command, Resource obj, bool insert)
        {
            AdoDatabaseDriver.Add(command, "id", obj.Id);
            AdoDatabaseDriver.Add(command, "tenant_id", obj.TenantId);
            AdoDatabaseDriver.Add(command, "owner_user_id", obj.OwnerUserId);
            AdoDatabaseDriver.Add(command, "visibility", obj.Visibility.ToString());
            AdoDatabaseDriver.Add(command, "name", obj.Name);
            AdoDatabaseDriver.Add(command, "connector_type", obj.ConnectorType);
            AdoDatabaseDriver.Add(command, "display_name", obj.DisplayName);
            AdoDatabaseDriver.Add(command, "description", obj.Description);
            AdoDatabaseDriver.Add(command, "endpoint", obj.Endpoint);
            AdoDatabaseDriver.Add(command, "environment", obj.Environment);
            AdoDatabaseDriver.Add(command, "notes", obj.Notes);
            AdoDatabaseDriver.Add(command, "default_access_mode_ceiling", obj.DefaultAccessModeCeiling?.ToString());
            AdoDatabaseDriver.Add(command, "last_test_utc", obj.LastTestUtc);
            AdoDatabaseDriver.Add(command, "last_test_result", obj.LastTestResult);
            AdoDatabaseDriver.Add(command, "last_test_message", obj.LastTestMessage);
            AdoDatabaseDriver.Add(command, "active", obj.Active);
            if (insert) AdoDatabaseDriver.Add(command, "created_utc", obj.CreatedUtc);
            AdoDatabaseDriver.Add(command, "last_update_utc", obj.LastUpdateUtc);
        }

        private async Task InsertChildrenAsync(DbConnection connection, DbTransaction transaction, Resource obj, CancellationToken token)
        {
            foreach (ResourceSetting setting in obj.Settings)
            {
                setting.ResourceId = obj.Id;
                setting.TenantId = obj.TenantId;
                await InsertSettingAsync(connection, transaction, setting, token).ConfigureAwait(false);
            }
            foreach (ResourceSecretBinding slot in obj.SecretBindings)
            {
                slot.ResourceId = obj.Id;
                slot.TenantId = obj.TenantId;
                await AdoDatabaseDriver.ExecuteAsync(connection, transaction,
                    "INSERT INTO " + _Slots + " (id, resource_id, tenant_id, slot_name, secret_id, secret_field_name) VALUES (@id, @resource_id, @tenant_id, @slot_name, @secret_id, @secret_field_name);",
                    command =>
                    {
                        AdoDatabaseDriver.Add(command, "id", slot.Id);
                        AdoDatabaseDriver.Add(command, "resource_id", slot.ResourceId);
                        AdoDatabaseDriver.Add(command, "tenant_id", slot.TenantId);
                        AdoDatabaseDriver.Add(command, "slot_name", slot.SlotName);
                        AdoDatabaseDriver.Add(command, "secret_id", slot.SecretId);
                        AdoDatabaseDriver.Add(command, "secret_field_name", slot.SecretFieldName);
                    }, token).ConfigureAwait(false);
            }
        }

        private async Task InsertSettingAsync(DbConnection connection, DbTransaction transaction, ResourceSetting setting, CancellationToken token)
        {
            string? stringValue = null;
            long? integerValue = null;
            bool? booleanValue = null;
            switch (setting.ValueKind)
            {
                case SettingValueKindEnum.Integer:
                    if (Int64.TryParse(setting.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)) integerValue = parsed;
                    else stringValue = setting.Value;
                    break;
                case SettingValueKindEnum.Boolean:
                    string v = (setting.Value ?? String.Empty).Trim().ToLowerInvariant();
                    if (v == "true" || v == "1" || v == "yes") booleanValue = true;
                    else if (v == "false" || v == "0" || v == "no") booleanValue = false;
                    else stringValue = setting.Value;
                    break;
                default:
                    stringValue = setting.Value;
                    break;
            }
            await AdoDatabaseDriver.ExecuteAsync(connection, transaction,
                "INSERT INTO " + _Settings + " (id, resource_id, tenant_id, setting_key, value_kind, string_value, integer_value, boolean_value) VALUES (@id, @resource_id, @tenant_id, @setting_key, @value_kind, @string_value, @integer_value, @boolean_value);",
                command =>
                {
                    AdoDatabaseDriver.Add(command, "id", setting.Id);
                    AdoDatabaseDriver.Add(command, "resource_id", setting.ResourceId);
                    AdoDatabaseDriver.Add(command, "tenant_id", setting.TenantId);
                    AdoDatabaseDriver.Add(command, "setting_key", setting.Key);
                    AdoDatabaseDriver.Add(command, "value_kind", setting.ValueKind.ToString());
                    AdoDatabaseDriver.Add(command, "string_value", stringValue);
                    AdoDatabaseDriver.Add(command, "integer_value", integerValue);
                    AdoDatabaseDriver.Add(command, "boolean_value", booleanValue);
                }, token).ConfigureAwait(false);
        }

        private async Task DeleteChildrenAsync(DbConnection connection, DbTransaction transaction, string tenantId, string resourceId, CancellationToken token)
        {
            Action<DbCommand> bind = command =>
            {
                AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
                AdoDatabaseDriver.Add(command, "resource_id", resourceId);
            };
            await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "DELETE FROM " + _Settings + " WHERE tenant_id = @tenant_id AND resource_id = @resource_id;", bind, token).ConfigureAwait(false);
            await AdoDatabaseDriver.ExecuteAsync(connection, transaction, "DELETE FROM " + _Slots + " WHERE tenant_id = @tenant_id AND resource_id = @resource_id;", bind, token).ConfigureAwait(false);
        }

        private async Task LoadChildrenAsync(List<Resource> resources, CancellationToken token)
        {
            if (resources.Count == 0) return;
            List<string> parameters = new List<string>();
            for (int i = 0; i < resources.Count; i++) parameters.Add("@r" + i);
            string inList = "(" + String.Join(", ", parameters) + ")";
            Action<DbCommand> bind = command =>
            {
                for (int i = 0; i < resources.Count; i++) AdoDatabaseDriver.Add(command, "r" + i, resources[i].Id);
            };
            List<ResourceSetting> settings = await _Driver.QueryAsync("SELECT * FROM " + _Settings + " WHERE resource_id IN " + inList + " ORDER BY setting_key ASC;", bind, MapSetting, token).ConfigureAwait(false);
            List<ResourceSecretBinding> slots = await _Driver.QueryAsync("SELECT * FROM " + _Slots + " WHERE resource_id IN " + inList + " ORDER BY slot_name ASC;", bind, MapSlot, token).ConfigureAwait(false);
            foreach (Resource resource in resources)
            {
                resource.Settings = settings.Where(s => s.ResourceId == resource.Id && s.TenantId == resource.TenantId).ToList();
                resource.SecretBindings = slots.Where(s => s.ResourceId == resource.Id && s.TenantId == resource.TenantId).ToList();
            }
        }

        private static ResourceSetting MapSetting(DbDataReader reader)
        {
            ResourceSetting obj = new ResourceSetting();
            obj.Id = AdoConverters.Str(reader, "id");
            obj.ResourceId = AdoConverters.Str(reader, "resource_id");
            obj.TenantId = AdoConverters.Str(reader, "tenant_id");
            obj.Key = AdoConverters.Str(reader, "setting_key");
            obj.ValueKind = AdoConverters.Enum(reader, "value_kind", SettingValueKindEnum.String);
            long? integerValue = AdoConverters.NullableLong(reader, "integer_value");
            bool? booleanValue = AdoConverters.NullableBool(reader, "boolean_value");
            if (integerValue.HasValue) obj.Value = integerValue.Value.ToString(CultureInfo.InvariantCulture);
            else if (booleanValue.HasValue) obj.Value = booleanValue.Value ? "true" : "false";
            else obj.Value = AdoConverters.Str(reader, "string_value");
            return obj;
        }

        private static ResourceSecretBinding MapSlot(DbDataReader reader)
        {
            ResourceSecretBinding obj = new ResourceSecretBinding();
            obj.Id = AdoConverters.Str(reader, "id");
            obj.ResourceId = AdoConverters.Str(reader, "resource_id");
            obj.TenantId = AdoConverters.Str(reader, "tenant_id");
            obj.SlotName = AdoConverters.Str(reader, "slot_name");
            obj.SecretId = AdoConverters.Str(reader, "secret_id");
            obj.SecretFieldName = AdoConverters.NullableStr(reader, "secret_field_name");
            return obj;
        }

        #endregion
    }
}
