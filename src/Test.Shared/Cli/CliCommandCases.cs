namespace Test.Shared.Cli
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Cli.Credentials;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;

    /// <summary>
    /// <c>login</c>, <c>logout</c>, <c>whoami</c>, and <c>profiles list</c> against the in-process fake Hugo server.
    /// </summary>
    public static class CliCommandCases
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string SuiteId = "cli.commands";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the cases.
        /// </summary>
        /// <returns>Cases.</returns>
        public static List<TestCaseDescriptor> Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            cases.Add(Case("login-password", "Password login from non-TTY stdin stores the token and never the password", LoginPassword));
            cases.Add(Case("login-password-stdin", "--password-stdin reads the password from the first line", LoginPasswordStdin));
            cases.Add(Case("login-access-key", "Access key login stores the token and keys with mode 0600", LoginAccessKey));
            cases.Add(Case("login-no-store-key", "--no-store-key stores only the token", LoginNoStoreKey));
            cases.Add(Case("login-rejected", "A rejected login exits 1 and writes nothing", LoginRejected));
            cases.Add(Case("login-must-change", "Must-change-password is reported on stderr", LoginMustChange));
            cases.Add(Case("whoami", "whoami prints the principal from the cached token, and --json prints JSON", Whoami));
            cases.Add(Case("whoami-renews", "whoami renews an expired cached token with stored keys and saves it", WhoamiRenews));
            cases.Add(Case("whoami-expired", "whoami with a rejected token and no keys exits 1 and suggests login", WhoamiExpired));
            cases.Add(Case("profiles-404", "profiles list explains a server without the route (404)", Profiles404));
            cases.Add(Case("profiles-list", "profiles list prints names and MCP endpoints, and --json the raw body", ProfilesList));
            cases.Add(Case("logout", "logout revokes the token and removes the entry", Logout));
            return cases;
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string id, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, body);
        }

        private static async Task<ServerCredential?> CachedAsync(CliTestEnvironment env, string server, CancellationToken token)
        {
            CredentialFile file = await new CredentialStore(env.CredentialsPath).LoadAsync(token).ConfigureAwait(false);
            return file.Find(server);
        }

        private static async Task SeedAsync(CliTestEnvironment env, ServerCredential entry, CancellationToken token)
        {
            await new CredentialStore(env.CredentialsPath).UpdateAsync(file =>
            {
                file.Upsert(entry);
                return true;
            }, token).ConfigureAwait(false);
        }

        private static async Task LoginPassword(CancellationToken token)
        {
            using (FakeHugoServer server = new FakeHugoServer())
            using (CliTestEnvironment env = new CliTestEnvironment())
            {
                string stdin = server.Email + "\n" + server.Password + "\n";
                CliRunResult result = await env.RunAsync(stdin, token, "login", "--server", server.BaseAddress).ConfigureAwait(false);
                Assert.Equal(0, result.ExitCode, "exit code " + result);
                Assert.True(result.Stdout.Contains("Signed in to " + server.BaseAddress, StringComparison.Ordinal), "stdout: " + result.Stdout);
                Assert.True(!result.Stderr.Contains("Password:", StringComparison.Ordinal), "no prompts when stdin is not a terminal");
                ServerCredential? entry = await CachedAsync(env, server.BaseAddress, token).ConfigureAwait(false);
                Assert.True(entry != null && server.IsValid(entry.Token), "token cached");
                Assert.Equal(server.Email, entry!.Email, "email cached");
                Assert.Equal(server.TenantId, entry.TenantId, "tenant cached");
                Assert.True(entry.AccessKey == null && entry.SecretKey == null, "no keys");
                string text = await File.ReadAllTextAsync(env.CredentialsPath, token).ConfigureAwait(false);
                Assert.True(!text.Contains(server.Password, StringComparison.Ordinal), "password never stored");
                FakeHugoRequest issue = server.Where(r => r.RawPath == "/v1.0/token" && r.Method == "POST").Single();
                Assert.Equal(server.Email, issue.Header("x-email"), "x-email header");
                Assert.True(issue.Header("Authorization") == null, "no bearer on login");
            }
        }

        private static async Task LoginPasswordStdin(CancellationToken token)
        {
            using (FakeHugoServer server = new FakeHugoServer())
            using (CliTestEnvironment env = new CliTestEnvironment())
            {
                CliRunResult result = await env.RunAsync(server.Password + "\n", token, "login", "--server", server.BaseAddress, "--email", server.Email, "--tenant", "ten_given", "--password-stdin").ConfigureAwait(false);
                Assert.Equal(0, result.ExitCode, "exit code " + result);
                FakeHugoRequest issue = server.Where(r => r.RawPath == "/v1.0/token" && r.Method == "POST").Single();
                Assert.Equal("ten_given", issue.Header("x-tenant-guid"), "tenant header");
                Assert.Equal(server.Password, issue.Header("x-password"), "password header");
            }
        }

        private static async Task LoginAccessKey(CancellationToken token)
        {
            using (FakeHugoServer server = new FakeHugoServer())
            using (CliTestEnvironment env = new CliTestEnvironment())
            {
                CliRunResult result = await env.RunAsync(server.SecretKey + "\n", token, "login", "--server", server.BaseAddress, "--access-key", server.AccessKey).ConfigureAwait(false);
                Assert.Equal(0, result.ExitCode, "exit code " + result);
                ServerCredential? entry = await CachedAsync(env, server.BaseAddress, token).ConfigureAwait(false);
                Assert.True(entry != null && server.IsValid(entry.Token), "token cached");
                Assert.Equal(server.AccessKey, entry!.AccessKey, "access key cached");
                Assert.Equal(server.SecretKey, entry.SecretKey, "secret key cached");
                Assert.Equal("Credential", entry.PrincipalType, "principal type");
                if (!OperatingSystem.IsWindows())
                    Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(env.CredentialsPath), "file mode 0600");
                FakeHugoRequest issue = server.Where(r => r.RawPath == "/v1.0/token").Single();
                Assert.Equal(server.AccessKey, issue.Header("x-access-key"), "x-access-key header");
                Assert.Equal(server.SecretKey, issue.Header("x-secret-key"), "x-secret-key header");
            }
        }

        private static async Task LoginNoStoreKey(CancellationToken token)
        {
            using (FakeHugoServer server = new FakeHugoServer())
            using (CliTestEnvironment env = new CliTestEnvironment())
            {
                CliRunResult result = await env.RunAsync(token, "login", "--server", server.BaseAddress, "--access-key", server.AccessKey, "--secret-key", server.SecretKey, "--no-store-key").ConfigureAwait(false);
                Assert.Equal(0, result.ExitCode, "exit code " + result);
                ServerCredential? entry = await CachedAsync(env, server.BaseAddress, token).ConfigureAwait(false);
                Assert.True(entry != null && entry.Token != null, "token cached");
                Assert.True(entry!.AccessKey == null && entry.SecretKey == null, "keys not stored");
                string text = await File.ReadAllTextAsync(env.CredentialsPath, token).ConfigureAwait(false);
                Assert.True(!text.Contains(server.SecretKey, StringComparison.Ordinal), "secret absent from file");
            }
        }

        private static async Task LoginRejected(CancellationToken token)
        {
            using (FakeHugoServer server = new FakeHugoServer())
            using (CliTestEnvironment env = new CliTestEnvironment())
            {
                CliRunResult result = await env.RunAsync("wrong password\n", token, "login", "--server", server.BaseAddress, "--email", server.Email).ConfigureAwait(false);
                Assert.Equal(1, result.ExitCode, "exit code " + result);
                Assert.True(result.Stderr.Contains("401", StringComparison.Ordinal) && result.Stderr.Contains("invalid", StringComparison.Ordinal), "message: " + result.Stderr);
                Assert.True(!File.Exists(env.CredentialsPath), "nothing written");

                CliRunResult noPassword = await env.RunAsync(String.Empty, token, "login", "--server", server.BaseAddress, "--email", server.Email).ConfigureAwait(false);
                Assert.Equal(1, noPassword.ExitCode, "empty stdin");
                Assert.True(noPassword.Stderr.Contains("password is required", StringComparison.Ordinal), "message: " + noPassword.Stderr);

                CliRunResult unreachable = await env.RunAsync("x\n", token, "login", "--server", "http://127.0.0.1:9", "--email", "a@b.c").ConfigureAwait(false);
                Assert.Equal(1, unreachable.ExitCode, "unreachable");
                Assert.True(unreachable.Stderr.Contains("Cannot reach", StringComparison.Ordinal), "message: " + unreachable.Stderr);
            }
        }

        private static async Task LoginMustChange(CancellationToken token)
        {
            using (FakeHugoServer server = new FakeHugoServer())
            using (CliTestEnvironment env = new CliTestEnvironment())
            {
                server.MustChangePassword = true;
                CliRunResult result = await env.RunAsync(server.Password + "\n", token, "login", "--server", server.BaseAddress, "--email", server.Email).ConfigureAwait(false);
                Assert.Equal(0, result.ExitCode, "exit code " + result);
                Assert.True(result.Stderr.Contains("asks you to change it", StringComparison.Ordinal), "warning: " + result.Stderr);
                CliRunResult whoami = await env.RunAsync(token, "whoami").ConfigureAwait(false);
                Assert.Equal(0, whoami.ExitCode, "whoami still works " + whoami);
                Assert.True(whoami.Stderr.Contains("asks you to change it", StringComparison.Ordinal), "whoami warning");
            }
        }

        private static async Task Whoami(CancellationToken token)
        {
            using (FakeHugoServer server = new FakeHugoServer())
            using (CliTestEnvironment env = new CliTestEnvironment())
            {
                await SeedAsync(env, new ServerCredential { Server = server.BaseAddress, Token = server.IssueToken() }, token).ConfigureAwait(false);
                CliRunResult result = await env.RunAsync(token, "whoami").ConfigureAwait(false);
                Assert.Equal(0, result.ExitCode, "exit code " + result);
                Assert.True(result.Stdout.Contains("Thomas Anderson (User)", StringComparison.Ordinal), "principal: " + result.Stdout);
                Assert.True(result.Stdout.Contains(server.TenantId, StringComparison.Ordinal), "tenant");
                Assert.True(result.Stdout.Contains("login cache", StringComparison.Ordinal), "credential source");

                CliRunResult json = await env.RunAsync(token, "whoami", "--json").ConfigureAwait(false);
                Assert.Equal(0, json.ExitCode, "json exit code");
                Assert.True(json.Stdout.TrimStart().StartsWith("{", StringComparison.Ordinal) && json.Stdout.Contains("\"principalName\": \"Thomas Anderson\"", StringComparison.Ordinal), "json: " + json.Stdout);
            }
        }

        private static async Task WhoamiRenews(CancellationToken token)
        {
            using (FakeHugoServer server = new FakeHugoServer())
            using (CliTestEnvironment env = new CliTestEnvironment())
            {
                await SeedAsync(env, new ServerCredential { Server = server.BaseAddress, Token = "expired", AccessKey = server.AccessKey, SecretKey = server.SecretKey }, token).ConfigureAwait(false);
                CliRunResult result = await env.RunAsync(token, "whoami").ConfigureAwait(false);
                Assert.Equal(0, result.ExitCode, "exit code " + result);
                Assert.Equal(1, server.TokensIssued, "renewed once");
                ServerCredential? entry = await CachedAsync(env, server.BaseAddress, token).ConfigureAwait(false);
                Assert.True(entry != null && server.IsValid(entry.Token), "renewed token saved");
            }
        }

        private static async Task WhoamiExpired(CancellationToken token)
        {
            using (FakeHugoServer server = new FakeHugoServer())
            using (CliTestEnvironment env = new CliTestEnvironment())
            {
                CliRunResult result = await env.RunAsync(token, "whoami", "--server", server.BaseAddress, "--token", "revoked").ConfigureAwait(false);
                Assert.Equal(1, result.ExitCode, "exit code " + result);
                Assert.True(result.Stderr.Contains("hugo login", StringComparison.Ordinal), "suggests login: " + result.Stderr);
                Assert.Equal(String.Empty, result.Stdout, "nothing on stdout");
            }
        }

        private static async Task Profiles404(CancellationToken token)
        {
            using (FakeHugoServer server = new FakeHugoServer())
            using (CliTestEnvironment env = new CliTestEnvironment())
            {
                env.Environment[CliEnvironmentVariables.Server] = server.BaseAddress;
                env.Environment[CliEnvironmentVariables.Token] = server.IssueToken();
                CliRunResult result = await env.RunAsync(token, "profiles", "list").ConfigureAwait(false);
                Assert.Equal(1, result.ExitCode, "exit code " + result);
                Assert.True(result.Stderr.Contains("does not offer the agent profile list yet", StringComparison.Ordinal), "message: " + result.Stderr);
                Assert.True(result.Stderr.Contains("--profile", StringComparison.Ordinal), "suggests --profile");
            }
        }

        private static async Task ProfilesList(CancellationToken token)
        {
            using (FakeHugoServer server = new FakeHugoServer())
            using (CliTestEnvironment env = new CliTestEnvironment())
            {
                server.ProfilesRouteAvailable = true;
                CliRunResult result = await env.RunAsync(token, "profiles", "list", "--server", server.BaseAddress, "--access-key", server.AccessKey, "--secret-key", server.SecretKey).ConfigureAwait(false);
                Assert.Equal(0, result.ExitCode, "exit code " + result);
                List<string> lines = result.StdoutLines;
                Assert.Equal(3, lines.Count, "header and two rows: " + result.Stdout);
                Assert.True(lines[0].StartsWith("NAME", StringComparison.Ordinal), "header");
                Assert.True(lines.Any(l => l.StartsWith("Ops Team", StringComparison.Ordinal) && l.EndsWith(server.BaseAddress + "/mcp/Ops%20Team", StringComparison.Ordinal)), "profile endpoint: " + result.Stdout);
                Assert.True(!File.Exists(env.CredentialsPath), "flag credentials are not written to disk");

                CliRunResult json = await env.RunAsync(token, "profiles", "list", "--server", server.BaseAddress, "--token", server.IssueToken(), "--json").ConfigureAwait(false);
                Assert.Equal(0, json.ExitCode, "json exit code");
                Assert.True(json.Stdout.Contains("\"apr_ops\"", StringComparison.Ordinal), "raw body: " + json.Stdout);
            }
        }

        private static async Task Logout(CancellationToken token)
        {
            using (FakeHugoServer server = new FakeHugoServer())
            using (CliTestEnvironment env = new CliTestEnvironment())
            {
                string issued = server.IssueToken();
                await SeedAsync(env, new ServerCredential { Server = server.BaseAddress, Token = issued, AccessKey = "a", SecretKey = "s" }, token).ConfigureAwait(false);
                CliRunResult result = await env.RunAsync(token, "logout").ConfigureAwait(false);
                Assert.Equal(0, result.ExitCode, "exit code " + result);
                Assert.True(!server.IsValid(issued), "token revoked");
                Assert.True(server.Where(r => r.Method == "DELETE" && r.RawPath == "/v1.0/token").Count == 1, "DELETE /v1.0/token");
                Assert.True(await CachedAsync(env, server.BaseAddress, token).ConfigureAwait(false) == null, "entry removed");
                string text = await File.ReadAllTextAsync(env.CredentialsPath, token).ConfigureAwait(false);
                Assert.True(!text.Contains("\"s\"", StringComparison.Ordinal), "keys removed");

                CliRunResult again = await env.RunAsync(token, "logout", "--server", server.BaseAddress).ConfigureAwait(false);
                Assert.Equal(0, again.ExitCode, "second logout");
                Assert.True(again.Stdout.Contains("Not signed in", StringComparison.Ordinal), "message: " + again.Stdout);
            }
        }

        #endregion
    }
}
