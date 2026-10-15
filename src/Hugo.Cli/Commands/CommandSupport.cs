namespace Hugo.Cli.Commands
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Cli.Api;
    using Hugo.Cli.Arguments;
    using Hugo.Cli.Credentials;
    using Hugo.Cli.Mcp;

    /// <summary>
    /// Options and helpers shared by the commands. Thread-safe (stateless).
    /// </summary>
    public static class CommandSupport
    {
        #region Public-Members

        /// <summary>
        /// <c>--server URL</c>.
        /// </summary>
        public static OptionSpec Server
        {
            get
            {
                return OptionSpec.Value("server", "URL", "Hugo server base URL (env HUGO_SERVER; default: the last server you signed in to, then http://127.0.0.1:8800).");
            }
        }

        /// <summary>
        /// <c>--token TOKEN</c>.
        /// </summary>
        public static OptionSpec Token
        {
            get
            {
                return OptionSpec.Value("token", "TOKEN", "Session token (env HUGO_TOKEN).");
            }
        }

        /// <summary>
        /// <c>--access-key KEY</c>.
        /// </summary>
        public static OptionSpec AccessKey
        {
            get
            {
                return OptionSpec.Value("access-key", "KEY", "Access key, used with --secret-key (env HUGO_ACCESS_KEY).");
            }
        }

        /// <summary>
        /// <c>--secret-key KEY</c>.
        /// </summary>
        public static OptionSpec SecretKey
        {
            get
            {
                return OptionSpec.Value("secret-key", "KEY", "Secret key, used with --access-key (env HUGO_SECRET_KEY).");
            }
        }

        /// <summary>
        /// <c>--tenant ID</c>.
        /// </summary>
        public static OptionSpec Tenant
        {
            get
            {
                return OptionSpec.Value("tenant", "ID", "Tenant ID (env HUGO_TENANT).");
            }
        }

        /// <summary>
        /// <c>--credentials-file PATH</c>.
        /// </summary>
        public static OptionSpec CredentialsFile
        {
            get
            {
                return OptionSpec.Value("credentials-file", "PATH", "Credential file (env HUGO_CREDENTIALS_FILE; default ~/.hugo/credentials.json).");
            }
        }

        /// <summary>
        /// <c>--json</c>.
        /// </summary>
        public static OptionSpec Json
        {
            get
            {
                return OptionSpec.Flag("json", "Print the server's JSON instead of a table.");
            }
        }

        /// <summary>
        /// The credential options shared by commands that call the server.
        /// </summary>
        public static List<OptionSpec> CredentialOptions
        {
            get
            {
                return new List<OptionSpec> { Server, Token, AccessKey, SecretKey, Tenant, CredentialsFile };
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Read the credential flags from parsed arguments.
        /// </summary>
        /// <param name="arguments">Parsed arguments.</param>
        /// <returns>Flags.</returns>
        /// <exception cref="ArgumentNullException">Thrown when arguments is null.</exception>
        public static CredentialOptions ReadFlags(ParsedArguments arguments)
        {
            if (arguments == null) throw new ArgumentNullException(nameof(arguments));
            CredentialOptions flags = new CredentialOptions();
            flags.Server = arguments.Get("server");
            flags.Profile = arguments.Get("profile");
            flags.Token = arguments.Get("token");
            flags.AccessKey = arguments.Get("access-key");
            flags.SecretKey = arguments.Get("secret-key");
            flags.Tenant = arguments.Get("tenant");
            flags.CredentialsFile = arguments.Get("credentials-file");
            return flags;
        }

        /// <summary>
        /// Resolve credentials for a command.
        /// </summary>
        /// <param name="console">Console.</param>
        /// <param name="arguments">Parsed arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Resolved credentials.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        /// <exception cref="CliUsageException">Thrown for invalid flag combinations.</exception>
        /// <exception cref="CliException">Thrown when the credential file is unreadable.</exception>
        public static async Task<ResolvedCredentials> ResolveAsync(CliConsole console, ParsedArguments arguments, CancellationToken token)
        {
            if (console == null) throw new ArgumentNullException(nameof(console));
            return await CredentialResolver.ResolveAsync(ReadFlags(arguments), console.GetEnvironmentVariable, console.HomeDirectory, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Throw when no credentials were found.
        /// </summary>
        /// <param name="resolved">Resolved credentials.</param>
        /// <exception cref="ArgumentNullException">Thrown when resolved is null.</exception>
        /// <exception cref="CliException">Thrown when there are no credentials.</exception>
        public static void RequireCredentials(ResolvedCredentials resolved)
        {
            if (resolved == null) throw new ArgumentNullException(nameof(resolved));
            if (resolved.HasCredentials) return;
            throw new CliException("Not signed in to " + resolved.Server + ". Run 'hugo login --server " + resolved.Server
                + "', set HUGO_TOKEN (or HUGO_ACCESS_KEY and HUGO_SECRET_KEY), or pass --token.");
        }

        /// <summary>
        /// A callback that writes a renewed token back to the login cache, when the credentials came from it; null
        /// otherwise (tokens obtained from flag or environment keys are never written to disk).
        /// </summary>
        /// <param name="resolved">Resolved credentials.</param>
        /// <returns>Callback or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when resolved is null.</exception>
        public static Func<TokenResponse, CancellationToken, Task>? PersistRenewal(ResolvedCredentials resolved)
        {
            if (resolved == null) throw new ArgumentNullException(nameof(resolved));
            if (resolved.Source != CredentialSourceEnum.Cache || resolved.Store == null) return null;
            CredentialStore store = resolved.Store;
            string server = resolved.Server;
            return async (issued, token) =>
            {
                await store.UpdateAsync(file =>
                {
                    ServerCredential? entry = file.Find(server);
                    if (entry == null) return false;
                    entry.Token = issued.Token;
                    entry.ExpiresUtc = issued.ExpiresUtc;
                    entry.SavedUtc = DateTime.UtcNow;
                    return true;
                }, token).ConfigureAwait(false);
            };
        }

        /// <summary>
        /// Run a REST call with the resolved token, signing in with the keys when there is no token and renewing once
        /// when the server answers 401.
        /// </summary>
        /// <typeparam name="T">Result type.</typeparam>
        /// <param name="console">Console (renewal problems are written to standard error).</param>
        /// <param name="resolved">Resolved credentials.</param>
        /// <param name="api">REST client.</param>
        /// <param name="call">The call, given a token.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        /// <exception cref="CliException">Thrown when no token can be obtained or the token is rejected.</exception>
        /// <exception cref="HugoApiException">Thrown for statuses other than 401.</exception>
        public static async Task<T> WithTokenAsync<T>(CliConsole console, ResolvedCredentials resolved, HugoApiClient api, Func<string, CancellationToken, Task<T>> call, CancellationToken token)
        {
            if (console == null) throw new ArgumentNullException(nameof(console));
            if (resolved == null) throw new ArgumentNullException(nameof(resolved));
            if (api == null) throw new ArgumentNullException(nameof(api));
            if (call == null) throw new ArgumentNullException(nameof(call));
            RequireCredentials(resolved);

            using (RelayAuthenticator authenticator = new RelayAuthenticator(api, resolved.Token, resolved.AccessKey, resolved.SecretKey, resolved.TenantId))
            {
                authenticator.Log = message => console.StandardError.WriteLine("hugo: " + message);
                authenticator.TokenRenewed = PersistRenewal(resolved);
                string? sessionToken = await authenticator.GetTokenAsync(token).ConfigureAwait(false);
                if (String.IsNullOrEmpty(sessionToken)) throw new CliException("Could not sign in to " + resolved.Server + " with the access key.");
                try
                {
                    return await call(sessionToken, token).ConfigureAwait(false);
                }
                catch (HugoApiException e) when (e.StatusCode == 401)
                {
                    if (authenticator.CanRenew && await authenticator.RenewAsync(sessionToken, token).ConfigureAwait(false))
                    {
                        try
                        {
                            return await call(authenticator.CurrentToken!, token).ConfigureAwait(false);
                        }
                        catch (HugoApiException again) when (again.StatusCode == 401)
                        {
                            throw new CliException("Hugo rejected the renewed session token: " + again.Message, again);
                        }
                    }
                    throw new CliException("Hugo rejected the session token (it may have expired or been revoked). Run 'hugo login --server " + resolved.Server + "' again.", e);
                }
            }
        }

        /// <summary>
        /// Describe a credential source for people.
        /// </summary>
        /// <param name="source">Source.</param>
        /// <returns>Description.</returns>
        public static string DescribeSource(CredentialSourceEnum source)
        {
            switch (source)
            {
                case CredentialSourceEnum.Flags:
                    return "command line";
                case CredentialSourceEnum.Environment:
                    return "environment";
                case CredentialSourceEnum.Cache:
                    return "login cache";
                default:
                    return "none";
            }
        }

        #endregion
    }
}
