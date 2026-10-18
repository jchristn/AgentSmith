namespace Hugo.Benchmark
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>Public-surface document benchmark and paired local regression comparison.</summary>
    public static class Program
    {
        private static readonly JsonSerializerOptions _Json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true };

        /// <summary>Run with environment-only credentials and record complete or failed reports.</summary>
        /// <param name="args">Command and name/value options.</param><returns>0 success, 1 regression, 2 usage, 3 incomplete, 130 cancellation.</returns>
        public static async Task<int> Main(string[] args)
        {
            if (args.Length == 0) { Usage(); return 2; }
            string root = RepositoryRoot();
            Dictionary<string, string> options;
            try { options = Options(args); }
            catch (ArgumentException) { Usage(); return 2; }
            HashSet<string> allowed = new HashSet<string>(new[] { "isolated", "provider", "server-revision", "samples", "run-id", "url", "output", "endpoint-id", "baseline", "candidate", "max-ratio" }, StringComparer.Ordinal);
            if (options.Keys.Any(key => !allowed.Contains(key))) { Usage(); return 2; }
            if (args[0] == "validate")
            {
                foreach (int size in new[] { 256, 262080 }) if (Encoding.UTF8.GetByteCount(DocumentBenchmark.Fixture(size)) != size) throw new InvalidOperationException("Fixture byte count mismatch.");
                Console.WriteLine("Synthetic document fixtures have exact reproducible canonical UTF-8 byte sizes."); return 0;
            }
            if (args[0] == "compare") return Compare(options, root);
            if (args[0] != "documents" || !options.ContainsKey("isolated") || !options.ContainsKey("server-revision") || !options.ContainsKey("provider")) { Usage(); return 2; }
            string secret = Environment.GetEnvironmentVariable("HUGO_BENCHMARK_TOKEN") ?? String.Empty;
            if (secret.Length == 0) { Console.Error.WriteLine("Set HUGO_BENCHMARK_TOKEN to an authorized token for the isolated benchmark tenant."); return 2; }
            if (!Int32.TryParse(options.GetValueOrDefault("samples", "30"), out int samples) || samples < 3 || samples > 1000) { Console.Error.WriteLine("samples must be between 3 and 1000."); return 2; }
            string runId = options.GetValueOrDefault("run-id", "apc-baseline");
            if (runId.Length > 20 || runId.Length == 0 || runId.Any(c => !Char.IsAsciiLetterOrDigit(c) && c != '-')) { Console.Error.WriteLine("run-id must contain 1..20 ASCII letters, digits or hyphens."); return 2; }
            string output = Resolve(root, options.GetValueOrDefault("output", "benchmarks/results/prompt-documents.json"));
            BenchmarkReport report = new BenchmarkReport { Command = String.Join(" ", args), SourceCommit = GitRevision(root), ServerRevision = options["server-revision"], DatabaseProvider = options["provider"], OperatingSystem = RuntimeInformation.OSDescription, Architecture = RuntimeInformation.ProcessArchitecture.ToString(), LogicalProcessors = Environment.ProcessorCount, Samples = samples };
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
                int exit = 0;
                try
                {
                    using (BenchmarkClient client = new BenchmarkClient(options.GetValueOrDefault("url", "http://127.0.0.1:8894"), secret))
                    {
                        await new DocumentBenchmark(client, report, runId, options.GetValueOrDefault("endpoint-id"), samples).RunAsync(cancellation.Token).ConfigureAwait(false);
                    }
                    report.Status = "complete";
                }
                catch (OperationCanceledException) { report.Status = "cancelled"; report.Failures++; exit = 130; }
                catch (Exception e) when (e is HttpRequestException || e is InvalidOperationException || e is IOException)
                {
                    report.Status = "incomplete"; report.Failures++; exit = 3; Console.Error.WriteLine("Incomplete benchmark: " + e.Message);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                await File.WriteAllTextAsync(output, JsonSerializer.Serialize(report, _Json), CancellationToken.None).ConfigureAwait(false);
                Console.WriteLine("Report: " + output + " (" + report.Status + ")");
                return exit;
            }
        }

        private static int Compare(Dictionary<string, string> options, string root)
        {
            if (!options.TryGetValue("baseline", out string? baselinePath) || !options.TryGetValue("candidate", out string? candidatePath)) { Usage(); return 2; }
            BenchmarkReport baseline = JsonSerializer.Deserialize<BenchmarkReport>(File.ReadAllText(Resolve(root, baselinePath)), _Json)!;
            BenchmarkReport candidate = JsonSerializer.Deserialize<BenchmarkReport>(File.ReadAllText(Resolve(root, candidatePath)), _Json)!;
            if (baseline.Status != "complete" || candidate.Status != "complete" || baseline.Failures != 0 || candidate.Failures != 0 || baseline.OperatingSystem != candidate.OperatingSystem || baseline.Architecture != candidate.Architecture || baseline.LogicalProcessors != candidate.LogicalProcessors || baseline.DatabaseProvider != candidate.DatabaseProvider || baseline.Samples != candidate.Samples) { Console.Error.WriteLine("Reports are incomplete or environment/sample metadata differs; incomparable."); return 3; }
            if (!Double.TryParse(options.GetValueOrDefault("max-ratio", "1.20"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double ratio) || !Double.IsFinite(ratio) || ratio < 1 || ratio > 10) { Usage(); return 2; }
            if (baseline.Measurements.Count == 0 || baseline.Measurements.Count != candidate.Measurements.Count) { Console.Error.WriteLine("Measurement sets differ or are empty; incomparable."); return 3; }
            int regressions = 0;
            foreach (BenchmarkMeasurement before in baseline.Measurements.Where(x => x.Operation != "ingest-and-attach"))
            {
                BenchmarkMeasurement? after = candidate.Measurements.SingleOrDefault(x => x.Case == before.Case && x.Operation == before.Operation && x.Cache == before.Cache);
                if (after == null || after.AttachmentCount != before.AttachmentCount || after.DocumentBytes != before.DocumentBytes) return 3;
                if (before.LatencyMs.Count != after.LatencyMs.Count) return 3;
                double[] differences = before.LatencyMs.Zip(after.LatencyMs, (a, b) => b - a).ToArray();
                Random random = new Random(1729);
                double[] bootstrap = new double[2000];
                for (int i = 0; i < bootstrap.Length; i++) { double sum = 0; for (int j = 0; j < differences.Length; j++) sum += differences[random.Next(differences.Length)]; bootstrap[i] = sum / differences.Length; }
                Array.Sort(bootstrap);
                bool regression = after.P50Ms > before.P50Ms * ratio && after.P50Ms - before.P50Ms > 1 && bootstrap[49] > 0;
                if (regression) { regressions++; Console.Error.WriteLine("Regression: " + before.Case + " " + before.Operation + " " + before.Cache); }
            }
            Console.WriteLine("Paired latency comparison: " + regressions + " regressions. Tolerance " + ratio + "x and >1 ms, 95% fixed-seed bootstrap mean-difference interval.");
            return regressions == 0 ? 0 : 1;
        }

        private static Dictionary<string, string> Options(string[] args)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 1; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Expected --name value.");
                string key = args[i].Substring(2);
                values[key] = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : "true";
            }
            return values;
        }

        private static string RepositoryRoot()
        {
            DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null) { if (Directory.Exists(Path.Combine(directory.FullName, ".git")) && Directory.Exists(Path.Combine(directory.FullName, "benchmarks"))) return directory.FullName; directory = directory.Parent; }
            throw new InvalidOperationException("Run from a Hugo checkout containing .git and benchmarks.");
        }
        private static string Resolve(string root, string path) { return Path.IsPathRooted(path) ? path : Path.Combine(root, path); }
        private static string GitRevision(string root)
        {
            using (Process process = new Process { StartInfo = new ProcessStartInfo("git", "rev-parse HEAD") { WorkingDirectory = root, RedirectStandardOutput = true, UseShellExecute = false } })
            {
                process.Start(); string revision = process.StandardOutput.ReadToEnd().Trim(); process.WaitForExit(); return revision;
            }
        }
        private static void Usage() { Console.WriteLine("Hugo.Benchmark validate | documents --isolated --provider sqlite|postgresql --server-revision SOURCE [--url http://127.0.0.1:8894 --samples 30 --run-id apc-baseline --endpoint-id ID --output benchmarks/results/run.json] | compare --baseline PATH --candidate PATH [--max-ratio 1.20]. Credentials: HUGO_BENCHMARK_TOKEN only."); }
    }
}
