namespace Test.Shared.Cli
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Cli;
    using Hugo.Cli.Credentials;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;

    /// <summary>
    /// Credential precedence (flags, then environment, then login cache) and the credential file (permissions, paths
    /// with spaces, corrupt files, removal).
    /// </summary>
    public static class CliCredentialCases
    {
        #region Public-Members

        /// <summary>
        /// Suite identifier.
        /// </summary>
        public const string SuiteId = "cli.credentials";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the cases.
        /// </summary>
        /// <returns>Cases.</returns>
        public static List<TestCaseDescriptor> Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            cases.Add(Case("flags-win", "Flags win over the environment and the login cache", FlagsWin));
            cases.Add(Case("environment-over-cache", "The environment wins over the login cache", EnvironmentOverCache));
            cases.Add(Case("cache-fallback", "The login cache supplies the server, token, keys, and tenant when nothing else does", CacheFallback));
            cases.Add(Case("sources-not-mixed", "A cached token is never paired with flag or environment keys", SourcesNotMixed));
            cases.Add(Case("server-precedence", "Server: --server, HUGO_SERVER, cache default, then http://127.0.0.1:8800", ServerPrecedence));
            cases.Add(Case("cache-per-server", "Cache entries belong to one server", CachePerServer));
            cases.Add(Case("half-keys", "An access key without its secret key is rejected (flags and environment)", HalfKeys));
            cases.Add(Case("file-permissions", "The credential file is 0600 and its new directory 0700", FilePermissions));
            cases.Add(Case("paths-with-spaces", "Credential files in directories with spaces round-trip (flag and environment)", PathsWithSpaces));
            cases.Add(Case("corrupt-file", "A corrupt credential file is reported clearly", CorruptFile));
            cases.Add(Case("remove-entry", "Removing the default server's entry promotes the newest remaining one", RemoveEntry));
            return cases;
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string id, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, body);
        }

        private static async Task SeedCacheAsync(CliTestEnvironment env, string server, string? token, string? accessKey, string? secretKey, string? tenant, CancellationToken ct)
        {
            CredentialStore store = new CredentialStore(env.CredentialsPath);
            await store.UpdateAsync(file =>
            {
                ServerCredential entry = new ServerCredential();
                entry.Server = server;
                entry.Token = token;
                entry.AccessKey = accessKey;
                entry.SecretKey = secretKey;
                entry.TenantId = tenant;
                file.Upsert(entry);
                return true;
            }, ct).ConfigureAwait(false);
        }

        private static Task<ResolvedCredentials> ResolveAsync(CliTestEnvironment env, CredentialOptions flags, CancellationToken ct)
        {
            return CredentialResolver.ResolveAsync(flags, name => env.Environment.TryGetValue(name, out string? value) ? value : null, env.Home, ct);
        }

        private static async Task FlagsWin(CancellationToken token)
        {
            using (CliTestEnvironment env = new CliTestEnvironment())
            {
                await SeedCacheAsync(env, "http://127.0.0.1:8800", "cache-token", "cache-ak", "cache-sk", "ten_cache", token).ConfigureAwait(false);
                env.Environment[CliEnvironmentVariables.Token] = "env-token";
                env.Environment[CliEnvironmentVariables.Tenant] = "ten_env";
                CredentialOptions flags = new CredentialOptions { Token = "flag-token", Tenant = "ten_flag" };
                ResolvedCredentials resolved = await ResolveAsync(env, flags, token).ConfigureAwait(false);
                Assert.Equal(CredentialSourceEnum.Flags, resolved.Source, "source");
                Assert.Equal("flag-token", resolved.Token, "token");
                Assert.True(resolved.AccessKey == null, "no cached keys with a flag token");
                Assert.Equal("ten_flag", resolved.TenantId, "tenant");

                CredentialOptions keys = new CredentialOptions { AccessKey = "flag-ak", SecretKey = "flag-sk" };
                ResolvedCredentials byKeys = await ResolveAsync(env, keys, token).ConfigureAwait(false);
                Assert.Equal(CredentialSourceEnum.Flags, byKeys.Source, "source with keys");
                Assert.True(byKeys.Token == null, "environment token not used with flag keys");
                Assert.Equal("flag-ak", byKeys.AccessKey, "access key");
                Assert.Equal("ten_env", byKeys.TenantId, "tenant from environment when no flag");
            }
        }

        private static async Task EnvironmentOverCache(CancellationToken token)
        {
            using (CliTestEnvironment env = new CliTestEnvironment())
            {
                await SeedCacheAsync(env, "http://127.0.0.1:8800", "cache-token", null, null, "ten_cache", token).ConfigureAwait(false);
                env.Environment[CliEnvironmentVariables.AccessKey] = "env-ak";
                env.Environment[CliEnvironmentVariables.SecretKey] = "env-sk";
                ResolvedCredentials resolved = await ResolveAsync(env, new CredentialOptions(), token).ConfigureAwait(false);
                Assert.Equal(CredentialSourceEnum.Environment, resolved.Source, "source");
                Assert.True(resolved.Token == null, "cached token not used");
                Assert.Equal("env-ak", resolved.AccessKey, "access key");
                Assert.Equal("env-sk", resolved.SecretKey, "secret key");
                Assert.True(resolved.TenantId == null, "cache tenant not used with environment credentials");
            }
        }

        private static async Task CacheFallback(CancellationToken token)
        {
            using (CliTestEnvironment env = new CliTestEnvironment())
            {
                await SeedCacheAsync(env, "https://hugo.example.com", "cache-token", "cache-ak", "cache-sk", "ten_cache", token).ConfigureAwait(false);
                ResolvedCredentials resolved = await ResolveAsync(env, new CredentialOptions(), token).ConfigureAwait(false);
                Assert.Equal(CredentialSourceEnum.Cache, resolved.Source, "source");
                Assert.Equal("https://hugo.example.com", resolved.Server, "server from cache default");
                Assert.Equal("cache-token", resolved.Token, "token");
                Assert.Equal("cache-ak", resolved.AccessKey, "access key");
                Assert.Equal("ten_cache", resolved.TenantId, "tenant");
                Assert.True(resolved.HasKeys, "has keys");
            }
        }

        private static async Task SourcesNotMixed(CancellationToken token)
        {
            using (CliTestEnvironment env = new CliTestEnvironment())
            {
                await SeedCacheAsync(env, "http://127.0.0.1:8800", "cache-token", null, null, null, token).ConfigureAwait(false);
                env.Environment[CliEnvironmentVariables.Token] = "env-token";
                ResolvedCredentials resolved = await ResolveAsync(env, new CredentialOptions(), token).ConfigureAwait(false);
                Assert.Equal("env-token", resolved.Token, "environment token");
                Assert.True(!resolved.HasKeys, "no keys");

                CredentialOptions flags = new CredentialOptions { AccessKey = "a", SecretKey = "s" };
                ResolvedCredentials byFlags = await ResolveAsync(env, flags, token).ConfigureAwait(false);
                Assert.True(byFlags.Token == null, "neither cached nor environment token paired with flag keys");
            }
        }

        private static async Task ServerPrecedence(CancellationToken token)
        {
            using (CliTestEnvironment env = new CliTestEnvironment())
            {
                ResolvedCredentials fallback = await ResolveAsync(env, new CredentialOptions(), token).ConfigureAwait(false);
                Assert.Equal("http://127.0.0.1:8800", fallback.Server, "built-in default");
                Assert.Equal(CredentialSourceEnum.None, fallback.Source, "no credentials");

                await SeedCacheAsync(env, "http://cache.example:1", "t", null, null, null, token).ConfigureAwait(false);
                ResolvedCredentials cached = await ResolveAsync(env, new CredentialOptions(), token).ConfigureAwait(false);
                Assert.Equal("http://cache.example:1", cached.Server, "cache default");

                env.Environment[CliEnvironmentVariables.Server] = "http://env.example:2/";
                ResolvedCredentials fromEnv = await ResolveAsync(env, new CredentialOptions(), token).ConfigureAwait(false);
                Assert.Equal("http://env.example:2", fromEnv.Server, "environment");

                ResolvedCredentials fromFlag = await ResolveAsync(env, new CredentialOptions { Server = "http://flag.example:3" }, token).ConfigureAwait(false);
                Assert.Equal("http://flag.example:3", fromFlag.Server, "flag");

                env.Environment[CliEnvironmentVariables.Profile] = "Env Profile";
                ResolvedCredentials profile = await ResolveAsync(env, new CredentialOptions(), token).ConfigureAwait(false);
                Assert.Equal("Env Profile", profile.Profile, "profile from environment");
                ResolvedCredentials profileFlag = await ResolveAsync(env, new CredentialOptions { Profile = "Flag" }, token).ConfigureAwait(false);
                Assert.Equal("Flag", profileFlag.Profile, "profile from flag");
            }
        }

        private static async Task CachePerServer(CancellationToken token)
        {
            using (CliTestEnvironment env = new CliTestEnvironment())
            {
                await SeedCacheAsync(env, "http://a.example:1", "token-a", null, null, null, token).ConfigureAwait(false);
                await SeedCacheAsync(env, "http://b.example:2", "token-b", null, null, null, token).ConfigureAwait(false);
                ResolvedCredentials a = await ResolveAsync(env, new CredentialOptions { Server = "HTTP://A.example:1/" }, token).ConfigureAwait(false);
                Assert.Equal("token-a", a.Token, "server a");
                ResolvedCredentials b = await ResolveAsync(env, new CredentialOptions(), token).ConfigureAwait(false);
                Assert.Equal("http://b.example:2", b.Server, "latest login is default");
                Assert.Equal("token-b", b.Token, "server b");
                ResolvedCredentials other = await ResolveAsync(env, new CredentialOptions { Server = "http://c.example:3" }, token).ConfigureAwait(false);
                Assert.Equal(CredentialSourceEnum.None, other.Source, "no entry for another server");
            }
        }

        private static async Task HalfKeys(CancellationToken token)
        {
            using (CliTestEnvironment env = new CliTestEnvironment())
            {
                bool usage = false;
                try
                {
                    await ResolveAsync(env, new CredentialOptions { AccessKey = "a" }, token).ConfigureAwait(false);
                }
                catch (CliUsageException)
                {
                    usage = true;
                }
                Assert.True(usage, "flag access key without secret");

                bool secretOnly = false;
                try
                {
                    await ResolveAsync(env, new CredentialOptions { SecretKey = "s" }, token).ConfigureAwait(false);
                }
                catch (CliUsageException)
                {
                    secretOnly = true;
                }
                Assert.True(secretOnly, "flag secret key without access key");

                env.Environment[CliEnvironmentVariables.AccessKey] = "a";
                bool error = false;
                try
                {
                    await ResolveAsync(env, new CredentialOptions(), token).ConfigureAwait(false);
                }
                catch (CliException e)
                {
                    error = e.Message.Contains(CliEnvironmentVariables.SecretKey, StringComparison.Ordinal);
                }
                Assert.True(error, "environment access key without secret");
            }
        }

        private static async Task FilePermissions(CancellationToken token)
        {
            using (CliTestEnvironment env = new CliTestEnvironment())
            {
                await SeedCacheAsync(env, "http://127.0.0.1:8800", "t", "ak", "sk", null, token).ConfigureAwait(false);
                Assert.True(File.Exists(env.CredentialsPath), "file written");
                if (OperatingSystem.IsWindows()) return;
                UnixFileMode mode = File.GetUnixFileMode(env.CredentialsPath);
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode, "file mode");
                UnixFileMode directoryMode = File.GetUnixFileMode(Path.GetDirectoryName(env.CredentialsPath)!);
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, directoryMode, "directory mode");

                File.SetUnixFileMode(env.CredentialsPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
                await SeedCacheAsync(env, "http://127.0.0.1:8800", "t2", null, null, null, token).ConfigureAwait(false);
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(env.CredentialsPath), "rewrite restores 0600");
                Assert.True(Directory.GetFiles(Path.GetDirectoryName(env.CredentialsPath)!, "*.tmp").Length == 0, "no temporary files left");
            }
        }

        private static async Task PathsWithSpaces(CancellationToken token)
        {
            using (CliTestEnvironment env = new CliTestEnvironment())
            {
                string path = Path.Combine(env.Root, "my creds", "sub dir", "hugo credentials.json");
                CredentialOptions flags = new CredentialOptions { CredentialsFile = path };
                Assert.Equal(path, CredentialResolver.CredentialsPath(flags, n => null, env.Home), "flag path");
                CredentialStore store = new CredentialStore(path);
                await store.UpdateAsync(file =>
                {
                    file.Upsert(new ServerCredential { Server = "http://127.0.0.1:8800", Token = "spaced-token" });
                    return true;
                }, token).ConfigureAwait(false);
                ResolvedCredentials resolved = await ResolveAsync(env, flags, token).ConfigureAwait(false);
                Assert.Equal("spaced-token", resolved.Token, "token via flag path");

                env.Environment[CliEnvironmentVariables.CredentialsFile] = path;
                ResolvedCredentials viaEnv = await ResolveAsync(env, new CredentialOptions(), token).ConfigureAwait(false);
                Assert.Equal("spaced-token", viaEnv.Token, "token via environment path");
                Assert.True(!File.Exists(env.CredentialsPath), "default file untouched");
            }
        }

        private static async Task CorruptFile(CancellationToken token)
        {
            using (CliTestEnvironment env = new CliTestEnvironment())
            {
                Directory.CreateDirectory(Path.GetDirectoryName(env.CredentialsPath)!);
                await File.WriteAllTextAsync(env.CredentialsPath, "{ this is not json", token).ConfigureAwait(false);
                CliRunResult result = await env.RunAsync(token, "whoami").ConfigureAwait(false);
                Assert.Equal(1, result.ExitCode, "exit code " + result);
                Assert.True(result.Stderr.Contains("not valid JSON", StringComparison.Ordinal), "message: " + result.Stderr);
                Assert.True(result.Stderr.Contains(env.CredentialsPath, StringComparison.Ordinal), "names the file");
            }
        }

        private static Task RemoveEntry(CancellationToken token)
        {
            CredentialFile file = new CredentialFile();
            file.Upsert(new ServerCredential { Server = "http://a:1", Token = "a", SavedUtc = DateTime.UtcNow.AddMinutes(-10) });
            file.Upsert(new ServerCredential { Server = "http://b:2", Token = "b", SavedUtc = DateTime.UtcNow.AddMinutes(-5) });
            file.Upsert(new ServerCredential { Server = "http://c:3", Token = "c", SavedUtc = DateTime.UtcNow });
            Assert.Equal("http://c:3", file.DefaultServer, "latest upsert is default");
            Assert.True(file.Remove("http://c:3"), "removed");
            Assert.Equal("http://b:2", file.DefaultServer, "newest remaining becomes default");
            Assert.True(!file.Remove("http://c:3"), "second removal is a no-op");
            Assert.Equal(2, file.Servers.Count, "remaining entries");
            return Task.CompletedTask;
        }

        #endregion
    }
}
