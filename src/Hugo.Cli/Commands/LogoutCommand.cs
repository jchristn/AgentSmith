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
    /// <c>hugo logout</c>: revokes the cached session token on the server and removes the server's entry from the
    /// credential file.
    /// </summary>
    public class LogoutCommand : CliCommand
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Name
        {
            get
            {
                return "logout";
            }
        }

        /// <inheritdoc />
        public override string Summary
        {
            get
            {
                return "Revoke and forget the cached session token.";
            }
        }

        /// <inheritdoc />
        public override string Details
        {
            get
            {
                return "Revokes the cached session token for the server (DELETE /v1.0/token) and removes the server's entry,\n"
                    + "including any stored access key, from the credential file. The entry is removed even when the server\n"
                    + "cannot be reached.";
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
                    OptionSpec.Flag("no-revoke", "Only forget the token locally; do not revoke it on the server."),
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
            string? serverText = flags.Server ?? Empty(console.GetEnvironmentVariable(CliEnvironmentVariables.Server)) ?? file.DefaultServer;
            string server = serverText == null ? ServerAddress.Default : ServerAddress.Normalize(serverText);
            ServerCredential? entry = file.Find(server);
            if (entry == null)
            {
                console.Out.WriteLine("Not signed in to " + server + ".");
                return (int)CliExitCodeEnum.Success;
            }

            if (!String.IsNullOrEmpty(entry.Token) && !arguments.Has("no-revoke"))
            {
                using (HugoApiClient api = new HugoApiClient(server))
                {
                    try
                    {
                        await api.RevokeAsync(entry.Token, token).ConfigureAwait(false);
                    }
                    catch (HugoApiException e)
                    {
                        console.StandardError.WriteLine("hugo: the server did not revoke the token (HTTP " + e.StatusCode + "): " + e.Message);
                    }
                    catch (CliException e)
                    {
                        console.StandardError.WriteLine("hugo: " + e.Message);
                    }
                }
            }

            await store.UpdateAsync(f => f.Remove(server), token).ConfigureAwait(false);
            console.Out.WriteLine("Signed out of " + server + ".");
            return (int)CliExitCodeEnum.Success;
        }

        #endregion

        #region Private-Methods

        private static string? Empty(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        #endregion
    }
}
