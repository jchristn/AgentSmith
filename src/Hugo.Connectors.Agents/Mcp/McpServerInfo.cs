namespace Hugo.Connectors.Agents.Mcp
{
    using System;
    using Hugo.Connectors.Agents.Mcp.Wire;

    /// <summary>
    /// What an external MCP server reported in <c>initialize</c>: its name, version, capabilities, and instructions.
    /// Immutable after construction.
    /// </summary>
    public class McpServerInfo
    {
        #region Public-Members

        /// <summary>
        /// Server name, or empty when not reported.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Server version, or empty when not reported.
        /// </summary>
        public string Version { get; }

        /// <summary>
        /// Negotiated protocol version, or empty when not reported.
        /// </summary>
        public string ProtocolVersion { get; }

        /// <summary>
        /// The server's instructions for clients (third-party content), or empty.
        /// </summary>
        public string Instructions { get; }

        /// <summary>
        /// Whether the server declared the tools capability. True when the initialize result was not available.
        /// </summary>
        public bool HasTools { get; }

        /// <summary>
        /// Whether the server declared the resources capability. True when the initialize result was not available, so
        /// the call itself decides.
        /// </summary>
        public bool HasResources { get; }

        /// <summary>
        /// Whether the initialize result was captured.
        /// </summary>
        public bool Known { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate from an initialize result.
        /// </summary>
        /// <param name="result">Initialize result, or null when it was not available.</param>
        public McpServerInfo(McpWireInitializeResult? result)
        {
            Known = result != null;
            Name = result?.ServerInfo?.Name ?? String.Empty;
            Version = result?.ServerInfo?.Version ?? String.Empty;
            ProtocolVersion = result?.ProtocolVersion ?? String.Empty;
            Instructions = result?.Instructions ?? String.Empty;
            HasTools = result?.Capabilities == null || result.Capabilities.Tools != null;
            HasResources = result?.Capabilities == null || result.Capabilities.Resources != null;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Display text such as <c>docs-server 1.2.0</c>, or <c>(unnamed server)</c>.
        /// </summary>
        /// <returns>Text.</returns>
        public string Display()
        {
            string name = String.IsNullOrWhiteSpace(Name) ? "(unnamed server)" : TextClip.OneLine(Name);
            return String.IsNullOrWhiteSpace(Version) ? name : name + " " + TextClip.OneLine(Version);
        }

        #endregion
    }
}
