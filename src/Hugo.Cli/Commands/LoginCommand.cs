namespace Hugo.Cli.Commands
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Cli.Api;
    using Hugo.Cli.Arguments;
    using Hugo.Cli.Credentials;

    /// <summary>
    /// <c>hugo login</c>: signs in with email and password, or with an access key, and stores the session token in the
    /// credential file. Passwords are never stored.
    /// </summary>
    public class LoginCommand : CliCommand
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Name
        {
            get
            {
                return "login";
            }
        }

        /// <inheritdoc />
        public override string Summary
        {
            get
            {
                return "Sign in and cache a session token.";
            }
        }

        /// <inheritdoc />
        public override string Details
        {
            get
            {
                return "Signs in to a Hugo server and stores the session token in ~/.hugo/credentials.json (mode 0600).\n\n"
                    + "With --access-key, the access key and secret key are exchanged for a session token and stored too, so\n"
                    + "'hugo mcp stdio' can renew the session when it expires; pass --no-store-key to store only the token.\n"
                    + "Without --access-key, you are asked for your email and password (the password is read without echo and\n"
                    + "never stored). When standard input is not a terminal, the email (unless --email is given) and the\n"
                    + "password or secret key are read as lines from standard input.\n\n"
                    + "Examples:\n"
                    + "  hugo login --server http://127.0.0.1:8800 --email admin@example.com\n"
                    + "  hugo login --server https://hugo.example.com --access-key AK... --tenant ten_...\n"
                    + "  printf '%s\\n' \"$PASSWORD\" | hugo login --email admin@example.com --password-stdin";
            }
        }

        /// <inheritdoc />
        public override IReadOnlyList<OptionSpec> Options
        {
            get
            {
                return new List<OptionSpec>
                {
                    CommandSupport.Server,
                    OptionSpec.Value("email", "EMAIL", "Email to sign in with (prompted when missing)."),
                    CommandSupport.Tenant,
                    CommandSupport.AccessKey,
                    OptionSpec.Value("secret-key", "KEY", "Secret key (env HUGO_SECRET_KEY; prompted without echo when missing)."),
                    OptionSpec.Flag("password-stdin", "Read the password (or secret key) from the first line of standard input."),
                    OptionSpec.Flag("no-store-key", "With --access-key, store only the session token, not the keys."),
                    CommandSupport.CredentialsFile
                };
            }
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override async Task<int> RunAsync(CliConsole console, ParsedArguments arguments, CancellationToken token)
        {
            if (console == null) throw new ArgumentNullException(nameof(console));
            if (arguments == null) throw new ArgumentNullException(nameof(arguments));

            CredentialOptions flags = CommandSupport.ReadFlags(arguments);
            CredentialStore store = new CredentialStore(CredentialResolver.CredentialsPath(flags, console.GetEnvironmentVariable, console.HomeDirectory));
            CredentialFile file = await store.LoadAsync(token).ConfigureAwait(false);
            string? serverText = First(flags.Server, console.GetEnvironmentVariable(CliEnvironmentVariables.Server), file.DefaultServer);
            string server = serverText == null ? ServerAddress.Default : ServerAddress.Normalize(serverText);
            string? tenant = First(flags.Tenant, console.GetEnvironmentVariable(CliEnvironmentVariables.Tenant));
            string? accessKey = First(flags.AccessKey, String.IsNullOrEmpty(arguments.Get("email")) ? console.GetEnvironmentVariable(CliEnvironmentVariables.AccessKey) : null);
            bool fromStdin = arguments.Has("password-stdin");

            ServerCredential entry = new ServerCredential();
            entry.Server = server;
            TokenResponse issued;
            using (HugoApiClient api = new HugoApiClient(server))
            {
                try
                {
                    if (accessKey != null)
                    {
                        string? secretKey = First(flags.SecretKey, console.GetEnvironmentVariable(CliEnvironmentVariables.SecretKey));
                        if (secretKey == null) secretKey = fromStdin ? console.In.ReadLine() : console.ReadSecret("Secret key: ");
                        if (String.IsNullOrEmpty(secretKey)) throw new CliException("A secret key is required with --access-key.");
                        issued = await api.LoginWithAccessKeyAsync(accessKey, secretKey, tenant, token).ConfigureAwait(false);
                        if (!arguments.Has("no-store-key"))
                        {
                            entry.AccessKey = accessKey;
                            entry.SecretKey = secretKey;
                        }
                    }
                    else
                    {
                        if (flags.SecretKey != null) throw new CliUsageException("--secret-key requires --access-key.");
                        string? email = arguments.Get("email");
                        if (email == null && !fromStdin) email = console.Prompt("Email: ")?.Trim();
                        if (String.IsNullOrEmpty(email)) throw new CliException("An email is required. Pass --email, or --access-key to sign in with keys.");
                        string? password = fromStdin ? console.In.ReadLine() : console.ReadSecret("Password: ");
                        if (String.IsNullOrEmpty(password)) throw new CliException("A password is required.");
                        issued = await api.LoginWithPasswordAsync(email, password, tenant, token).ConfigureAwait(false);
                        entry.Email = email;
                    }
                }
                catch (HugoApiException e)
                {
                    throw new CliException("Sign-in to " + server + " failed (HTTP " + e.StatusCode + "): " + e.Message, e);
                }
            }

            entry.Token = issued.Token;
            entry.ExpiresUtc = issued.ExpiresUtc;
            entry.TenantId = !String.IsNullOrEmpty(issued.TenantId) ? issued.TenantId : tenant;
            entry.PrincipalType = issued.PrincipalType;
            entry.SavedUtc = DateTime.UtcNow;
            await store.UpdateAsync(f =>
            {
                f.Upsert(entry);
                return true;
            }, token).ConfigureAwait(false);

            string who = entry.Email ?? ("access key " + accessKey);
            console.Out.WriteLine("Signed in to " + server + " as " + who + (entry.TenantId != null ? " (tenant " + entry.TenantId + ")" : String.Empty) + ".");
            console.Out.WriteLine("Session token saved to " + store.FilePath + (entry.ExpiresUtc != null ? "; it expires " + entry.ExpiresUtc.Value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm") + " UTC." : "."));
            if (issued.MustChangePassword)
                console.StandardError.WriteLine("hugo: this account must change its password before it can use Hugo. Change it in the dashboard (or POST /v1.0/api/users/me/password), then run 'hugo login' again.");
            return (int)CliExitCodeEnum.Success;
        }

        #endregion

        #region Private-Methods

        private static string? First(params string?[] values)
        {
            foreach (string? value in values)
            {
                if (!String.IsNullOrWhiteSpace(value)) return value.Trim();
            }
            return null;
        }

        #endregion
    }
}
