namespace Test.Automated.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Threading.Tasks;
    using Hugo.Sdk;
    using Hugo.Sdk.Enumeration;
    using Hugo.Sdk.Enums;
    using Hugo.Sdk.Models;
    using Hugo.Sdk.Requests;
    using Hugo.Sdk.Responses;

    /// <summary>
    /// Sessions, audit, prompt templates, request history, settings, and typed errors.
    /// </summary>
    public class AdminSuite : TestSuite
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Name
        {
            get
            {
                return "Admin";
            }
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override async Task RunAsync(TestRunner runner, HarnessContext ctx)
        {
            await runner.RunAsync("Sessions list, read, and revoke", async () =>
            {
                using (HugoClient client = ctx.NewClient())
                {
                    TokenResponse login = await client.LoginAsync(ctx.OwnerEmail, ctx.OwnerPassword, ctx.TenantId).ConfigureAwait(false);
                    Check.NotEmpty(login.SessionId, "session id");
                    EnumerationQuery query = new EnumerationQuery();
                    query.MaxResults = 1000;
                    EnumerationResult<AuthSession> page = await ctx.Owner.Sessions.ListAsync(query).ConfigureAwait(false);
                    Check.True(page.Objects.Any(s => s.Id == login.SessionId), "session listed");
                    AuthSession session = await ctx.Owner.Sessions.GetAsync(login.SessionId!).ConfigureAwait(false);
                    Check.Equal(ctx.OwnerUserId, session.UserId, "session user");
                    Check.Equal(PrincipalTypeEnum.User, session.PrincipalType, "principal type");
                    await ctx.Owner.Sessions.RevokeAsync(login.SessionId!).ConfigureAwait(false);
                    await Check.FailsAsync(() => client.GetTokenDetailsAsync(), 401).ConfigureAwait(false);
                    AuthSession revoked = await ctx.Owner.Sessions.GetAsync(login.SessionId!).ConfigureAwait(false);
                    Check.True(revoked.RevokedUtc.HasValue, "revocation recorded");
                }
            }).ConfigureAwait(false);

            await runner.RunAsync("Audit list and read", async () =>
            {
                AuditFilter filter = new AuditFilter();
                filter.PageSize = 10;
                PageResult<AuditRecord> page = await ctx.Owner.Audit.ListAsync(filter).ConfigureAwait(false);
                Check.True(page.TotalCount > 0, "records exist");
                Check.Equal(1, page.PageNumber, "page number");
                AuditRecord first = page.Objects.First();
                AuditRecord read = await ctx.Owner.Audit.GetAsync(first.Id).ConfigureAwait(false);
                Check.Equal(first.EventType, read.EventType, "event type");
                AuditFilter byType = new AuditFilter();
                byType.EventType = "SessionIssued";
                byType.PageSize = 5;
                PageResult<AuditRecord> sessions = await ctx.Owner.Audit.ListAsync(byType).ConfigureAwait(false);
                Check.True(sessions.Objects.All(r => r.EventType == "SessionIssued"), "event type filter");
            }).ConfigureAwait(false);

            await runner.RunAsync("Prompt templates CRUD, versions, restore, and clone", async () =>
            {
                PromptTemplateRequest create = new PromptTemplateRequest();
                create.Name = Unique("tpl");
                create.Description = "harness template";
                create.Kind = PromptTemplateKindEnum.Fragment;
                create.Content = "Hello {{input.name}}";
                PromptTemplate template = await ctx.Owner.PromptTemplates.CreateAsync(create).ConfigureAwait(false);
                try
                {
                    Check.Equal(1, template.CurrentVersion, "first version");
                    PromptTemplateRequest update = new PromptTemplateRequest();
                    update.Name = template.Name;
                    update.Kind = template.Kind;
                    update.Content = "Good evening, {{input.name}}";
                    update.ChangeNote = "second";
                    PromptTemplate updated = await ctx.Owner.PromptTemplates.UpdateAsync(template.Id, update).ConfigureAwait(false);
                    Check.Equal(2, updated.CurrentVersion, "second version");
                    List<PromptTemplateVersion> versions = await ctx.Owner.PromptTemplates.GetVersionsAsync(template.Id).ConfigureAwait(false);
                    Check.True(versions.Count >= 2, "versions listed");
                    PromptTemplateVersion v1 = await ctx.Owner.PromptTemplates.GetVersionAsync(template.Id, 1).ConfigureAwait(false);
                    Check.Equal("Hello {{input.name}}", v1.Content, "version 1 content");
                    PromptTemplate restored = await ctx.Owner.PromptTemplates.RestoreAsync(template.Id, 1).ConfigureAwait(false);
                    Check.Equal("Hello {{input.name}}", restored.Content, "restored content");
                    PromptTemplate cloned = await ctx.Owner.PromptTemplates.CloneAsync(template.Id, Unique("tplclone")).ConfigureAwait(false);
                    Check.Equal(restored.Content, cloned.Content, "clone content");
                    await ctx.Owner.PromptTemplates.DeleteAsync(cloned.Id).ConfigureAwait(false);
                    EnumerationResult<PromptTemplate> page = await ctx.Owner.PromptTemplates.ListAsync().ConfigureAwait(false);
                    Check.True(page.Objects.Any(t => t.Id == template.Id), "listed");
                }
                finally
                {
                    await ctx.Owner.PromptTemplates.DeleteAsync(template.Id).ConfigureAwait(false);
                }
                await Check.FailsAsync(() => ctx.Owner.PromptTemplates.GetAsync(template.Id), 404).ConfigureAwait(false);
            }).ConfigureAwait(false);

            await runner.RunAsync("Prompt template render (stored and ad hoc)", async () =>
            {
                PromptTemplateRequest create = new PromptTemplateRequest();
                create.Name = Unique("render");
                create.Kind = PromptTemplateKindEnum.Fragment;
                create.Content = "Mr. {{input.name}}";
                PromptTemplate template = await ctx.Owner.PromptTemplates.CreateAsync(create).ConfigureAwait(false);
                try
                {
                    PromptRenderResponse stored = await ctx.Owner.PromptTemplates.RenderAsync(template.Id, new { input = new { name = "Anderson" } }).ConfigureAwait(false);
                    Check.True(stored.Success, "stored render succeeded");
                    Check.Equal("Mr. Anderson", stored.Rendered, "stored render");
                    PromptRenderResponse adHoc = await ctx.Owner.PromptTemplates.RenderContentAsync("I am {{input.who}}", new { input = new { who = "Agent Smith" } }).ConfigureAwait(false);
                    Check.Equal("I am Agent Smith", adHoc.Rendered, "ad hoc render");
                }
                finally
                {
                    await ctx.Owner.PromptTemplates.DeleteAsync(template.Id).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);

            await runner.RunAsync("Request history list, read, summary, and delete", async () =>
            {
                await ctx.Owner.Users.GetMeAsync().ConfigureAwait(false);
                RequestHistoryFilter filter = new RequestHistoryFilter();
                filter.PathContains = "/v1.0/api/users/me";
                filter.PageSize = 5;
                PageResult<RequestHistoryEntry> page = await WaitForHistoryAsync(ctx.Owner, filter).ConfigureAwait(false);
                Check.True(page.Objects.Count > 0, "captured request listed");
                RequestHistoryEntry first = page.Objects.First();
                Check.Equal("GET", first.Method, "method");
                RequestHistoryEntry read = await ctx.Owner.RequestHistory.GetAsync(first.Id).ConfigureAwait(false);
                Check.Equal(first.Id, read.Id, "read entry");
                Check.True(read.Headers.Count > 0, "headers captured");
                Check.True(read.Headers.All(h => !String.Equals(h.Name, "authorization", StringComparison.OrdinalIgnoreCase) || !(h.Value ?? String.Empty).Contains(ctx.Owner.Token!, StringComparison.Ordinal)), "token not stored in clear");
                RequestHistorySummary summary = await ctx.Owner.RequestHistory.GetSummaryAsync(DateTime.UtcNow.AddHours(-1), null, 5).ConfigureAwait(false);
                Check.True(summary.TotalCount > 0, "summary counts");
                await ctx.Owner.RequestHistory.DeleteAsync(first.Id).ConfigureAwait(false);
                await Check.FailsAsync(() => ctx.Owner.RequestHistory.GetAsync(first.Id), 404).ConfigureAwait(false);
                RequestHistoryFilter many = new RequestHistoryFilter();
                many.PathContains = "/v1.0/api/users/me";
                DeleteCountResponse deleted = await ctx.Owner.RequestHistory.DeleteManyAsync(many).ConfigureAwait(false);
                Check.True(deleted.DeletedCount >= 0, "bulk delete count");
            }).ConfigureAwait(false);

            await runner.RunAsync("Settings read by a system administrator only", async () =>
            {
                SettingsView settings = await ctx.Admin.Settings.GetAsync().ConfigureAwait(false);
                Check.True(settings.Fields.Count > 0, "fields");
                SettingField? key = settings.Fields.FirstOrDefault(f => f.IsSecret);
                if (key != null && key.IsSet) Check.True(key.Value != "hugo-default-signing-key-override-me", "secret settings are masked");
                await Check.FailsAsync(() => ctx.Owner.Settings.GetAsync(), 403).ConfigureAwait(false);
            }).ConfigureAwait(false);

            await runner.RunAsync("Typed 404 carries code, message, request id, and status", async () =>
            {
                HugoException e = await Check.FailsAsync(() => ctx.Owner.Secrets.GetAsync("sec_does_not_exist"), 404, "NotFound").ConfigureAwait(false);
                Check.Equal("NotFound", e.Error, "error class");
                Check.NotEmpty(e.Response?.Message, "message");
                Check.NotEmpty(e.RequestId, "request id");
                Check.True(!e.IsRouteNotFound, "a missing record is not a missing route");
            }).ConfigureAwait(false);

            await runner.RunAsync("Unknown route is reported as RouteNotFound", async () =>
            {
                HugoException e = await Check.FailsAsync(() => ctx.Owner.Transport.SendTextAsync(HttpMethod.Get, "/v1.0/api/no-such-route-" + HarnessContext.RandomHex(2), null, null, null, true), 404, "RouteNotFound").ConfigureAwait(false);
                Check.True(e.IsRouteNotFound, "IsRouteNotFound");
                Check.NotEmpty(e.RequestId, "request id (from the x-request-id header)");
            }).ConfigureAwait(false);

            await runner.RunAsync("Cross-tenant read is not found", async () =>
            {
                HugoClient foreign = ctx.ForTenant(ctx.Owner, ctx.Environment.AdminTenantId);
                using (foreign)
                {
                    try
                    {
                        await foreign.Secrets.ListAsync().ConfigureAwait(false);
                        throw new TestAssertionException("a tenant administrator listed another tenant's secrets");
                    }
                    catch (HugoException e)
                    {
                        Check.True(e.StatusCode == 403 || e.StatusCode == 404, "expected 403 or 404, got " + e.StatusCode + " " + e.Code);
                        Check.NotEmpty(e.Code, "error code");
                    }
                }
            }).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static async Task<PageResult<RequestHistoryEntry>> WaitForHistoryAsync(HugoClient client, RequestHistoryFilter filter)
        {
            PageResult<RequestHistoryEntry> page = new PageResult<RequestHistoryEntry>();
            for (int i = 0; i < 20; i++)
            {
                page = await client.RequestHistory.ListAsync(filter).ConfigureAwait(false);
                if (page.Objects.Count > 0) return page;
                await Task.Delay(250).ConfigureAwait(false);
            }
            return page;
        }

        #endregion
    }
}
