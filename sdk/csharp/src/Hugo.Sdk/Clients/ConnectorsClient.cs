namespace Hugo.Sdk.Clients
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Sdk.Http;
    using Hugo.Sdk.Models;

    /// <summary>
    /// Connector descriptors with settings schema, secret slots, and tools (/v1.0/api/connectors). Preview: verified
    /// against the server when the Phase 3 routes land.
    /// </summary>
    public class ConnectorsClient : ClientBase
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="transport">Transport.</param>
        public ConnectorsClient(HugoTransport transport) : base(transport)
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// List every loaded connector.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Connectors.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<List<Connector>> ListAsync(CancellationToken token = default)
        {
            return GetAsync<List<Connector>>("/v1.0/api/connectors", null, token);
        }

        /// <summary>
        /// Read one connector descriptor.
        /// </summary>
        /// <param name="type">Connector type, for example ssh.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Connector.</returns>
        /// <exception cref="HugoException">Thrown on a non-success status.</exception>
        public Task<Connector> GetAsync(string type, CancellationToken token = default)
        {
            return GetAsync<Connector>("/v1.0/api/connectors/" + Segment(type), null, token);
        }

        #endregion
    }
}
