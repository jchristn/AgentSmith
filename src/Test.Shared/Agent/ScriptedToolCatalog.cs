namespace Test.Shared.Agent
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Agent;

    /// <summary>
    /// Fixed tool catalog for tests. Thread-safe for reads.
    /// </summary>
    public class ScriptedToolCatalog : IAgentToolCatalog
    {
        #region Public-Members

        /// <summary>
        /// Tools returned.
        /// </summary>
        public List<AgentToolDefinition> Tools { get; } = new List<AgentToolDefinition>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Add a family tool with a <c>resource</c> enum.
        /// </summary>
        /// <param name="name">Tool name.</param>
        /// <param name="description">Description.</param>
        /// <param name="knownEffect">Effect known in advance, or null.</param>
        /// <param name="resources">Resource aliases for the enum.</param>
        /// <returns>This catalog.</returns>
        public ScriptedToolCatalog Add(string name, string description, EffectEnum? knownEffect, params string[] resources)
        {
            string enumJson = String.Join(",", (resources.Length == 0 ? new string[] { "default" } : resources).Select(r => "\"" + r + "\""));
            Tools.Add(new AgentToolDefinition
            {
                Name = name,
                Description = description,
                KnownEffect = knownEffect,
                Family = name.Split('_')[0],
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"resource\":{\"type\":\"string\",\"enum\":[" + enumJson + "]},\"command\":{\"type\":\"string\"}},\"required\":[\"resource\"]}"
            });
            return this;
        }

        /// <summary>
        /// Add a Hugo meta tool.
        /// </summary>
        /// <param name="name">Tool name.</param>
        /// <param name="description">Description.</param>
        /// <returns>This catalog.</returns>
        public ScriptedToolCatalog AddMeta(string name, string description)
        {
            Tools.Add(new AgentToolDefinition { Name = name, Description = description, IsMetaTool = true, KnownEffect = EffectEnum.Read });
            return this;
        }

        /// <inheritdoc />
        public Task<List<AgentToolDefinition>> GetToolsAsync(AgentTurnRequest turn, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(turn);
            return Task.FromResult(Tools.ToList());
        }

        #endregion
    }
}
