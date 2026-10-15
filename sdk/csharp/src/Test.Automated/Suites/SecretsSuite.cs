namespace Test.Automated.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Sdk;
    using Hugo.Sdk.Enumeration;
    using Hugo.Sdk.Enums;
    using Hugo.Sdk.Http;
    using Hugo.Sdk.Models;
    using Hugo.Sdk.Requests;
    using Hugo.Sdk.Responses;

    /// <summary>
    /// Secrets vault: CRUD, values, referrers, parse, reveal with re-authentication, and the events stream.
    /// </summary>
    public class SecretsSuite : TestSuite
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Name
        {
            get
            {
                return "Secrets";
            }
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override async Task RunAsync(TestRunner runner, HarnessContext ctx)
        {
            await runner.RunAsync("Secrets CRUD", async () =>
            {
                Secret secret = await CreateAsync(ctx.Owner, Unique("secret"), "s3cret-value-1234").ConfigureAwait(false);
                Check.Equal(SecretKindEnum.Password, secret.Kind, "kind");
                Check.True(secret.Fields.Contains("password"), "field names");
                Secret read = await ctx.Owner.Secrets.GetAsync(secret.Id).ConfigureAwait(false);
                Check.Equal(secret.Name, read.Name, "name");
                SecretRequest update = new SecretRequest();
                update.Description = "updated description";
                Secret updated = await ctx.Owner.Secrets.UpdateAsync(secret.Id, update).ConfigureAwait(false);
                Check.Equal("updated description", updated.Description, "description");
                Check.Equal(secret.Name, updated.Name, "an omitted name is kept");
                Check.Equal(SecretKindEnum.Password, updated.Kind, "an omitted kind is kept");
                EnumerationResult<Secret> page = await ctx.Owner.Secrets.ListAsync().ConfigureAwait(false);
                Check.True(page.Objects.Any(s => s.Id == secret.Id), "listed");
                await ctx.Owner.Secrets.DeleteAsync(secret.Id).ConfigureAwait(false);
                await Check.FailsAsync(() => ctx.Owner.Secrets.GetAsync(secret.Id), 404, "NotFound").ConfigureAwait(false);
            }).ConfigureAwait(false);

            await runner.RunAsync("BearerToken secrets and the kind filter", async () =>
            {
                SecretRequest request = new SecretRequest();
                request.Name = Unique("bearer");
                request.Kind = SecretKindEnum.BearerToken;
                request.Values = new Dictionary<string, string> { { "token", "bearer-token-value-1234" } };
                Secret bearer = await ctx.Owner.Secrets.CreateAsync(request).ConfigureAwait(false);
                Secret password = await CreateAsync(ctx.Owner, Unique("pw"), "another-value-1234").ConfigureAwait(false);
                try
                {
                    Check.Equal(SecretKindEnum.BearerToken, bearer.Kind, "kind");
                    Check.True(bearer.Fields.Contains("token"), "token field");
                    SecretFilter filter = new SecretFilter();
                    filter.Kind = "BearerToken";
                    EnumerationResult<Secret> page = await ctx.Owner.Secrets.ListAsync(filter).ConfigureAwait(false);
                    Check.True(page.Objects.Any(s => s.Id == bearer.Id), "kind filter includes the bearer token");
                    Check.True(page.Objects.All(s => s.Kind == SecretKindEnum.BearerToken), "kind filter excludes other kinds");
                    SecretFilter bad = new SecretFilter();
                    bad.Kind = "NoSuchKind";
                    HugoException e = await Check.FailsAsync(() => ctx.Owner.Secrets.ListAsync(bad), 400, "ValidationFailed").ConfigureAwait(false);
                    Check.True(e.Message.Contains("BearerToken", StringComparison.Ordinal), "the error lists the allowed values");
                }
                finally
                {
                    await ctx.Owner.Secrets.DeleteAsync(bearer.Id).ConfigureAwait(false);
                    await ctx.Owner.Secrets.DeleteAsync(password.Id).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);

            await runner.RunAsync("Secret values and referrers", async () =>
            {
                Secret secret = await CreateAsync(ctx.Owner, Unique("values"), "first-value-1234").ConfigureAwait(false);
                try
                {
                    Secret changed = await ctx.Owner.Secrets.SetValuesAsync(secret.Id, new Dictionary<string, string> { { "password", "second-value-5678" } }).ConfigureAwait(false);
                    Check.True(changed.Fields.Contains("password"), "field kept");
                    List<SecretReferrer> referrers = await ctx.Owner.Secrets.GetReferrersAsync(secret.Id).ConfigureAwait(false);
                    Check.Equal(0, referrers.Count, "no referrers");
                }
                finally
                {
                    await ctx.Owner.Secrets.DeleteAsync(secret.Id).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);

            await runner.RunAsync("Secret parse (kubeconfig)", async () =>
            {
                SecretParseRequest request = new SecretParseRequest();
                request.FileName = "config";
                request.Content = "apiVersion: v1\nkind: Config\nclusters:\n- name: prod\n  cluster:\n    server: https://10.0.0.1:6443\ncontexts:\n- name: prod-admin\n  context:\n    cluster: prod\n    user: admin\n    namespace: shop\ncurrent-context: prod-admin\nusers:\n- name: admin\n  user:\n    token: abc123\n";
                SecretParseResult result = await ctx.Owner.Secrets.ParseAsync(request).ConfigureAwait(false);
                Check.Equal(SecretKindEnum.Kubeconfig, result.Kind, "kind");
                Check.Equal("prod-admin", result.CurrentContext, "current context");
                Check.True(result.Contexts.Any(c => c.Namespace == "shop"), "context namespace");
            }).ConfigureAwait(false);

            await runner.RunAsync("Secret reveal requires re-authentication, then succeeds", async () =>
            {
                Secret secret = await CreateAsync(ctx.Owner, Unique("reveal"), "reveal-me-1234").ConfigureAwait(false);
                try
                {
                    using (HugoClient client = ctx.NewClient())
                    {
                        await client.LoginAsync(ctx.OwnerEmail, ctx.OwnerPassword, ctx.TenantId).ConfigureAwait(false);
                        await Check.FailsAsync(() => client.Secrets.RevealAsync(secret.Id, "harness check"), 403, "ReauthenticationRequired").ConfigureAwait(false);
                        await client.ReauthenticateAsync(ctx.OwnerPassword).ConfigureAwait(false);
                        SecretRevealResponse revealed = await client.Secrets.RevealAsync(secret.Id, "harness check").ConfigureAwait(false);
                        Check.Equal("reveal-me-1234", revealed.Values["password"], "plaintext");
                        Secret after = await client.Secrets.GetAsync(secret.Id).ConfigureAwait(false);
                        Check.True(after.LastRevealedUtc.HasValue, "last revealed recorded");
                    }
                }
                finally
                {
                    await ctx.Owner.Secrets.DeleteAsync(secret.Id).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);

            await runner.RunAsync("Events stream delivers secret.created", async () =>
            {
                string name = Unique("evt");
                Secret? created = null;
                bool opened = false;
                bool seen = false;
                using (CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
                {
                    try
                    {
                        await foreach (SseEvent item in ctx.Owner.Events.StreamAsync(false, cts.Token).ConfigureAwait(false))
                        {
                            if (item.Event == "stream.opened")
                            {
                                opened = true;
                                created = await CreateAsync(ctx.Owner, name, "event-value-1234").ConfigureAwait(false);
                                continue;
                            }
                            if (item.Event == "secret.created" && item.Data.Contains(name, StringComparison.Ordinal))
                            {
                                seen = true;
                                break;
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }
                if (created != null) await ctx.Owner.Secrets.DeleteAsync(created.Id).ConfigureAwait(false);
                Check.True(opened, "stream.opened received");
                Check.True(seen, "secret.created received for " + name);
            }).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static Task<Secret> CreateAsync(HugoClient client, string name, string value)
        {
            SecretRequest request = new SecretRequest();
            request.Name = name;
            request.Kind = SecretKindEnum.Password;
            request.Description = "Hugo C# SDK harness";
            request.Values = new Dictionary<string, string> { { "password", value } };
            return client.Secrets.CreateAsync(request);
        }

        #endregion
    }
}
