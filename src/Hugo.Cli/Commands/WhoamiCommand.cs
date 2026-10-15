namespace Hugo.Cli.Commands
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Cli.Api;
    using Hugo.Cli.Arguments;
    using Hugo.Cli.Credentials;

    /// <summary>
    /// <c>hugo whoami</c>: shows the principal the current credentials belong to (<c>GET /v1.0/token/details</c>).
    /// </summary>
    public class WhoamiCommand : CliCommand
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Name
        {
            get
            {
                return "whoami";
            }
        }

        /// <inheritdoc />
        public override string Summary
        {
            get
            {
                return "Show who the current credentials belong to.";
            }
        }

        /// <inheritdoc />
        public override string Details
        {
            get
            {
                return "Asks the server who the current credentials belong to (GET /v1.0/token/details) and prints the\n"
                    + "principal, tenant, and session expiry. Credentials are resolved like 'hugo mcp stdio'.";
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
            TokenDetailsResponse details;
            using (HugoApiClient api = new HugoApiClient(resolved.Server))
            {
                try
                {
                    details = await CommandSupport.WithTokenAsync(console, resolved, api, (t, ct) => api.GetTokenDetailsAsync(t, ct), token).ConfigureAwait(false);
                }
                catch (HugoApiException e)
                {
                    throw new CliException("The server refused GET /v1.0/token/details (HTTP " + e.StatusCode + "): " + e.Message, e);
                }
            }

            if (arguments.Has("json"))
            {
                console.Out.WriteLine(JsonSerializer.Serialize(details, CliJson.Indented));
            }
            else
            {
                string name = details.PrincipalName ?? details.Email ?? details.PrincipalId ?? "(unknown)";
                List<KeyValuePair<string, string>> rows = new List<KeyValuePair<string, string>>();
                rows.Add(new KeyValuePair<string, string>("Server", resolved.Server));
                rows.Add(new KeyValuePair<string, string>("Principal", name + (details.PrincipalType != null ? " (" + details.PrincipalType + ")" : String.Empty)));
                if (!String.IsNullOrEmpty(details.Email)) rows.Add(new KeyValuePair<string, string>("Email", details.Email));
                if (!String.IsNullOrEmpty(details.TenantId)) rows.Add(new KeyValuePair<string, string>("Tenant", details.TenantId));
                if (!String.IsNullOrEmpty(details.UserId)) rows.Add(new KeyValuePair<string, string>("User ID", details.UserId));
                if (!String.IsNullOrEmpty(details.CredentialId)) rows.Add(new KeyValuePair<string, string>("Credential ID", details.CredentialId));
                rows.Add(new KeyValuePair<string, string>("Admin", details.IsAdmin ? "yes" : (details.IsTenantAdmin ? "tenant admin" : "no")));
                if (details.ExpiresUtc != null) rows.Add(new KeyValuePair<string, string>("Expires", details.ExpiresUtc.Value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm") + " UTC"));
                rows.Add(new KeyValuePair<string, string>("Credentials", CommandSupport.DescribeSource(resolved.Source)));
                foreach (KeyValuePair<string, string> row in rows)
                {
                    console.Out.WriteLine((row.Key + ":").PadRight(15) + row.Value);
                }
            }

            if (details.MustChangePassword)
                console.StandardError.WriteLine("hugo: this account still has its seeded or reset password, so Hugo asks you to change it. Change it in the dashboard (or POST /v1.0/api/users/me/password); nothing is blocked meanwhile.");
            return (int)CliExitCodeEnum.Success;
        }

        #endregion
    }
}
