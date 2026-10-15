namespace Hugo.Cli.Commands
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Cli.Api;
    using Hugo.Cli.Arguments;
    using Hugo.Cli.Credentials;

    /// <summary>
    /// <c>hugo profiles list</c>: lists the agent profiles the caller can use (<c>GET /v1.0/api/agent-profiles</c>).
    /// </summary>
    public class ProfilesListCommand : CliCommand
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Name
        {
            get
            {
                return "profiles list";
            }
        }

        /// <inheritdoc />
        public override string Summary
        {
            get
            {
                return "List the agent profiles you can use.";
            }
        }

        /// <inheritdoc />
        public override string Details
        {
            get
            {
                return "Lists the agent profiles visible to the current credentials (GET /v1.0/api/agent-profiles) with each\n"
                    + "profile's MCP endpoint. Pass a profile name to 'hugo mcp stdio --profile NAME'.";
            }
        }

        /// <inheritdoc />
        public override IReadOnlyList<OptionSpec> Options
        {
            get
            {
                List<OptionSpec> options = CommandSupport.CredentialOptions;
                options.Add(CommandSupport.Json);
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
            AgentProfileListResponse list;
            using (HugoApiClient api = new HugoApiClient(resolved.Server))
            {
                try
                {
                    list = await CommandSupport.WithTokenAsync(console, resolved, api, (t, ct) => api.ListAgentProfilesAsync(t, ct), token).ConfigureAwait(false);
                }
                catch (HugoApiException e) when (e.StatusCode == 404)
                {
                    throw new CliException("The Hugo server at " + resolved.Server + " does not offer the agent profile list yet (GET /v1.0/api/agent-profiles returned 404). "
                        + "Update the server, or ask your administrator for a profile name and pass it to 'hugo mcp stdio --profile NAME'.", e);
                }
                catch (HugoApiException e) when (e.StatusCode == 403 && String.Equals(e.Code, "PasswordChangeRequired", StringComparison.Ordinal))
                {
                    throw new CliException("This account must change its password before it can use Hugo. Change it in the dashboard (or POST /v1.0/api/users/me/password), then try again.", e);
                }
                catch (HugoApiException e)
                {
                    throw new CliException("The server refused GET /v1.0/api/agent-profiles (HTTP " + e.StatusCode + "): " + e.Message, e);
                }
            }

            if (arguments.Has("json"))
            {
                console.Out.WriteLine(list.RawJson.Trim());
                return (int)CliExitCodeEnum.Success;
            }

            if (list.Objects.Count == 0)
            {
                console.Out.WriteLine("No agent profiles are visible to these credentials.");
                return (int)CliExitCodeEnum.Success;
            }

            List<string[]> rows = new List<string[]>();
            rows.Add(new string[] { "NAME", "DESCRIPTION", "MCP ENDPOINT" });
            foreach (AgentProfileSummary profile in list.Objects)
            {
                string name = profile.Name ?? profile.Id ?? "(unnamed)";
                string description = profile.Description ?? profile.DisplayName ?? String.Empty;
                if (description.Length > 60) description = description.Substring(0, 57) + "...";
                string endpoint = profile.Name != null ? ServerAddress.McpEndpoint(resolved.Server, profile.Name).AbsoluteUri : String.Empty;
                rows.Add(new string[] { name, description.Replace('\n', ' '), endpoint });
            }
            int nameWidth = rows.Max(r => r[0].Length);
            int descriptionWidth = rows.Max(r => r[1].Length);
            foreach (string[] row in rows)
            {
                console.Out.WriteLine(row[0].PadRight(nameWidth + 2) + row[1].PadRight(descriptionWidth + 2) + row[2]);
            }
            return (int)CliExitCodeEnum.Success;
        }

        #endregion
    }
}
