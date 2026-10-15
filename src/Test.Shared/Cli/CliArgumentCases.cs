namespace Test.Shared.Cli
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Cli;
    using Hugo.Cli.Arguments;
    using Hugo.Cli.Commands;
    using Hugo.Cli.Mcp;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;

    /// <summary>
    /// Argument parsing, help and version output, exit codes, and the small helpers (server URLs, JSON minification,
    /// header encoding, the event-stream reader).
    /// </summary>
    public static class CliArgumentCases
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string SuiteId = "cli.args";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the cases.
        /// </summary>
        /// <returns>Cases.</returns>
        public static List<TestCaseDescriptor> Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            cases.Add(Case("parse-forms", "--name value, --name=value, flags, and values with spaces parse", ParseForms));
            cases.Add(Case("parse-errors", "Unknown, repeated, valueless, and single-dash options are usage errors", ParseErrors));
            cases.Add(Case("parse-terminator", "-- ends options; extra positionals are usage errors", ParseTerminator));
            cases.Add(Case("version", "--version prints the product version and exits 0", Version));
            cases.Add(Case("help-every-command", "--help works for every command and group and exits 0", HelpEveryCommand));
            cases.Add(Case("exit-codes", "No command, unknown commands, and bad options exit 2 with the message on stderr", ExitCodes));
            cases.Add(Case("server-address", "Server URLs normalize and MCP endpoints percent-encode profile names", ServerUrls));
            cases.Add(Case("minify", "Minify puts JSON on one line and keeps escaped line breaks", Minify));
            cases.Add(Case("header-encoding", "Mcp-Name values are plain ASCII or =?base64?...?=", HeaderEncoding));
            cases.Add(Case("sse-reader", "The event-stream reader joins data lines, skips comments, and keeps ids and retry", SseReader));
            cases.Add(Case("no-em-dash", "Help text contains no em-dashes", NoEmDash));
            return cases;
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string id, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, body);
        }

        private static List<OptionSpec> Specs()
        {
            return new List<OptionSpec>
            {
                OptionSpec.Value("server", "URL", "Server."),
                OptionSpec.Value("credentials-file", "PATH", "File."),
                OptionSpec.Flag("json", "JSON.")
            };
        }

        private static Task ParseForms(CancellationToken token)
        {
            ParsedArguments parsed = ArgumentParser.Parse(new[] { "whoami", "--server", "http://127.0.0.1:1", "--credentials-file=/tmp/a b/c.json", "--json" }, 1, Specs());
            Assert.Equal("http://127.0.0.1:1", parsed.Get("server"), "server");
            Assert.Equal("/tmp/a b/c.json", parsed.Get("credentials-file"), "path with spaces");
            Assert.True(parsed.Has("json"), "flag");
            Assert.True(!parsed.HelpRequested, "no help");
            ParsedArguments help = ArgumentParser.Parse(new[] { "-h" }, 0, Specs());
            Assert.True(help.HelpRequested, "-h is help");
            ParsedArguments dashValue = ArgumentParser.Parse(new[] { "--server", "-weird" }, 0, Specs());
            Assert.Equal("-weird", dashValue.Get("server"), "a value may start with a dash");
            return Task.CompletedTask;
        }

        private static Task ParseErrors(CancellationToken token)
        {
            ExpectUsage(new[] { "--nope" }, "unknown option");
            ExpectUsage(new[] { "--server" }, "missing value");
            ExpectUsage(new[] { "--server", "a", "--server", "b" }, "repeated option");
            ExpectUsage(new[] { "--json=yes" }, "flag with value");
            ExpectUsage(new[] { "-s", "x" }, "single dash");
            return Task.CompletedTask;
        }

        private static Task ParseTerminator(CancellationToken token)
        {
            ParsedArguments parsed = ArgumentParser.Parse(new[] { "--json", "--", "--server" }, 0, Specs(), 1);
            Assert.Equal(1, parsed.Positionals.Count, "positionals");
            Assert.Equal("--server", parsed.Positionals[0], "positional after --");
            ExpectUsage(new[] { "extra" }, "unexpected positional");
            return Task.CompletedTask;
        }

        private static void ExpectUsage(string[] args, string what)
        {
            try
            {
                ArgumentParser.Parse(args, 0, Specs());
            }
            catch (CliUsageException)
            {
                return;
            }
            throw new TestAssertionException("Expected a usage error for " + what + ".");
        }

        private static async Task Version(CancellationToken token)
        {
            using (CliTestEnvironment env = new CliTestEnvironment())
            {
                CliRunResult result = await env.RunAsync(token, "--version").ConfigureAwait(false);
                Assert.Equal(0, result.ExitCode, "exit code " + result);
                Assert.Equal("hugo " + ProductInfo.Version + "\n", result.Stdout, "stdout");
                Assert.True(ProductInfo.Version.StartsWith("0.1.0", StringComparison.Ordinal), "product version " + ProductInfo.Version);
                Assert.True(!ProductInfo.Version.Contains('+', StringComparison.Ordinal), "no source revision suffix");
            }
        }

        private static async Task HelpEveryCommand(CancellationToken token)
        {
            using (CliTestEnvironment env = new CliTestEnvironment())
            {
                foreach (CliCommand command in CliApplication.Commands)
                {
                    List<string> args = new List<string>(command.Words) { "--help" };
                    CliRunResult result = await env.RunAsync(token, args.ToArray()).ConfigureAwait(false);
                    Assert.Equal(0, result.ExitCode, command.Name + " --help: " + result);
                    Assert.True(result.Stdout.StartsWith("Usage: hugo " + command.Name, StringComparison.Ordinal), command.Name + " usage line: " + result.Stdout);
                    Assert.True(result.Stdout.Contains("--help", StringComparison.Ordinal), command.Name + " lists --help");
                    foreach (OptionSpec option in command.Options)
                        Assert.True(result.Stdout.Contains("--" + option.Name, StringComparison.Ordinal), command.Name + " documents --" + option.Name);

                    List<string> topic = new List<string> { "help" };
                    topic.AddRange(command.Words);
                    CliRunResult viaHelp = await env.RunAsync(token, topic.ToArray()).ConfigureAwait(false);
                    Assert.Equal(result.Stdout, viaHelp.Stdout, "hugo help " + command.Name);

                    List<string> withBadOption = new List<string>(command.Words) { "--nope", "-h" };
                    CliRunResult helpWins = await env.RunAsync(token, withBadOption.ToArray()).ConfigureAwait(false);
                    Assert.Equal(0, helpWins.ExitCode, command.Name + " help with another option");
                }

                CliRunResult main = await env.RunAsync(token, "--help").ConfigureAwait(false);
                Assert.Equal(0, main.ExitCode, "main help");
                foreach (CliCommand command in CliApplication.Commands)
                    Assert.True(main.Stdout.Contains(command.Name, StringComparison.Ordinal), "main help lists " + command.Name);

                foreach (string group in new[] { "mcp", "profiles" })
                {
                    CliRunResult groupHelp = await env.RunAsync(token, group, "--help").ConfigureAwait(false);
                    Assert.Equal(0, groupHelp.ExitCode, group + " --help");
                    Assert.True(groupHelp.Stdout.Contains("Usage: hugo " + group, StringComparison.Ordinal), group + " usage");
                }
            }
        }

        private static async Task ExitCodes(CancellationToken token)
        {
            using (CliTestEnvironment env = new CliTestEnvironment())
            {
                CliRunResult none = await env.RunAsync(token).ConfigureAwait(false);
                Assert.Equal(2, none.ExitCode, "no arguments");
                Assert.Equal(String.Empty, none.Stdout, "nothing on stdout without arguments");
                Assert.True(none.Stderr.Contains("Usage: hugo", StringComparison.Ordinal), "usage on stderr");

                CliRunResult unknown = await env.RunAsync(token, "frobnicate").ConfigureAwait(false);
                Assert.Equal(2, unknown.ExitCode, "unknown command");
                Assert.True(unknown.Stderr.Contains("Unknown command", StringComparison.Ordinal), "message: " + unknown.Stderr);

                CliRunResult unknownSub = await env.RunAsync(token, "mcp", "http").ConfigureAwait(false);
                Assert.Equal(2, unknownSub.ExitCode, "unknown subcommand");

                CliRunResult group = await env.RunAsync(token, "mcp").ConfigureAwait(false);
                Assert.Equal(2, group.ExitCode, "bare group");

                CliRunResult badOption = await env.RunAsync(token, "whoami", "--nope").ConfigureAwait(false);
                Assert.Equal(2, badOption.ExitCode, "bad option");
                Assert.True(badOption.Stderr.Contains("hugo whoami --help", StringComparison.Ordinal), "points to command help");

                CliRunResult badServer = await env.RunAsync(token, "whoami", "--server", "ftp://example.com").ConfigureAwait(false);
                Assert.Equal(2, badServer.ExitCode, "invalid server URL is a usage error");

                CliRunResult halfKey = await env.RunAsync(token, "whoami", "--access-key", "ak").ConfigureAwait(false);
                Assert.Equal(2, halfKey.ExitCode, "access key without secret key");

                CliRunResult notSignedIn = await env.RunAsync(token, "whoami", "--server", "http://127.0.0.1:9").ConfigureAwait(false);
                Assert.Equal(1, notSignedIn.ExitCode, "not signed in");
                Assert.True(notSignedIn.Stderr.Contains("hugo login", StringComparison.Ordinal), "suggests login: " + notSignedIn.Stderr);
            }
        }

        private static Task ServerUrls(CancellationToken token)
        {
            Assert.Equal("http://127.0.0.1:8800", ServerAddress.Normalize("HTTP://127.0.0.1:8800/"), "trailing slash and case");
            Assert.Equal("https://hugo.example.com/prefix", ServerAddress.Normalize(" https://Hugo.Example.com/prefix/ "), "path prefix kept");
            Assert.Equal("http://127.0.0.1:8800/mcp", ServerAddress.McpEndpoint("http://127.0.0.1:8800", null).ToString(), "default endpoint");
            Assert.Equal("http://127.0.0.1:8800/mcp/Hugo", ServerAddress.McpEndpoint("http://127.0.0.1:8800", "Hugo").ToString(), "profile endpoint");
            Assert.Equal("http://127.0.0.1:8800/mcp/Ops%20Team", ServerAddress.McpEndpoint("http://127.0.0.1:8800", "Ops Team").AbsoluteUri, "spaces encoded");
            foreach (string bad in new[] { "", "127.0.0.1:8800", "ftp://x", "http://x/?a=b" })
            {
                bool threw = false;
                try
                {
                    ServerAddress.Normalize(bad);
                }
                catch (CliUsageException)
                {
                    threw = true;
                }
                Assert.True(threw, "rejects '" + bad + "'");
            }
            return Task.CompletedTask;
        }

        private static Task Minify(CancellationToken token)
        {
            string compact = CliJson.Minify("{\n  \"jsonrpc\": \"2.0\",\n  \"params\": { \"text\": \"a\\nb\", \"n\": [1, 2] }\n}");
            Assert.True(compact.IndexOf('\n', StringComparison.Ordinal) < 0, "no raw newline");
            Assert.Equal("{\"jsonrpc\":\"2.0\",\"params\":{\"text\":\"a\\nb\",\"n\":[1,2]}}", compact, "compact form");
            bool threw = false;
            try
            {
                CliJson.Minify("{not json");
            }
            catch (JsonException)
            {
                threw = true;
            }
            Assert.True(threw, "invalid JSON throws");
            return Task.CompletedTask;
        }

        private static Task HeaderEncoding(CancellationToken token)
        {
            Assert.Equal("echo", McpHeaderEncoder.Encode("echo"), "plain");
            Assert.Equal("file:///a/b.txt", McpHeaderEncoder.Encode("file:///a/b.txt"), "uri");
            string encoded = McpHeaderEncoder.Encode("café");
            Assert.Equal("=?base64?" + Convert.ToBase64String(Encoding.UTF8.GetBytes("café")) + "?=", encoded, "non-ASCII");
            Assert.True(McpHeaderEncoder.Encode(" padded").StartsWith("=?base64?", StringComparison.Ordinal), "leading space");
            Assert.True(McpHeaderEncoder.Encode("=?base64?abc?=") != "=?base64?abc?=", "sentinel-looking text is encoded");
            using (JsonDocument doc = JsonDocument.Parse("{\"s\":\"us-east-1\",\"i\":42,\"b\":true,\"o\":{}}"))
            {
                JsonElement root = doc.RootElement;
                Assert.Equal("us-east-1", McpHeaderEncoder.FormatArgument(root.GetProperty("s"), "string"), "string arg");
                Assert.Equal("42", McpHeaderEncoder.FormatArgument(root.GetProperty("i"), "integer"), "integer arg");
                Assert.Equal("true", McpHeaderEncoder.FormatArgument(root.GetProperty("b"), "boolean"), "boolean arg");
                Assert.True(McpHeaderEncoder.FormatArgument(root.GetProperty("o"), null) == null, "object arg has no header");
                Assert.True(McpHeaderEncoder.FormatArgument(root.GetProperty("s"), "integer") == null, "type mismatch has no header");
            }
            return Task.CompletedTask;
        }

        private static async Task SseReader(CancellationToken token)
        {
            string text = ": comment\n\nid: 1\ndata: \n\nid: 2\nevent: message\nretry: 1500\ndata: {\"a\":\ndata: 1}\n\ndata:no-space\n\nid: 3\ndata: partial";
            using (SseEventReader reader = new SseEventReader(new MemoryStream(Encoding.UTF8.GetBytes(text))))
            {
                SseEvent? priming = await reader.ReadNextAsync(token).ConfigureAwait(false);
                Assert.NotNull(priming, "priming event");
                Assert.Equal("1", priming!.Id, "priming id");
                Assert.Equal(String.Empty, priming.Data, "priming data");
                SseEvent? second = await reader.ReadNextAsync(token).ConfigureAwait(false);
                Assert.Equal("{\"a\":\n1}", second!.Data, "joined data");
                Assert.Equal("2", second.Id, "id");
                Assert.Equal(1500, second.RetryMs, "retry");
                SseEvent? third = await reader.ReadNextAsync(token).ConfigureAwait(false);
                Assert.Equal("no-space", third!.Data, "data without a space");
                SseEvent? end = await reader.ReadNextAsync(token).ConfigureAwait(false);
                Assert.True(end == null, "a partial event at end of stream is discarded");
            }
        }

        private static Task NoEmDash(CancellationToken token)
        {
            StringBuilder all = new StringBuilder(CliApplication.MainHelp());
            foreach (CliCommand command in CliApplication.Commands) all.Append(command.Help());
            Assert.True(all.ToString().IndexOf('—') < 0, "no em-dash in help");
            return Task.CompletedTask;
        }

        #endregion
    }
}
