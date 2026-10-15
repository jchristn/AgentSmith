namespace Hugo.Core.Database.Ado
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Database.Interfaces;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;

    /// <summary>
    /// Provider-neutral analytics reads. Rows are fetched with only the columns analytics needs and bucketed by the
    /// caller, so the SQL stays portable across providers. Thread-safe.
    /// </summary>
    public class AdoAnalyticsMethods : IAnalyticsMethods
    {
        #region Private-Members

        private readonly AdoDatabaseDriver _Driver;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">Owning driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when driver is null.</exception>
        public AdoAnalyticsMethods(AdoDatabaseDriver driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<List<MessageUsageRow>> ListMessageUsageAsync(string tenantId, DateTime fromUtc, DateTime toUtc, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            return await _Driver.QueryAsync("SELECT created_utc, model_endpoint_id, input_tokens, output_tokens, time_to_first_token_ms, duration_ms, finish_reason FROM " + _Driver.Table("messages")
                + " WHERE tenant_id = @tenant_id AND created_utc >= @from AND created_utc < @to AND role IN ('Assistant', 'Summary') ORDER BY created_utc ASC;", command => Window(command, tenantId, fromUtc, toUtc), reader =>
            {
                MessageUsageRow row = new MessageUsageRow();
                row.CreatedUtc = AdoConverters.Date(reader, "created_utc");
                row.ModelEndpointId = AdoConverters.NullableStr(reader, "model_endpoint_id");
                row.InputTokens = AdoConverters.Long(reader, "input_tokens");
                row.OutputTokens = AdoConverters.Long(reader, "output_tokens");
                row.TimeToFirstTokenMs = AdoConverters.NullableLong(reader, "time_to_first_token_ms");
                row.DurationMs = AdoConverters.NullableLong(reader, "duration_ms");
                row.FinishReason = AdoConverters.NullableStr(reader, "finish_reason");
                return row;
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<ToolUsageRow>> ListToolUsageAsync(string tenantId, DateTime fromUtc, DateTime toUtc, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            return await _Driver.QueryAsync("SELECT created_utc, tool_name, connector_type, decision, status, duration_ms, approval_id FROM " + _Driver.Table("tool_invocations")
                + " WHERE tenant_id = @tenant_id AND created_utc >= @from AND created_utc < @to ORDER BY created_utc ASC;", command => Window(command, tenantId, fromUtc, toUtc), reader =>
            {
                ToolUsageRow row = new ToolUsageRow();
                row.CreatedUtc = AdoConverters.Date(reader, "created_utc");
                row.ToolName = AdoConverters.Str(reader, "tool_name");
                row.ConnectorType = AdoConverters.NullableStr(reader, "connector_type");
                row.Decision = AdoConverters.Enum(reader, "decision", InvocationDecisionEnum.Denied);
                row.Status = AdoConverters.Enum(reader, "status", InvocationStatusEnum.Failed);
                row.DurationMs = AdoConverters.Long(reader, "duration_ms");
                row.HadApproval = !String.IsNullOrEmpty(AdoConverters.NullableStr(reader, "approval_id"));
                return row;
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<long> CountConversationsAsync(string tenantId, DateTime fromUtc, DateTime toUtc, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            return await CountAsync("conversations", tenantId, fromUtc, toUtc, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<long> CountApprovalsAsync(string tenantId, DateTime fromUtc, DateTime toUtc, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            return await CountAsync("approvals", tenantId, fromUtc, toUtc, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private async Task<long> CountAsync(string rawTable, string tenantId, DateTime fromUtc, DateTime toUtc, CancellationToken token)
        {
            object? value = await _Driver.ScalarAsync("SELECT COUNT(*) FROM " + _Driver.Table(rawTable) + " WHERE tenant_id = @tenant_id AND created_utc >= @from AND created_utc < @to;", command => Window(command, tenantId, fromUtc, toUtc), token).ConfigureAwait(false);
            return value == null ? 0 : Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }

        private static void Window(DbCommand command, string tenantId, DateTime fromUtc, DateTime toUtc)
        {
            AdoDatabaseDriver.Add(command, "tenant_id", tenantId);
            AdoDatabaseDriver.Add(command, "from", fromUtc);
            AdoDatabaseDriver.Add(command, "to", toUtc);
        }

        #endregion
    }
}
