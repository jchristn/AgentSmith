namespace Hugo.Server.Services
{
    using System;
    using System.Collections.Generic;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Agents.A2A;
    using Hugo.Connectors.Agents.Mcp;
    using Hugo.Connectors.Aws;
    using Hugo.Connectors.Docker;
    using Hugo.Connectors.Http;
    using Hugo.Connectors.Kubernetes;
    using Hugo.Connectors.Logging;
    using Hugo.Connectors.Observability;
    using Hugo.Connectors.Redis;
    using Hugo.Connectors.Sql;
    using Hugo.Connectors.Ssh;
    using Hugo.Connectors.Storage;
    using Hugo.Core.Connectors;

    /// <summary>
    /// Explicit registration of the built-in connectors (plan section 8.2, P3.3). No reflection scanning is used for
    /// built-ins, so trimming and startup stay predictable. Add a connector here when its project lands. Thread-safe.
    /// </summary>
    public static class ConnectorRegistrations
    {
        #region Public-Methods

        /// <summary>
        /// New instances of every built-in connector. Wave 1: ssh, s3, azure-blob, gcs, sqlite, mysql, sqlserver,
        /// postgresql, papertrail, solarwinds-observability. Wave 2: redis, http, prometheus, loki, elasticsearch, aws,
        /// kubernetes, docker, mcp, a2a-agent.
        /// </summary>
        /// <param name="allowMcpStdio">Server gate for external MCP servers over stdio (Connectors.McpAllowStdio). Default false.</param>
        /// <returns>Connectors.</returns>
        public static List<IConnector> BuiltIns(bool allowMcpStdio = false)
        {
            return new List<IConnector>
            {
                new SshConnector(),
                new S3Connector(),
                new AzureBlobConnector(),
                new GcsConnector(),
                new SqliteConnector(),
                new MysqlConnector(),
                new SqlServerConnector(),
                new PostgresqlConnector(),
                new PapertrailConnector(),
                new SolarWindsObservabilityConnector(),
                new RedisConnector(),
                new HttpConnector(),
                new PrometheusConnector(),
                new LokiConnector(),
                new ElasticsearchConnector(),
                new AwsConnector(),
                new KubernetesConnector(),
                new DockerConnector(),
                new McpConnector(new McpConnectorOptions { AllowStdio = allowMcpStdio }),
                new A2AAgentConnector()
            };
        }

        /// <summary>
        /// Register every built-in connector.
        /// </summary>
        /// <param name="registry">Registry.</param>
        /// <param name="allowMcpStdio">Server gate for external MCP servers over stdio. Default false.</param>
        /// <exception cref="ArgumentNullException">Thrown when registry is null.</exception>
        public static void RegisterBuiltIns(ConnectorRegistry registry, bool allowMcpStdio = false)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            foreach (IConnector connector in BuiltIns(allowMcpStdio)) registry.Register(connector);
        }

        #endregion
    }
}
