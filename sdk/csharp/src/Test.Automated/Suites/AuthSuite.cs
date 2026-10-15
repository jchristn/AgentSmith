namespace Test.Automated.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Hugo.Sdk;
    using Hugo.Sdk.Enums;
    using Hugo.Sdk.Models;
    using Hugo.Sdk.Requests;
    using Hugo.Sdk.Responses;

    /// <summary>
    /// Health, tokens, refresh, logout, password change, re-authentication, and the administrator API key.
    /// </summary>
    public class AuthSuite : TestSuite
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Name
        {
            get
            {
                return "Auth";
            }
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override async Task RunAsync(TestRunner runner, HarnessContext ctx)
        {
            await runner.RunAsync("Health is anonymous and healthy", async () =>
            {
                using (HugoClient anonymous = ctx.NewClient())
                {
                    HealthResponse health = await anonymous.System.GetHealthAsync().ConfigureAwait(false);
                    Check.Equal("healthy", health.Status, "status");
                    Check.Equal("0.1.0-alpha", health.Version, "version");
                    HealthResponse live = await anonymous.System.GetLivenessAsync().ConfigureAwait(false);
                    Check.Equal("alive", live.Status, "liveness");
                }
            }).ConfigureAwait(false);

            await runner.RunAsync("Server info", async () =>
            {
                ServerInfo info = await ctx.Owner.System.GetInfoAsync().ConfigureAwait(false);
                Check.Equal("Hugo", info.Product, "product");
                Check.Equal(HugoClient.SdkVersion, info.Version, "version matches the SDK");
                Check.True(info.RouteCount > 0, "route count");
                Check.NotEmpty(info.A2AUrl, "a2aUrl");
            }).ConfigureAwait(false);

            await runner.RunAsync("Tenant lookup by email", async () =>
            {
                using (HugoClient anonymous = ctx.NewClient())
                {
                    List<TenantLookupEntry> tenants = await anonymous.LookupTenantsAsync(ctx.OwnerEmail).ConfigureAwait(false);
                    Check.True(tenants.Any(t => t.TenantId == ctx.TenantId), "lookup returns the provisioned tenant");
                }
            }).ConfigureAwait(false);

            await runner.RunAsync("Login, validate, and token details", async () =>
            {
                TokenValidResponse valid = await ctx.Owner.ValidateTokenAsync().ConfigureAwait(false);
                Check.True(valid.Valid, "token valid");
                TokenDetails details = await ctx.Owner.GetTokenDetailsAsync().ConfigureAwait(false);
                Check.Equal(PrincipalTypeEnum.User, details.PrincipalType, "principal type");
                Check.Equal(ctx.TenantId, details.TenantId, "tenant");
                Check.Equal(ctx.OwnerEmail, details.Email, "email");
                Check.True(details.IsTenantAdmin, "tenant admin");
                Check.Equal<AuthSchemeEnum?>(AuthSchemeEnum.BearerToken, details.AuthScheme, "auth scheme");
            }).ConfigureAwait(false);

            await runner.RunAsync("Refresh issues a new token", async () =>
            {
                using (HugoClient client = ctx.NewClient())
                {
                    TokenResponse first = await client.LoginAsync(ctx.OwnerEmail, ctx.OwnerPassword, ctx.TenantId).ConfigureAwait(false);
                    TokenResponse refreshed = await client.RefreshAsync().ConfigureAwait(false);
                    Check.True(refreshed.Token != first.Token, "token changed");
                    Check.Equal(refreshed.Token, client.Token, "client uses the refreshed token");
                    TokenDetails details = await client.GetTokenDetailsAsync().ConfigureAwait(false);
                    Check.Equal(ctx.OwnerUserId, details.UserId, "same user after refresh");
                }
            }).ConfigureAwait(false);

            await runner.RunAsync("Automatic refresh near expiry", async () =>
            {
                using (HugoClient client = ctx.NewClient())
                {
                    TokenResponse first = await client.LoginAsync(ctx.OwnerEmail, ctx.OwnerPassword, ctx.TenantId).ConfigureAwait(false);
                    client.Transport.RefreshSkewSeconds = 3600;
                    await client.GetTokenDetailsAsync().ConfigureAwait(false);
                    Check.True(client.Token != first.Token, "token was refreshed before the request");
                }
            }).ConfigureAwait(false);

            await runner.RunAsync("Invalid login is a typed 401", async () =>
            {
                using (HugoClient client = ctx.NewClient())
                {
                    HugoException e = await Check.FailsAsync(() => client.LoginAsync(ctx.OwnerEmail, "wrong-password", ctx.TenantId), 401, "InvalidCredentials").ConfigureAwait(false);
                    Check.NotEmpty(e.RequestId, "request id");
                    Check.Equal("Unauthorized", e.Error, "error class");
                }
            }).ConfigureAwait(false);

            await runner.RunAsync("Unauthenticated request is rejected", async () =>
            {
                using (HugoClient client = ctx.NewClient())
                {
                    await Check.FailsAsync(() => client.GetTokenDetailsAsync(), 401, "AuthenticationRequired").ConfigureAwait(false);
                }
            }).ConfigureAwait(false);

            await runner.RunAsync("Logout revokes the token", async () =>
            {
                using (HugoClient client = ctx.NewClient())
                {
                    await client.LoginAsync(ctx.OwnerEmail, ctx.OwnerPassword, ctx.TenantId).ConfigureAwait(false);
                    string token = client.Token!;
                    await client.LogoutAsync().ConfigureAwait(false);
                    Check.Equal(AuthModeEnum.None, client.AuthMode, "client cleared");
                    client.UseToken(token, ctx.TenantId);
                    await Check.FailsAsync(() => client.GetTokenDetailsAsync(), 401).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);

            await runner.RunAsync("Must change password is advisory", async () =>
            {
                string email = Unique("mustchange") + "@sdk-csharp.test";
                string initial = "Initial-" + HarnessContext.RandomHex(6);
                UserRequest request = new UserRequest();
                request.Email = email;
                request.Password = initial;
                request.MustChangePassword = true;
                User user = await ctx.Owner.Users.CreateAsync(request).ConfigureAwait(false);
                try
                {
                    using (HugoClient client = ctx.NewClient())
                    {
                        TokenResponse login = await client.LoginAsync(email, initial, ctx.TenantId).ConfigureAwait(false);
                        Check.True(login.MustChangePassword, "login reports must change password");
                        User before = await client.Users.GetAsync(user.Id).ConfigureAwait(false);
                        Check.True(before.MustChangePassword, "routes keep working while the flag is set");
                        TokenDetails flagged = await client.GetTokenDetailsAsync().ConfigureAwait(false);
                        Check.True(flagged.MustChangePassword, "details report the flag");
                        string next = "Changed-" + HarnessContext.RandomHex(6);
                        User changed = await client.ChangePasswordAsync(initial, next).ConfigureAwait(false);
                        Check.True(!changed.MustChangePassword, "flag cleared");
                        TokenDetails details = await client.GetTokenDetailsAsync().ConfigureAwait(false);
                        Check.True(!details.MustChangePassword, "details flag cleared");
                        User self = await client.Users.GetAsync(user.Id).ConfigureAwait(false);
                        Check.Equal(email, self.Email, "self read after the change");
                        await Check.FailsAsync(() => client.ChangePasswordAsync("not-the-password", "Another-12345"), 400).ConfigureAwait(false);
                    }
                }
                finally
                {
                    await ctx.Owner.Users.DeleteAsync(user.Id).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);

            await runner.RunAsync("Re-authenticate checks the password", async () =>
            {
                using (HugoClient client = ctx.NewClient())
                {
                    await client.LoginAsync(ctx.OwnerEmail, ctx.OwnerPassword, ctx.TenantId).ConfigureAwait(false);
                    await Check.FailsAsync(() => client.ReauthenticateAsync("wrong-password"), 401, "InvalidCredentials").ConfigureAwait(false);
                    ReauthenticateResponse ok = await client.ReauthenticateAsync(ctx.OwnerPassword).ConfigureAwait(false);
                    Check.True(ok.RevealAllowedUntilUtc > ok.ReauthenticatedUtc, "reveal window");
                    TokenDetails details = await client.GetTokenDetailsAsync().ConfigureAwait(false);
                    Check.True(details.ReauthenticatedUtc.HasValue, "session records re-authentication");
                }
            }).ConfigureAwait(false);

            await runner.RunAsync("Administrator API key", async () =>
            {
                if (String.IsNullOrEmpty(ctx.Environment.AdminApiKey)) throw new TestSkippedException("set HUGO_SDK_ADMIN_API_KEY (or HUGO_ADMIN_API_KEY) to the server's admin API key to run this test");
                using (HugoClient client = ctx.NewClient())
                {
                    client.UseAdminApiKey(ctx.Environment.AdminApiKey!, ctx.TenantId);
                    TokenDetails details = await client.GetTokenDetailsAsync().ConfigureAwait(false);
                    Check.Equal<AuthSchemeEnum?>(AuthSchemeEnum.AdminApiKey, details.AuthScheme, "auth scheme");
                    Check.True(details.IsAdmin, "is admin");
                    Tenant tenant = await client.Tenants.GetAsync(ctx.TenantId).ConfigureAwait(false);
                    Check.Equal(ctx.TenantId, tenant.Id, "tenant read with the API key");
                    client.UseAdminApiKey("wrong-key-0000000000000000", ctx.TenantId);
                    await Check.FailsAsync(() => client.GetTokenDetailsAsync(), 401).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);
        }

        #endregion
    }
}
