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
    /// <c>hugo mcp stdio</c>: relays the MCP stdio transport to a Hugo server's Streamable HTTP MCP endpoint, for MCP
    /// clients that can only launch stdio servers.
    /// </summary>
    public class McpStdioCommand : CliCommand
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Name
        {
            get
            {
                return "mcp stdio";
            }
        }

        /// <inheritdoc />
        public override string Summary
        {
            get
            {
                return "Relay MCP over stdio to a Hugo server (for stdio-only MCP clients).";
            }
        }

        /// <inheritdoc />
        public override string Details
        {
            get
            {
                return "Reads newline-delimited JSON-RPC messages from standard input, posts each one to {server}/mcp\n"
                    + "(or {server}/mcp/{profile}) with your session token, and writes every message the server sends back\n"
                    + "(responses, notifications, progress, and server requests) to standard output, one per line. Standard\n"
                    + "output carries protocol messages only; diagnostics go to standard error. The relay exits when standard\n"
                    + "input closes.\n\n"
                    + "Credentials come from the first of: --token or --access-key/--secret-key; HUGO_TOKEN or\n"
                    + "HUGO_ACCESS_KEY/HUGO_SECRET_KEY; the login cache written by 'hugo login'. With an access key, an\n"
                    + "expired session token is renewed automatically.\n\n"
                    + "Example (Claude Code): claude mcp add hugo -- hugo mcp stdio --server http://127.0.0.1:8800 --profile Hugo";
            }
        }

        /// <inheritdoc />
        public override IReadOnlyList<OptionSpec> Options
        {
            get
            {
                List<OptionSpec> options = CommandSupport.CredentialOptions;
                options.Insert(1, OptionSpec.Value("profile", "NAME", "Agent profile; the endpoint becomes /mcp/{NAME} (env HUGO_PROFILE)."));
                options.Add(OptionSpec.Flag("no-listen", "Do not open the GET stream for server-initiated messages."));
                options.Add(OptionSpec.Flag("verbose", "Log every relayed message to standard error."));
                return options;
            }
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override async Task<int> RunAsync(CliConsole console, ParsedArguments arguments, CancellationToken token)
        {
            if (console == null) throw new ArgumentNullException(nameof(console));
            if (arguments == null) throw new ArgumentNullException(nameof(arguments));

            ResolvedCredentials resolved = await CommandSupport.ResolveAsync(console, arguments, token).ConfigureAwait(false);
            CommandSupport.RequireCredentials(resolved);

            McpRelaySettings settings = new McpRelaySettings(ServerAddress.McpEndpoint(resolved.Server, resolved.Profile));
            settings.ListenForServerMessages = !arguments.Has("no-listen");
            settings.Verbose = arguments.Has("verbose");

            using (HugoApiClient api = new HugoApiClient(resolved.Server))
            {
                using (RelayAuthenticator authenticator = new RelayAuthenticator(api, resolved.Token, resolved.AccessKey, resolved.SecretKey, resolved.TenantId))
                {
                    authenticator.Log = message =>
                    {
                        lock (console.StandardError)
                        {
                            console.StandardError.WriteLine("[hugo mcp stdio] " + message);
                        }
                    };
                    authenticator.TokenRenewed = CommandSupport.PersistRenewal(resolved);
                    using (McpStdioRelay relay = new McpStdioRelay(settings, authenticator, console.StandardError))
                    {
                        if (settings.Verbose)
                            console.StandardError.WriteLine("[hugo mcp stdio] Credentials from the " + CommandSupport.DescribeSource(resolved.Source) + ".");
                        return await relay.RunAsync(console.StandardInput, console.StandardOutput, token).ConfigureAwait(false);
                    }
                }
            }
        }

        #endregion
    }
}
