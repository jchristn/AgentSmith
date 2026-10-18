namespace Hugo.Benchmark
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Net.Http;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Black-box prompt document correctness and indicative latency fixture.</summary>
    public class DocumentBenchmark
    {
        private readonly BenchmarkClient _Client;
        private readonly BenchmarkReport _Report;
        private readonly string _RunId;
        private readonly string? _Endpoint;
        private readonly int _Samples;

        /// <summary>Configure isolated fixture names and sampling.</summary>
        /// <param name="client">Public API client.</param><param name="report">Report sink.</param><param name="runId">Deterministic fixture namespace.</param><param name="endpoint">Optional existing tool-capable endpoint.</param><param name="samples">Measured samples per operation.</param>
        public DocumentBenchmark(BenchmarkClient client, BenchmarkReport report, string runId, string? endpoint, int samples)
        {
            _Client = client; _Report = report; _RunId = runId; _Endpoint = endpoint; _Samples = samples;
        }

        /// <summary>Run zero-document baseline plus 1/16/32 attachment fixtures in both modes.</summary>
        /// <param name="token">Cancellation.</param>
        public async Task RunAsync(CancellationToken token)
        {
            await ScenarioAsync(0, "Reference", 256, "baseline", token).ConfigureAwait(false);
            foreach (int count in new[] { 1, 16, 32 })
            {
                await ScenarioAsync(count, "Reference", 256, "small", token).ConfigureAwait(false);
                await ScenarioAsync(count, "Reference", 262080, "near-limit", token).ConfigureAwait(false);
                await ScenarioAsync(count, "Inline", 256, "small", token).ConfigureAwait(false);
                BenchmarkMeasurement small = _Report.Measurements.Single(m => m.Case == "reference-small-" + count && m.Operation == "composition" && m.Cache == "warm");
                BenchmarkMeasurement large = _Report.Measurements.Single(m => m.Case == "reference-near-limit-" + count && m.Operation == "composition" && m.Cache == "warm");
                if (Math.Abs(large.PromptBytes - small.PromptBytes) > count * 128 + 512) throw new InvalidOperationException("Reference prompt size grew with document body length beyond bounded metadata differences.");
            }
        }

        private async Task ScenarioAsync(int count, string mode, int bytes, string size, CancellationToken token)
        {
            string label = mode.ToLowerInvariant() + "-" + size + "-" + count;
            string profileId = String.Empty;
            List<ApiRecord> documents = new List<ApiRecord>();
            string markdown = Fixture(bytes);
            try
            {
                ApiRecord profile = await _Client.CallAsync(HttpMethod.Post, "/v1.0/api/agent-profiles", new { name = "bench-docs-" + _RunId + "-" + label, displayName = "Synthetic document benchmark", inferenceEndpointId = _Endpoint, exposeMcp = true }, token).ConfigureAwait(false);
                profileId = profile.Id;
                long set = 0;
                Stopwatch ingest = Stopwatch.StartNew();
                for (int i = 0; i < count; i++)
                {
                    ApiRecord document = await _Client.CallAsync(HttpMethod.Post, "/v1.0/api/prompt-documents", new { name = "BENCH_" + _RunId + "_" + label + "_" + i + ".md", description = "Synthetic schema reference. Read before interpreting benchmark events.", markdown, visibility = "Tenant", active = true }, token).ConfigureAwait(false);
                    documents.Add(document);
                    ApiRecord attachment = await _Client.CallAsync(HttpMethod.Post, "/v1.0/api/agent-profiles/" + profileId + "/documents", new { expectedDocumentSetVersion = set, documentId = document.Id, deliveryMode = mode, revisionPolicy = "Latest", displayOrder = i, enabled = true, allowPublicAccess = false }, token).ConfigureAwait(false);
                    set = attachment.DocumentSetVersion;
                }
                ingest.Stop();
                _Report.Measurements.Add(Measurement(label, "ingest-and-attach", "none", count, bytes, null, new List<double> { ingest.Elapsed.TotalMilliseconds }));
                List<double> cold = new List<double>(), warm = new List<double>(), reads = new List<double>();
                ApiRecord? last = null;
                for (int sample = 0; sample < _Samples; sample++)
                {
                    if (documents.Count > 0)
                    {
                        ApiRecord first = documents[0];
                        documents[0] = await _Client.CallAsync(HttpMethod.Put, "/v1.0/api/prompt-documents/" + first.Id, new { expectedVersion = first.Version, description = "Synthetic schema reference. Read before interpreting benchmark events. Sample " + sample }, token).ConfigureAwait(false);
                    }
                    Stopwatch clock = Stopwatch.StartNew();
                    last = await PreviewAsync(profileId, token).ConfigureAwait(false);
                    clock.Stop(); cold.Add(clock.Elapsed.TotalMilliseconds);
                    ValidatePreview(last, mode, count, markdown);
                    clock.Restart();
                    ApiRecord warmed = await PreviewAsync(profileId, token).ConfigureAwait(false);
                    clock.Stop(); warm.Add(clock.Elapsed.TotalMilliseconds);
                    if (warmed.CompiledText != last.CompiledText) throw new InvalidOperationException("Warm preview changed without a mutation.");
                    if (documents.Count > 0)
                    {
                        clock.Restart();
                        ApiRecord read = await _Client.CallAsync(HttpMethod.Get, "/v1.0/api/prompt-documents/" + documents[0].Id + "/revisions/" + documents[0].CurrentRevision, null, token).ConfigureAwait(false);
                        clock.Stop(); reads.Add(clock.Elapsed.TotalMilliseconds);
                        if (read.Markdown != markdown || read.ContentBytes != Encoding.UTF8.GetByteCount(markdown) || read.ContentHash != Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(markdown))).ToLowerInvariant()) throw new InvalidOperationException("Management read did not preserve complete canonical content and checksum.");
                    }
                }
                _Report.Measurements.Add(Measurement(label, "composition", count > 0 ? "invalidated-by-revision" : "first-call-pair", count, bytes, last, cold));
                _Report.Measurements.Add(Measurement(label, "composition", "warm", count, bytes, last, warm));
                if (reads.Count > 0) _Report.Measurements.Add(Measurement(label, "management-full-revision-read", "not-applicable", count, bytes, last, reads));
                Console.WriteLine(label + ": " + _Samples + " complete composition/read samples");
            }
            finally
            {
                if (profileId.Length > 0) await _Client.CallAsync(HttpMethod.Delete, "/v1.0/api/agent-profiles/" + profileId, null, CancellationToken.None).ConfigureAwait(false);
                foreach (ApiRecord document in documents)
                {
                    ApiRecord latest = await _Client.CallAsync(HttpMethod.Get, "/v1.0/api/prompt-documents/" + document.Id, null, CancellationToken.None).ConfigureAwait(false);
                    await _Client.CallAsync(HttpMethod.Delete, "/v1.0/api/prompt-documents/" + document.Id + "?expectedVersion=" + latest.Version, null, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        private Task<ApiRecord> PreviewAsync(string profileId, CancellationToken token)
        {
            return _Client.CallAsync(HttpMethod.Post, "/v1.0/api/agent-profiles/" + profileId + "/documents/preview", new { audience = "Authenticated" }, token);
        }

        private static void ValidatePreview(ApiRecord result, string mode, int count, string markdown)
        {
            if (result.Issues.Count > 0) throw new InvalidOperationException("Composition reported validation issues: " + String.Join(",", result.Issues.Select(i => i.Code)) + "; measurements are incomparable.");
            if (count == 0) return;
            bool hasBody = result.CompiledText.Contains(markdown, StringComparison.Ordinal);
            if ((mode == "Inline") != hasBody) throw new InvalidOperationException("Inline/Reference body visibility contract failed.");
            if (mode == "Reference" && (result.CompiledText.Contains("BENCHMARK_DOCUMENT_BODY_MARKER", StringComparison.Ordinal) || result.CompiledText.Contains("# Synthetic benchmark schema", StringComparison.Ordinal))) throw new InvalidOperationException("Reference manifest unexpectedly grew with document bodies.");
        }

        private static BenchmarkMeasurement Measurement(string label, string operation, string cache, int count, int bytes, ApiRecord? result, List<double> samples)
        {
            double[] sorted = samples.OrderBy(x => x).ToArray();
            return new BenchmarkMeasurement { Case = label, Operation = operation, Cache = cache, AttachmentCount = count, DocumentBytes = count == 0 ? 0 : bytes, PromptBytes = result == null ? 0 : Encoding.UTF8.GetByteCount(result.CompiledText), EstimatedTokens = result?.EstimatedTokens ?? 0, Successes = samples.Count, P50Ms = Percentile(sorted, 0.5), P95Ms = Percentile(sorted, 0.95), MeanMs = samples.Average(), MaxMs = samples.Max(), LatencyMs = samples };
        }

        private static double Percentile(double[] sorted, double fraction) { return sorted[Math.Clamp((int)Math.Ceiling(sorted.Length * fraction) - 1, 0, sorted.Length - 1)]; }

        /// <summary>Build deterministic ASCII synthetic Markdown with an exact UTF-8 size.</summary>
        /// <param name="bytes">Size, at least 128.</param><returns>Canonical text.</returns>
        public static string Fixture(int bytes)
        {
            string prefix = "# Synthetic benchmark schema\n\nBENCHMARK_DOCUMENT_BODY_MARKER\n\nFields: event, severity, operationId, durationMs.\n\n";
            if (bytes < 128) throw new ArgumentOutOfRangeException(nameof(bytes));
            return prefix + new string('x', bytes - prefix.Length - 1) + "\n";
        }
    }
}
