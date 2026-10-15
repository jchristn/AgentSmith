namespace Hugo.Sdk.Clients
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Sdk.Http;
    using Hugo.Sdk.Requests;
    using Hugo.Sdk.Responses;

    /// <summary>
    /// Server settings (/v1.0/api/settings). System administrators only.
    /// </summary>
    public class SettingsClient : ClientBase
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="transport">Transport.</param>
        public SettingsClient(HugoTransport transport) : base(transport)
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Read settings with source, apply mode, and editability metadata. Secrets are masked.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Settings.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<SettingsView> GetAsync(CancellationToken token = default)
        {
            return GetAsync<SettingsView>("/v1.0/api/settings", null, token);
        }

        /// <summary>
        /// Update settings by path.
        /// </summary>
        /// <param name="values">Paths and new values.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Save result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when values is null.</exception>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<SettingsSaveResponse> UpdateAsync(List<SettingValueUpdate> values, CancellationToken token = default)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            SettingsUpdateRequest request = new SettingsUpdateRequest();
            request.Values = values;
            return PutAsync<SettingsSaveResponse>("/v1.0/api/settings", request, token);
        }

        #endregion
    }
}
