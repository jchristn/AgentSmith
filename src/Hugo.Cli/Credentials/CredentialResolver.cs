namespace Hugo.Cli.Credentials
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Applies credential precedence. The server is the first of <c>--server</c>, <c>HUGO_SERVER</c>, the login cache's
    /// default server, and <c>http://127.0.0.1:8800</c>. Credentials come as a set from the first source that has any:
    /// flags (<c>--token</c>, <c>--access-key</c> and <c>--secret-key</c>), then the environment (<c>HUGO_TOKEN</c>,
    /// <c>HUGO_ACCESS_KEY</c> and <c>HUGO_SECRET_KEY</c>), then the login cache entry for the chosen server. Sources are
    /// never mixed, so a cached token for one identity is never paired with keys for another. The tenant is the first
    /// of <c>--tenant</c>, <c>HUGO_TENANT</c>, and the cache entry's tenant. Thread-safe (stateless).
    /// </summary>
    public static class CredentialResolver
    {
        /// <summary>
        /// The credential file path: <c>--credentials-file</c>, then <c>HUGO_CREDENTIALS_FILE</c>, then
        /// <c>{home}/.hugo/credentials.json</c>.
        /// </summary>
        /// <param name="flags">Flags.</param>
        /// <param name="environment">Environment lookup.</param>
        /// <param name="homeDirectory">Home directory.</param>
        /// <returns>Path.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static string CredentialsPath(CredentialOptions flags, Func<string, string?> environment, string homeDirectory)
        {
            if (flags == null) throw new ArgumentNullException(nameof(flags));
            if (environment == null) throw new ArgumentNullException(nameof(environment));
            if (!String.IsNullOrWhiteSpace(flags.CredentialsFile)) return flags.CredentialsFile;
            string? fromEnvironment = environment(CliEnvironmentVariables.CredentialsFile);
            if (!String.IsNullOrWhiteSpace(fromEnvironment)) return fromEnvironment;
            return CredentialStore.DefaultPath(homeDirectory);
        }

        /// <summary>
        /// Resolve the server, profile, and credentials.
        /// </summary>
        /// <param name="flags">Flags.</param>
        /// <param name="environment">Environment lookup.</param>
        /// <param name="homeDirectory">Home directory.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Resolved values. <see cref="ResolvedCredentials.Source"/> is None when nothing was found.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        /// <exception cref="CliUsageException">Thrown when the server URL is invalid or an access key is given without its secret key on the command line.</exception>
        /// <exception cref="CliException">Thrown when the environment has an access key without its secret key, or the credential file is unreadable.</exception>
        public static async Task<ResolvedCredentials> ResolveAsync(CredentialOptions flags, Func<string, string?> environment, string homeDirectory, CancellationToken token)
        {
            if (flags == null) throw new ArgumentNullException(nameof(flags));
            if (environment == null) throw new ArgumentNullException(nameof(environment));
            if (String.IsNullOrEmpty(homeDirectory)) throw new ArgumentNullException(nameof(homeDirectory));

            CredentialStore store = new CredentialStore(CredentialsPath(flags, environment, homeDirectory));
            CredentialFile file = await store.LoadAsync(token).ConfigureAwait(false);

            ResolvedCredentials resolved = new ResolvedCredentials();
            resolved.Store = store;

            string? server = FirstNonEmpty(flags.Server, environment(CliEnvironmentVariables.Server), file.DefaultServer);
            resolved.Server = server == null ? ServerAddress.Default : ServerAddress.Normalize(server);
            resolved.Profile = FirstNonEmpty(flags.Profile, environment(CliEnvironmentVariables.Profile));

            string? envToken = Empty(environment(CliEnvironmentVariables.Token));
            string? envAccessKey = Empty(environment(CliEnvironmentVariables.AccessKey));
            string? envSecretKey = Empty(environment(CliEnvironmentVariables.SecretKey));
            ServerCredential? cached = file.Find(resolved.Server);

            if (Empty(flags.Token) != null || Empty(flags.AccessKey) != null || Empty(flags.SecretKey) != null)
            {
                if (Empty(flags.AccessKey) != null && Empty(flags.SecretKey) == null)
                    throw new CliUsageException("--access-key requires --secret-key.");
                if (Empty(flags.SecretKey) != null && Empty(flags.AccessKey) == null)
                    throw new CliUsageException("--secret-key requires --access-key.");
                resolved.Source = CredentialSourceEnum.Flags;
                resolved.Token = Empty(flags.Token);
                resolved.AccessKey = Empty(flags.AccessKey);
                resolved.SecretKey = Empty(flags.SecretKey);
            }
            else if (envToken != null || envAccessKey != null)
            {
                if (envAccessKey != null && envSecretKey == null)
                    throw new CliException(CliEnvironmentVariables.AccessKey + " is set but " + CliEnvironmentVariables.SecretKey + " is not.");
                resolved.Source = CredentialSourceEnum.Environment;
                resolved.Token = envToken;
                resolved.AccessKey = envAccessKey;
                resolved.SecretKey = envAccessKey != null ? envSecretKey : null;
            }
            else if (cached != null && (!String.IsNullOrEmpty(cached.Token) || (!String.IsNullOrEmpty(cached.AccessKey) && !String.IsNullOrEmpty(cached.SecretKey))))
            {
                resolved.Source = CredentialSourceEnum.Cache;
                resolved.Token = Empty(cached.Token);
                resolved.AccessKey = Empty(cached.AccessKey);
                resolved.SecretKey = Empty(cached.SecretKey);
            }

            resolved.TenantId = FirstNonEmpty(flags.Tenant, environment(CliEnvironmentVariables.Tenant), resolved.Source == CredentialSourceEnum.Cache ? cached?.TenantId : null);
            return resolved;
        }

        private static string? Empty(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string? FirstNonEmpty(params string?[] values)
        {
            foreach (string? value in values)
            {
                if (!String.IsNullOrWhiteSpace(value)) return value.Trim();
            }
            return null;
        }
    }
}
