namespace Test.Shared.Agent
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Agent;
    using Hugo.Core.Inference;
    using Hugo.Server.Services;
    using Test.Shared.Agent.StubLlm;
    using Touchstone.Core;

    /// <summary>
    /// P13.35: tool retrieval on a profile with more than 100 tools. The catalog is every distinct tool of the built-in
    /// wave 1 and wave 2 connectors (real names and descriptions) plus the meta tools; the stub LLM embeds with the
    /// deterministic feature-hashing embedder. The case checks the top-K shape, that meta tools are always kept, the hit
    /// rate over representative requests, and selection latency, and prints the measurements.
    /// </summary>
    public static class ToolRetrievalScaleCases
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public static readonly string Suite = "agent.retrieval-scale";

        /// <summary>
        /// The last measurement line printed by the scale case, or null before it ran.
        /// </summary>
        public static string? LastReport { get; private set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the cases.
        /// </summary>
        /// <returns>Cases.</returns>
        public static List<TestCaseDescriptor> Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            cases.Add(new TestCaseDescriptor(Suite, "over-100-tools", "Retrieval over more than 100 real connector tools keeps meta plus top K, finds the expected tool for representative requests, and stays fast", OverHundredToolsAsync));
            return cases;
        }

        #endregion

        #region Private-Methods

        private static async Task OverHundredToolsAsync(CancellationToken token)
        {
            using (AgentTestRig rig = new AgentTestRig())
            {
                List<AgentToolDefinition> tools = Catalog();
                List<AgentToolDefinition> meta = tools.Where(t => t.IsMetaTool).ToList();
                int nonMeta = tools.Count - meta.Count;
                AgentAssert.True(nonMeta > 100, "more than 100 non-meta tools: " + nonMeta);

                AgentRuntimeSettings defaults = new AgentRuntimeSettings();
                int threshold = defaults.ToolRetrievalThreshold;
                int topK = defaults.ToolRetrievalTopK;
                ToolRetriever retriever = new ToolRetriever(rig.Gateway, new ToolEmbeddingCache());
                ModelEndpointConfig endpoint = rig.Stub.EmbeddingEndpoint();

                List<string[]> probes = new List<string[]>
                {
                    new string[] { "Run a SQL query to count the orders placed yesterday", "sql_query" },
                    new string[] { "Show the last 100 lines of the nginx error log file on web-01", "ssh_tail_file" },
                    new string[] { "Restart the checkout deployment rollout in Kubernetes", "k8s_rollout_restart" },
                    new string[] { "List the objects in the backups storage bucket", "storage_list" },
                    new string[] { "What is the TTL of the session key in Redis", "redis_ttl" },
                    new string[] { "Show the running Docker containers", "docker_ps" },
                    new string[] { "Run a PromQL query for the HTTP request rate in Prometheus", "prom_query" },
                    new string[] { "Describe the EC2 instances in our AWS account", "aws_ec2_describe_instances" },
                    new string[] { "Search Elasticsearch for documents that mention timeout errors", "es_search" },
                    new string[] { "Delegate this research task to the remote A2A agent", "a2a_delegate" },
                    new string[] { "List the tools the external MCP server offers", "mcp_list_tools" },
                    new string[] { "Send an HTTP GET request to the status API endpoint", "http_request" }
                };

                List<double> warmMs = new List<double>();
                double coldMs = 0;
                int hits = 0;
                int topFive = 0;
                List<int> ranks = new List<int>();
                List<string> misses = new List<string>();
                for (int i = 0; i < probes.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    Stopwatch watch = Stopwatch.StartNew();
                    ToolRetrievalResult result = await retriever.SelectAsync(tools, probes[i][0], endpoint, threshold, topK, token).ConfigureAwait(false);
                    watch.Stop();
                    if (i == 0) coldMs = watch.Elapsed.TotalMilliseconds;
                    else warmMs.Add(watch.Elapsed.TotalMilliseconds);

                    AgentAssert.True(result.UsedRetrieval, "retrieval used for: " + probes[i][0] + " (" + result.Reason + ")");
                    AgentAssert.Equal(meta.Count + topK, result.Tools.Count, "meta tools plus top K");
                    foreach (AgentToolDefinition m in meta)
                    {
                        AgentAssert.True(result.Tools.Any(t => t.Name == m.Name), "meta tool " + m.Name + " is always kept");
                    }
                    AgentAssert.Equal(topK, result.Scores.Count, "scores for the selected non-meta tools");
                    int position = result.Tools.FindIndex(t => t.Name == probes[i][1]);
                    if (position >= 0)
                    {
                        hits++;
                        int rank = position - meta.Count + 1;
                        ranks.Add(rank);
                        if (rank <= 5) topFive++;
                    }
                    else
                    {
                        misses.Add(probes[i][1]);
                    }
                }

                double hitRate = (double)hits / probes.Count;
                double warmAverage = warmMs.Average();
                double warmMax = warmMs.Max();
                LastReport = String.Format(CultureInfo.InvariantCulture,
                    "[P13.35 tool retrieval] tools={0} (non-meta {1}, meta {2}) threshold={3} topK={4} hitRate={5}/{6} ({7:P0}) top5={12}/{6} meanRank={13:F1} coldMs={8:F1} warmAvgMs={9:F1} warmMaxMs={10:F1} misses={11}",
                    tools.Count, nonMeta, meta.Count, threshold, topK, hits, probes.Count, hitRate, coldMs, warmAverage, warmMax, misses.Count == 0 ? "none" : String.Join(",", misses), topFive, ranks.Count == 0 ? 0.0 : ranks.Average());
                Console.WriteLine(LastReport);

                AgentAssert.True(hitRate >= 0.8, "hit rate at least 80 percent: " + LastReport);
                AgentAssert.True(coldMs < 10000, "cold selection (embeds every tool) under 10 s: " + LastReport);
                AgentAssert.True(warmMax < 2000, "warm selection (query only, cached tool vectors) under 2 s: " + LastReport);
                AgentAssert.Equal(1 + nonMeta, rig.Stub.EmbeddingRequests[0].Inputs.Count, "the first selection embeds the query plus every tool");
                AgentAssert.True(rig.Stub.EmbeddingRequests.Skip(1).All(r => r.Inputs.Count == 1), "later selections embed only the query");
            }
        }

        private static List<AgentToolDefinition> Catalog()
        {
            List<AgentToolDefinition> tools = new List<AgentToolDefinition>();
            tools.Add(Meta("hugo_list_resources", "List the resources bound to this profile with their aliases, families, and access modes."));
            tools.Add(Meta("hugo_describe_resource", "Return the full guidance, tools, and limits for one bound resource."));
            tools.Add(Meta("hugo_await_approval", "Wait for an operator decision on a pending approval and return the outcome."));
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (IConnector connector in ConnectorRegistrations.BuiltIns())
            {
                foreach (IConnectorTool tool in connector.Tools)
                {
                    if (!seen.Add(tool.Descriptor.Name)) continue;
                    AgentToolDefinition definition = new AgentToolDefinition();
                    definition.Name = tool.Descriptor.Name;
                    definition.Description = tool.Descriptor.Description;
                    definition.InputSchemaJson = tool.Descriptor.InputSchemaJson;
                    definition.Family = connector.Descriptor.Family;
                    definition.KnownEffect = tool.Descriptor.ClassifiedPerCall ? null : tool.Descriptor.BaseEffect;
                    tools.Add(definition);
                }
            }
            return tools;
        }

        private static AgentToolDefinition Meta(string name, string description)
        {
            AgentToolDefinition definition = new AgentToolDefinition();
            definition.Name = name;
            definition.Description = description;
            definition.IsMetaTool = true;
            definition.KnownEffect = EffectEnum.Read;
            return definition;
        }

        #endregion
    }
}
