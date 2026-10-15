namespace Test.Shared.Cli
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;
    using Hugo.Cli.Mcp;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;

    /// <summary>
    /// Launches the built <c>hugo</c> tool as a child process and acts as an MCP client over its real standard input
    /// and output (P7.21): handshake, an event-stream tool call, a list, and a clean exit on end of input.
    /// </summary>
    public static class CliProcessCases
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string SuiteId = "cli.process";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the cases.
        /// </summary>
        /// <returns>Cases.</returns>
        public static List<TestCaseDescriptor> Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            cases.Add(Case("stdio-round-trip", "An MCP client drives hugo mcp stdio over real stdio pipes and the tool exits 0 on EOF", StdioRoundTrip));
            cases.Add(Case("exit-codes", "The tool process exits 0 for --version and 2 for an unknown command", ExitCodes));
            return cases;
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string id, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, body);
        }

        private static ProcessStartInfo StartInfo(CliTestEnvironment env, params string[] args)
        {
            string dll = typeof(Hugo.Cli.Program).Assembly.Location;
            string? host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            if (String.IsNullOrEmpty(host) || !File.Exists(host)) host = "dotnet";
            ProcessStartInfo info = new ProcessStartInfo(host);
            info.ArgumentList.Add(dll);
            foreach (string arg in args) info.ArgumentList.Add(arg);
            info.UseShellExecute = false;
            info.RedirectStandardInput = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.StandardInputEncoding = new UTF8Encoding(false);
            info.StandardOutputEncoding = new UTF8Encoding(false);
            info.StandardErrorEncoding = new UTF8Encoding(false);
            foreach (string name in info.Environment.Keys.Where(k => k.StartsWith("HUGO_", StringComparison.Ordinal)).ToList()) info.Environment.Remove(name);
            info.Environment["HOME"] = env.Home;
            info.Environment["USERPROFILE"] = env.Home;
            return info;
        }

        private static async Task<string?> ReadAsync(ChannelReader<string> reader, int timeoutMs)
        {
            using (CancellationTokenSource cts = new CancellationTokenSource(timeoutMs))
            {
                try
                {
                    return await reader.ReadAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
                catch (ChannelClosedException)
                {
                    return null;
                }
            }
        }

        private static async Task<List<string>> ReadUntilAsync(ChannelReader<string> reader, string idKey, StringBuilder stderr)
        {
            List<string> seen = new List<string>();
            DateTime deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline)
            {
                string? line = await ReadAsync(reader, Math.Max(1, (int)(deadline - DateTime.UtcNow).TotalMilliseconds)).ConfigureAwait(false);
                if (line == null) break;
                seen.Add(line);
                JsonRpcMessage? message = JsonRpcMessage.TryParse(line);
                if (message != null && message.IsResponse && message.IdKey == idKey) return seen;
            }
            throw new TestAssertionException("No response " + idKey + " from the hugo process. Seen: " + String.Join(" | ", seen) + " Stderr: " + stderr);
        }

        private static async Task StdioRoundTrip(CancellationToken token)
        {
            using (FakeHugoServer server = new FakeHugoServer())
            using (CliTestEnvironment env = new CliTestEnvironment())
            {
                string credentials = Path.Combine(env.Root, "creds dir", "c.json");
                ProcessStartInfo info = StartInfo(env, "mcp", "stdio", "--server", server.BaseAddress, "--token", server.IssueToken(), "--credentials-file", credentials, "--verbose");
                using (Process process = new Process())
                {
                    process.StartInfo = info;
                    StringBuilder stderr = new StringBuilder();
                    ConcurrentQueue<string> all = new ConcurrentQueue<string>();
                    Channel<string> lines = Channel.CreateUnbounded<string>();
                    process.ErrorDataReceived += (s, e) =>
                    {
                        if (e.Data != null) lock (stderr) stderr.AppendLine(e.Data);
                    };
                    Assert.True(process.Start(), "process started");
                    process.BeginErrorReadLine();
                    Task pump = Task.Run(async () =>
                    {
                        while (true)
                        {
                            string? line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false);
                            if (line == null) break;
                            all.Enqueue(line);
                            await lines.Writer.WriteAsync(line).ConfigureAwait(false);
                        }
                        lines.Writer.TryComplete();
                    });

                    try
                    {
                        StreamWriter stdin = process.StandardInput;
                        stdin.NewLine = "\n";
                        stdin.AutoFlush = true;
                        await stdin.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"process-test\",\"version\":\"1\"}}}").ConfigureAwait(false);
                        List<string> init = await ReadUntilAsync(lines.Reader, "1", stderr).ConfigureAwait(false);
                        Assert.True(init.Last().Contains("\"serverInfo\"", StringComparison.Ordinal), "initialize result: " + init.Last());
                        await stdin.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}").ConfigureAwait(false);
                        await stdin.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"stream_echo\",\"arguments\":{\"text\":\"over stdio\"},\"_meta\":{\"progressToken\":\"p1\"}}}").ConfigureAwait(false);
                        List<string> call = await ReadUntilAsync(lines.Reader, "2", stderr).ConfigureAwait(false);
                        Assert.True(call.Any(l => l.Contains("notifications/progress", StringComparison.Ordinal)), "progress relayed");
                        Assert.True(call.Last().Contains("streamed: over stdio", StringComparison.Ordinal), "tool result");
                        await stdin.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"id\":\"three\",\"method\":\"tools/list\"}").ConfigureAwait(false);
                        List<string> list = await ReadUntilAsync(lines.Reader, "\"three\"", stderr).ConfigureAwait(false);
                        Assert.True(list.Last().Contains("stream_echo", StringComparison.Ordinal), "tools listed");
                        stdin.Close();

                        using (CancellationTokenSource exit = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
                        {
                            await process.WaitForExitAsync(exit.Token).ConfigureAwait(false);
                        }
                        await Task.WhenAny(pump, Task.Delay(2000)).ConfigureAwait(false);
                        Assert.Equal(0, process.ExitCode, "exit code; stderr: " + stderr);
                        foreach (string line in all)
                        {
                            JsonRpcMessage? message = JsonRpcMessage.TryParse(line);
                            Assert.True(message != null && message.JsonRpc == "2.0", "stdout carries only JSON-RPC: " + line);
                        }
                        string diagnostics;
                        lock (stderr) diagnostics = stderr.ToString();
                        Assert.True(diagnostics.Contains("[hugo mcp stdio]", StringComparison.Ordinal), "verbose diagnostics on stderr: " + diagnostics);
                        Assert.True(server.Where(r => r.Method == "DELETE" && r.RawPath == "/mcp").Count == 1, "session deleted on exit");
                        Assert.True(!File.Exists(credentials), "the relay does not write credentials given as flags");
                    }
                    finally
                    {
                        if (!process.HasExited)
                        {
                            try
                            {
                                process.Kill(true);
                            }
                            catch (InvalidOperationException)
                            {
                            }
                        }
                    }
                }
            }
        }

        private static async Task ExitCodes(CancellationToken token)
        {
            using (CliTestEnvironment env = new CliTestEnvironment())
            {
                foreach (KeyValuePair<string, int> expectation in new Dictionary<string, int> { { "--version", 0 }, { "frobnicate", 2 } })
                {
                    using (Process process = new Process())
                    {
                        process.StartInfo = StartInfo(env, expectation.Key);
                        process.Start();
                        process.StandardInput.Close();
                        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
                        Task<string> stderr = process.StandardError.ReadToEndAsync();
                        using (CancellationTokenSource exit = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
                        {
                            await process.WaitForExitAsync(exit.Token).ConfigureAwait(false);
                        }
                        string output = await stdout.ConfigureAwait(false);
                        string error = await stderr.ConfigureAwait(false);
                        Assert.Equal(expectation.Value, process.ExitCode, expectation.Key + " exit code; stderr: " + error);
                        if (expectation.Value == 0) Assert.True(output.StartsWith("hugo 0.1.0", StringComparison.Ordinal), "version output: " + output);
                        else Assert.Equal(String.Empty, output, "nothing on stdout for a usage error");
                    }
                }
            }
        }

        #endregion
    }
}
