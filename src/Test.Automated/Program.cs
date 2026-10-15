namespace Test.Automated
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Test.Shared;
    using Touchstone.Cli;
    using Touchstone.Core;

    /// <summary>
    /// Console runner for the Hugo shared suites.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Entry point. Supports --results &lt;path&gt; to export JSON results, --providers sqlite,postgresql, and
        /// --suites prefix1,prefix2 to run only suites whose identifier starts with one of the prefixes.
        /// </summary>
        /// <param name="args">Arguments.</param>
        /// <returns>Exit code: 0 when every test passed.</returns>
        public static async Task<int> Main(string[] args)
        {
            string? resultsPath = null;
            if (args.Contains("--stub-llm")) return await StubLlmAsync().ConfigureAwait(false);
            List<string> prefixes = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--results" && i + 1 < args.Length) resultsPath = args[i + 1];
                if (args[i] == "--providers" && i + 1 < args.Length) Environment.SetEnvironmentVariable("HUGO_TEST_PROVIDERS", args[i + 1]);
                if (args[i] == "--suites" && i + 1 < args.Length) prefixes.AddRange(args[i + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }

            IReadOnlyList<TestSuiteDescriptor> suites = HugoSuites.All;
            if (prefixes.Count > 0) suites = suites.Where(s => prefixes.Any(p => s.SuiteId.StartsWith(p, StringComparison.OrdinalIgnoreCase))).ToList();
            return await ConsoleRunner.RunAsync(suites, resultsPath: resultsPath).ConfigureAwait(false);
        }

        private static async Task<int> StubLlmAsync()
        {
            // Live checks (scripts/smoke-chat.sh --stub): an OpenAI-compatible stub that answers as Agent Smith until stopped.
            using (Test.Shared.Agent.StubLlm.StubLlmServer stub = new Test.Shared.Agent.StubLlm.StubLlmServer())
            using (System.Threading.CancellationTokenSource stop = new System.Threading.CancellationTokenSource())
            {
                stub.Script.Responder = request => request.Body.Contains("Write a short title", StringComparison.Ordinal)
                    ? Test.Shared.Agent.StubLlm.StubLlmResponse.Message("Greetings from Agent Smith")
                    : null;
                stub.Script.DefaultResponse = Test.Shared.Agent.StubLlm.StubLlmResponse.Message("Good evening, Mr. Anderson. Agent Smith is listening.");
                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true;
                    stop.Cancel();
                };
                Console.WriteLine("stub-llm " + stub.BaseUrl + " model " + stub.ModelName);
                try
                {
                    await Task.Delay(System.Threading.Timeout.Infinite, stop.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                return 0;
            }
        }
    }
}
