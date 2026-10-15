namespace Hugo.Core.Agent
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Inference;

    /// <summary>
    /// Tool retrieval by embeddings (plan 13.2, P6.11). When a profile has more non-meta tools than the threshold
    /// and an embedding endpoint, the user message is embedded and the top-K tools by cosine similarity to their
    /// embedded descriptions are kept; meta tools are always kept. Any failure falls back to every tool.
    /// Thread-safe.
    /// </summary>
    public class ToolRetriever
    {
        #region Private-Members

        private readonly IModelGateway _Gateway;
        private readonly ToolEmbeddingCache _Cache;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="gateway">Model gateway.</param>
        /// <param name="cache">Embedding cache.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public ToolRetriever(IModelGateway gateway, ToolEmbeddingCache cache)
        {
            _Gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _Cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Cosine similarity of two vectors; 0 when either is empty, zero, or the lengths differ.
        /// </summary>
        /// <param name="a">Vector a.</param>
        /// <param name="b">Vector b.</param>
        /// <returns>Similarity in [-1, 1].</returns>
        public static double Cosine(float[] a, float[] b)
        {
            if (a == null || b == null || a.Length == 0 || a.Length != b.Length) return 0;
            double dot = 0;
            double na = 0;
            double nb = 0;
            for (int i = 0; i < a.Length; i++)
            {
                dot += (double)a[i] * b[i];
                na += (double)a[i] * a[i];
                nb += (double)b[i] * b[i];
            }
            if (na <= 0 || nb <= 0) return 0;
            return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
        }

        /// <summary>
        /// Select tools for a query.
        /// </summary>
        /// <param name="tools">All tools.</param>
        /// <param name="query">The user message.</param>
        /// <param name="embeddingEndpoint">Embedding endpoint, or null to offer every tool.</param>
        /// <param name="threshold">Retrieval runs only above this many non-meta tools.</param>
        /// <param name="topK">Non-meta tools to keep.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Selection. Never null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when tools is null.</exception>
        /// <exception cref="OperationCanceledException">Thrown when cancelled.</exception>
        public async Task<ToolRetrievalResult> SelectAsync(List<AgentToolDefinition> tools, string query, ModelEndpointConfig? embeddingEndpoint, int threshold, int topK, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(tools);
            ToolRetrievalResult all = new ToolRetrievalResult { Tools = tools.ToList() };
            List<AgentToolDefinition> meta = tools.Where(t => t.IsMetaTool).ToList();
            List<AgentToolDefinition> candidates = tools.Where(t => !t.IsMetaTool).ToList();
            if (candidates.Count <= threshold)
            {
                all.Reason = "below_threshold";
                return all;
            }
            if (embeddingEndpoint == null)
            {
                all.Reason = "no_embedding_endpoint";
                return all;
            }
            if (String.IsNullOrWhiteSpace(query))
            {
                all.Reason = "empty_query";
                return all;
            }

            try
            {
                List<string> keys = candidates.Select(t => ToolEmbeddingCache.KeyFor(embeddingEndpoint, t)).ToList();
                List<int> missing = new List<int>();
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (!_Cache.TryGet(keys[i], out float[]? _)) missing.Add(i);
                }

                List<string> inputs = new List<string> { query };
                inputs.AddRange(missing.Select(i => ToolEmbeddingCache.TextFor(candidates[i])));
                ModelEmbedResult embedded = await _Gateway.EmbedAsync(new ModelEmbedCall { Endpoint = embeddingEndpoint, Inputs = inputs }, token).ConfigureAwait(false);
                float[] queryVector = embedded.Vectors[0];
                for (int m = 0; m < missing.Count; m++) _Cache.Set(keys[missing[m]], embedded.Vectors[m + 1]);

                List<ToolScore> scored = new List<ToolScore>();
                for (int i = 0; i < candidates.Count; i++)
                {
                    float[]? vector;
                    if (!_Cache.TryGet(keys[i], out vector) || vector == null) continue;
                    scored.Add(new ToolScore(candidates[i], Cosine(queryVector, vector), i));
                }

                List<ToolScore> top = scored.OrderByDescending(s => s.Score).ThenBy(s => s.Order).Take(topK).ToList();
                ToolRetrievalResult result = new ToolRetrievalResult { UsedRetrieval = true };
                result.Tools.AddRange(meta);
                foreach (ToolScore s in top)
                {
                    result.Tools.Add(s.Tool);
                    result.Scores[s.Tool.Name] = s.Score;
                }
                return result;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                all.Reason = "retrieval_failed: " + e.Message;
                return all;
            }
        }

        #endregion
    }
}
