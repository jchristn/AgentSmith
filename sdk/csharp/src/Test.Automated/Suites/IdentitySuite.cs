namespace Test.Automated.Suites
{
    using System.Linq;
    using System.Threading.Tasks;
    using Hugo.Sdk;
    using Hugo.Sdk.Enumeration;
    using Hugo.Sdk.Enums;
    using Hugo.Sdk.Models;
    using Hugo.Sdk.Requests;
    using Hugo.Sdk.Responses;

    /// <summary>
    /// Tenants, users, credentials, access key authentication, and signed requests.
    /// </summary>
    public class IdentitySuite : TestSuite
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Name
        {
            get
            {
                return "Identity";
            }
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override async Task RunAsync(TestRunner runner, HarnessContext ctx)
        {
            await runner.RunAsync("Tenants list, read, and update (system administrator)", async () =>
            {
                EnumerationQuery query = new EnumerationQuery();
                query.MaxResults = 1000;
                EnumerationResult<Tenant> page = await ctx.Admin.Tenants.ListAsync(query).ConfigureAwait(false);
                Check.True(page.Objects.Any(t => t.Id == ctx.TenantId), "list includes the provisioned tenant");
                Tenant tenant = await ctx.Admin.Tenants.GetAsync(ctx.TenantId).ConfigureAwait(false);
                Check.Equal("sdk-csharp-" + ctx.RunId, tenant.Name, "name");
                TenantRequest update = new TenantRequest();
                update.Name = tenant.Name;
                update.Description = "updated by the C# harness";
                Tenant updated = await ctx.Admin.Tenants.UpdateAsync(ctx.TenantId, update).ConfigureAwait(false);
                Check.Equal("updated by the C# harness", updated.Description, "description");
                EnumerationResult<Tenant> mine = await ctx.Owner.Tenants.ListAsync().ConfigureAwait(false);
                Check.True(mine.Objects.All(t => t.Id == ctx.TenantId), "a tenant admin sees only their tenant");
            }).ConfigureAwait(false);

            await runner.RunAsync("Users CRUD", async () =>
            {
                UserRequest create = new UserRequest();
                create.Email = Unique("user") + "@sdk-csharp.test";
                create.Password = "Password-" + HarnessContext.RandomHex(4);
                create.FirstName = "Thomas";
                create.LastName = "Anderson";
                User user = await ctx.Owner.Users.CreateAsync(create).ConfigureAwait(false);
                Check.Equal(ctx.TenantId, user.TenantId, "tenant");
                Check.Equal("Thomas", user.FirstName, "first name");
                User read = await ctx.Owner.Users.GetAsync(user.Id).ConfigureAwait(false);
                Check.Equal(create.Email, read.Email, "email");
                UserRequest update = new UserRequest();
                update.FirstName = "Neo";
                update.PreferredLocale = "de";
                User updated = await ctx.Owner.Users.UpdateAsync(user.Id, update).ConfigureAwait(false);
                Check.Equal("Neo", updated.FirstName, "updated first name");
                Check.Equal("Anderson", updated.LastName, "unchanged last name");
                EnumerationResult<User> page = await ctx.Owner.Users.ListAsync().ConfigureAwait(false);
                Check.True(page.Objects.Any(u => u.Id == user.Id), "list includes the user");
                await ctx.Owner.Users.DeleteAsync(user.Id).ConfigureAwait(false);
                await Check.FailsAsync(() => ctx.Owner.Users.GetAsync(user.Id), 404).ConfigureAwait(false);
            }).ConfigureAwait(false);

            await runner.RunAsync("Signed-in user: read and update me", async () =>
            {
                User me = await ctx.Owner.Users.GetMeAsync().ConfigureAwait(false);
                Check.Equal(ctx.OwnerUserId, me.Id, "me");
                UserRequest update = new UserRequest();
                update.LastName = "Owner-" + HarnessContext.RandomHex(2);
                update.PreferredLocale = "en";
                User updated = await ctx.Owner.Users.UpdateMeAsync(update).ConfigureAwait(false);
                Check.Equal(update.LastName, updated.LastName, "last name");
            }).ConfigureAwait(false);

            await runner.RunAsync("Effective permissions of a user", async () =>
            {
                EffectivePermissions permissions = await ctx.Owner.Users.GetPermissionsAsync(ctx.OwnerUserId).ConfigureAwait(false);
                Check.Equal(ctx.OwnerUserId, permissions.PrincipalId, "principal");
                Check.True(permissions.IsTenantAdmin, "tenant admin");
                Check.True(permissions.HasPermission.Count > 0, "checks present");
            }).ConfigureAwait(false);

            await runner.RunAsync("Administrator password reset forces a change", async () =>
            {
                string email = Unique("reset") + "@sdk-csharp.test";
                UserRequest create = new UserRequest();
                create.Email = email;
                create.Password = "Password-" + HarnessContext.RandomHex(4);
                User user = await ctx.Owner.Users.CreateAsync(create).ConfigureAwait(false);
                try
                {
                    string reset = "Reset-" + HarnessContext.RandomHex(6);
                    User after = await ctx.Owner.Users.ResetPasswordAsync(user.Id, reset).ConfigureAwait(false);
                    Check.True(after.MustChangePassword, "must change after reset");
                    using (HugoClient client = ctx.NewClient())
                    {
                        TokenResponse login = await client.LoginAsync(email, reset, ctx.TenantId).ConfigureAwait(false);
                        Check.True(login.MustChangePassword, "login reports must change");
                    }
                    User noChange = await ctx.Owner.Users.ResetPasswordAsync(user.Id, "Reset2-" + HarnessContext.RandomHex(6), false).ConfigureAwait(false);
                    Check.True(!noChange.MustChangePassword, "mustChangePassword=false honored");
                }
                finally
                {
                    await ctx.Owner.Users.DeleteAsync(user.Id).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);

            await runner.RunAsync("Credentials CRUD and rotate", async () =>
            {
                CredentialRequest create = new CredentialRequest();
                create.Name = Unique("cred");
                create.AuthMode = CredentialAuthModeEnum.Hybrid;
                CredentialCreateResponse created = await ctx.Owner.Credentials.CreateAsync(create).ConfigureAwait(false);
                Check.NotEmpty(created.SecretKey, "secret key returned once");
                Check.Equal(created.AccessKey, created.Credential.AccessKey, "access key");
                Credential read = await ctx.Owner.Credentials.GetAsync(created.Credential.Id).ConfigureAwait(false);
                Check.Equal(created.SecretKey.Substring(created.SecretKey.Length - 4), read.SecretKeyLast4, "last four");
                CredentialRequest update = new CredentialRequest();
                update.Name = create.Name + "-renamed";
                Credential updated = await ctx.Owner.Credentials.UpdateAsync(read.Id, update).ConfigureAwait(false);
                Check.Equal(update.Name, updated.Name, "renamed");
                CredentialCreateResponse rotated = await ctx.Owner.Credentials.RotateAsync(read.Id).ConfigureAwait(false);
                Check.True(rotated.SecretKey != created.SecretKey, "rotated secret differs");
                using (HugoClient client = ctx.NewClient())
                {
                    client.UseAccessKey(created.AccessKey, created.SecretKey, ctx.TenantId);
                    await Check.FailsAsync(() => client.GetTokenDetailsAsync(), 401).ConfigureAwait(false);
                    client.UseAccessKey(rotated.AccessKey, rotated.SecretKey, ctx.TenantId);
                    await client.GetTokenDetailsAsync().ConfigureAwait(false);
                }
                EnumerationResult<Credential> page = await ctx.Owner.Credentials.ListAsync().ConfigureAwait(false);
                Check.True(page.Objects.Any(c => c.Id == read.Id), "list includes the credential");
                await ctx.Owner.Credentials.DeleteAsync(read.Id).ConfigureAwait(false);
                await Check.FailsAsync(() => ctx.Owner.Credentials.GetAsync(read.Id), 404).ConfigureAwait(false);
            }).ConfigureAwait(false);

            await runner.RunAsync("Access key headers and session exchange", async () =>
            {
                CredentialRequest create = new CredentialRequest();
                create.Name = Unique("direct");
                create.AuthMode = CredentialAuthModeEnum.Hybrid;
                CredentialCreateResponse created = await ctx.Owner.Credentials.CreateAsync(create).ConfigureAwait(false);
                try
                {
                    using (HugoClient client = ctx.NewClient())
                    {
                        client.UseAccessKey(created.AccessKey, created.SecretKey, ctx.TenantId);
                        TokenDetails details = await client.GetTokenDetailsAsync().ConfigureAwait(false);
                        Check.Equal(PrincipalTypeEnum.Credential, details.PrincipalType, "principal type");
                        Check.Equal(created.Credential.Id, details.CredentialId, "credential");
                        Check.Equal<AuthSchemeEnum?>(AuthSchemeEnum.AccessKeySecret, details.AuthScheme, "scheme");
                        EffectivePermissions permissions = await client.Credentials.GetPermissionsAsync(created.Credential.Id).ConfigureAwait(false);
                        Check.Equal(PrincipalTypeEnum.Credential, permissions.PrincipalType, "permissions principal type");
                    }
                    using (HugoClient client = ctx.NewClient())
                    {
                        TokenResponse issued = await client.LoginWithAccessKeyAsync(created.AccessKey, created.SecretKey).ConfigureAwait(false);
                        Check.NotEmpty(issued.Token, "token");
                        Check.Equal(ctx.TenantId, client.TenantId, "tenant from the token response");
                        TokenDetails details = await client.GetTokenDetailsAsync().ConfigureAwait(false);
                        Check.Equal(PrincipalTypeEnum.Credential, details.PrincipalType, "principal type after exchange");
                    }
                }
                finally
                {
                    await ctx.Owner.Credentials.DeleteAsync(created.Credential.Id).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);

            await runner.RunAsync("Signed requests (GET with query, POST with body)", async () =>
            {
                CredentialRequest create = new CredentialRequest();
                create.Name = Unique("signed");
                create.AuthMode = CredentialAuthModeEnum.SignedRequest;
                // A credential gets only its own scope, never the owner's administrator bypass.
                create.RoleName = "Viewer";
                CredentialCreateResponse created = await ctx.Owner.Credentials.CreateAsync(create).ConfigureAwait(false);
                try
                {
                    using (HugoClient client = ctx.NewClient())
                    {
                        client.UseSignedRequests(created.AccessKey, created.SecretKey, ctx.TenantId);
                        TokenDetails details = await client.GetTokenDetailsAsync().ConfigureAwait(false);
                        Check.Equal<AuthSchemeEnum?>(AuthSchemeEnum.AccessKeySignature, details.AuthScheme, "scheme");
                        EnumerationQuery query = new EnumerationQuery();
                        query.MaxResults = 5;
                        query.Search = "no match here";
                        EnumerationResult<Secret> page = await client.Secrets.ListAsync(query).ConfigureAwait(false);
                        Check.True(page.Success, "signed GET with a query string");
                        PromptRenderResponse rendered = await client.PromptTemplates.RenderContentAsync("Hello {{input.name}}", new { input = new { name = "Mr. Anderson" } }).ConfigureAwait(false);
                        Check.Equal("Hello Mr. Anderson", rendered.Rendered, "signed POST with a body");
                        client.UseAccessKey(created.AccessKey, created.SecretKey, ctx.TenantId);
                        await Check.FailsAsync(() => client.GetTokenDetailsAsync(), 401).ConfigureAwait(false);
                    }
                    using (HugoClient bad = ctx.NewClient())
                    {
                        bad.UseSignedRequests(created.AccessKey, "wrong-secret", ctx.TenantId);
                        await Check.FailsAsync(() => bad.GetTokenDetailsAsync(), 401).ConfigureAwait(false);
                    }
                }
                finally
                {
                    await ctx.Owner.Credentials.DeleteAsync(created.Credential.Id).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);
        }

        #endregion
    }
}
