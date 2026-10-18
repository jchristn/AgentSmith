namespace Test.Shared.Integration
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Responses;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;

    /// <summary>Public HTTP validation, confidentiality, and optimistic document management contracts.</summary>
    public static class PromptDocumentRouteContractSuite
    {
        /// <summary>Create provider-specific route tests.</summary><returns>Shared cases.</returns>
        public static List<TestSuiteDescriptor> Build()
        {
            return TestEnvironment.Providers().Select(provider =>
            {
                string id = "agent-document-routes-" + provider.ToString().ToLowerInvariant();
                return new TestSuiteDescriptor(id, "Document route contracts", new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(id, "strict-input", "Unknown fields, enum/version syntax and body limits emit safe documented errors", ct => InputAsync(provider, ct)),
                    new TestCaseDescriptor(id, "cas-lifecycle", "Metadata/revision/attachment routes enforce CAS and deletion lifecycle", ct => LifecycleAsync(provider, ct)),
                    new TestCaseDescriptor(id, "literal-credentials", "Document create and edit preserve credential-shaped documentation as literal text", ct => LiteralCredentialsAsync(provider, ct)),
                    new TestCaseDescriptor(id, "history-redaction", "Markdown never enters HTTP request history bodies", ct => HistoryAsync(provider, ct))
                });
            }).ToList();
        }

        private static async Task InputAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                TestResponse unknown = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Post, "/v1.0/api/prompt-documents", new { name = "Unknown.md", description = "Synthetic schema", markdown = "PRIVATE_MARKDOWN_SENTINEL", arbitrary = "PRIVATE_FIELD_SENTINEL" }, token).ConfigureAwait(false);
                Assert.Equal(400, unknown.StatusCode, "Unknown field rejected");
                Assert.True(!unknown.Body.Contains("PRIVATE_MARKDOWN_SENTINEL", StringComparison.Ordinal) && !unknown.Body.Contains("PRIVATE_FIELD_SENTINEL", StringComparison.Ordinal), "Invalid body error never echoes content");
                TestResponse enumError = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Post, "/v1.0/api/prompt-documents", new { name = "Enum.md", description = "Synthetic schema", markdown = "body", visibility = "Everything" }, token).ConfigureAwait(false);
                Assert.Equal(400, enumError.StatusCode, "Invalid enum rejected");
                TestResponse tooLarge = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Post, "/v1.0/api/prompt-documents", new { name = "Large.md", description = "Synthetic schema", markdown = new string('x', 262145) }, token).ConfigureAwait(false);
                Assert.Equal(422, tooLarge.StatusCode, "Canonical document limit");
                TestResponse transport = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Post, "/v1.0/api/prompt-documents", new { name = "Transport.md", description = "Synthetic schema", markdown = new string('x', 1703937) }, token).ConfigureAwait(false);
                Assert.Equal(413, transport.StatusCode, "Bounded HTTP body");
                PromptDocumentView d = await CreateAsync(rig, "Versions.md", "body", token).ConfigureAwait(false);
                TestResponse fractional = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Put, "/v1.0/api/prompt-documents/" + d.Id, new { expectedVersion = 1.5, markdown = "changed" }, token).ConfigureAwait(false);
                Assert.Equal(400, fractional.StatusCode, "Fractional CAS token rejected");
                TestResponse query = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Delete, "/v1.0/api/prompt-documents/" + d.Id + "?expectedVersion=9007199254740992", null, token).ConfigureAwait(false);
                Assert.Equal(400, query.StatusCode, "Unsafe integer query version rejected");
                TestResponse safeMaximum = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Put, "/v1.0/api/prompt-documents/" + d.Id, new { expectedVersion = 9007199254740991L, markdown = "stale safe maximum" }, token).ConfigureAwait(false);
                Assert.Equal(409, safeMaximum.StatusCode, "Maximum JSON-safe version is syntactically valid and conflicts as stale");
                TestResponse safeDelete = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Delete, "/v1.0/api/prompt-documents/" + d.Id + "?expectedVersion=9007199254740991", null, token).ConfigureAwait(false);
                Assert.Equal(409, safeDelete.StatusCode, "Maximum JSON-safe delete version conflicts as stale");
                TestResponse omitted = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Put, "/v1.0/api/prompt-documents/" + d.Id, new { markdown = "changed" }, token).ConfigureAwait(false);
                Assert.Equal(400, omitted.StatusCode, "Missing CAS token rejected");
            }
        }

        private static async Task LifecycleAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                PromptDocumentView d = await CreateAsync(rig, "Lifecycle.md", "# Original\r\n{{literal}} 世界  \r\n", token).ConfigureAwait(false);
                PromptDocumentRevision revision = await HttpTestHelpers.SendAsync<PromptDocumentRevision>(rig.Anderson, HttpMethod.Get, "/v1.0/api/prompt-documents/" + d.Id + "/revisions/1", null, 200, token).ConfigureAwait(false);
                Assert.Equal("# Original\n{{literal}} 世界  \n", revision.Markdown, "Canonical body read");
                PromptDocumentView saved = await HttpTestHelpers.SendAsync<PromptDocumentView>(rig.Anderson, HttpMethod.Put, "/v1.0/api/prompt-documents/" + d.Id, new { expectedVersion = 1, markdown = "# Revised" }, 200, token).ConfigureAwait(false);
                Assert.Equal(2L, saved.Version, "Document version advances");
                TestResponse stale = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Put, "/v1.0/api/prompt-documents/" + d.Id, new { expectedVersion = 1, markdown = "overwrite" }, token).ConfigureAwait(false);
                Assert.Equal(409, stale.StatusCode, "Stale replacement conflicts");
                string path = "/v1.0/api/agent-profiles/" + rig.Hugo.Id + "/documents";
                AgentPromptDocumentView attachment = await HttpTestHelpers.SendAsync<AgentPromptDocumentView>(rig.Anderson, HttpMethod.Post, path, new { documentId = d.Id, expectedDocumentSetVersion = 0, deliveryMode = "Reference", revisionPolicy = "Pinned", pinnedRevision = 1 }, 201, token).ConfigureAwait(false);
                Assert.Equal(1L, attachment.DocumentSetVersion, "Attachment set advances");
                AgentPromptDocumentList list = await HttpTestHelpers.SendAsync<AgentPromptDocumentList>(rig.Anderson, HttpMethod.Get, path, null, 200, token).ConfigureAwait(false);
                Assert.Equal(1, list.Items.Single().Revision, "Pinned selection exposed");
                TestResponse inUse = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Delete, "/v1.0/api/prompt-documents/" + d.Id + "?expectedVersion=2", null, token).ConfigureAwait(false);
                Assert.Equal(409, inUse.StatusCode, "Attached deletion conflicts");
                PromptDocumentReferrers refs = await HttpTestHelpers.SendAsync<PromptDocumentReferrers>(rig.Anderson, HttpMethod.Get, "/v1.0/api/prompt-documents/" + d.Id + "/referrers", null, 200, token).ConfigureAwait(false);
                Assert.Equal(rig.Hugo.Id, refs.Items.Single().ProfileId, "Authorized referrer");
                Assert.Equal(204, (await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Delete, path + "/" + attachment.Id + "?expectedVersion=1&expectedDocumentSetVersion=1", null, token).ConfigureAwait(false)).StatusCode, "Detach status");
                Assert.Equal(204, (await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Delete, "/v1.0/api/prompt-documents/" + d.Id + "?expectedVersion=2", null, token).ConfigureAwait(false)).StatusCode, "Delete status");
                TestResponse gone = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Get, "/v1.0/api/prompt-documents/" + d.Id, null, token).ConfigureAwait(false);
                Assert.Equal(404, gone.StatusCode, "Deleted document hidden");
            }
        }

        private static async Task LiteralCredentialsAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                string example = "# Synthetic token syntax\nExample: sk-" + new string('x', 32) + "\n";
                PromptDocumentView document = await CreateAsync(rig, "TokenSyntax.md", example, token).ConfigureAwait(false);
                PromptDocumentRevision initial = await HttpTestHelpers.SendAsync<PromptDocumentRevision>(rig.Anderson, HttpMethod.Get, "/v1.0/api/prompt-documents/" + document.Id + "/revisions/1", null, 200, token).ConfigureAwait(false);
                Assert.Equal(example, initial.Markdown, "Credential-shaped examples are stored unchanged on create");
                string edited = example + "Another example: ghp_" + new string('y', 36);
                PromptDocumentView updated = await HttpTestHelpers.SendAsync<PromptDocumentView>(rig.Anderson, HttpMethod.Put, "/v1.0/api/prompt-documents/" + document.Id, new { expectedVersion = document.Version, markdown = edited }, 200, token).ConfigureAwait(false);
                PromptDocumentRevision revision = await HttpTestHelpers.SendAsync<PromptDocumentRevision>(rig.Anderson, HttpMethod.Get, "/v1.0/api/prompt-documents/" + document.Id + "/revisions/" + updated.CurrentRevision, null, 200, token).ConfigureAwait(false);
                Assert.Equal(edited, revision.Markdown, "Credential-shaped examples are stored unchanged on edit");
            }
        }

        private static async Task HistoryAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                PromptDocumentView d = await CreateAsync(rig, "History.md", "PRIVATE_HISTORY_MARKDOWN_SENTINEL", token).ConfigureAwait(false);
                await HttpTestHelpers.SendAsync<PromptDocumentRevision>(rig.Anderson, HttpMethod.Get, "/v1.0/api/prompt-documents/" + d.Id + "/revisions/1", null, 200, token).ConfigureAwait(false);
                using (HttpClient anonymous = new HttpClient { BaseAddress = new Uri(rig.Host.BaseUrl) })
                {
                    TestResponse denied = await HttpTestHelpers.SendAsync(anonymous, HttpMethod.Post, "/v1.0/api/prompt-documents", new { name = "Rejected.md", description = "Rejected upload", markdown = "PRIVATE_HISTORY_MARKDOWN_SENTINEL" }, token).ConfigureAwait(false);
                    Assert.Equal(401, denied.StatusCode, "Anonymous upload rejected before route handler");
                }
                Assert.True(Hugo.Server.Services.RequestHistoryCaptureService.IsSecretRoute("/v1.0/api/agent-profiles/example/documents/preview"), "Attachment previews have global body redaction even before handlers run");
                RequestHistoryPage page = new RequestHistoryPage();
                for (int attempt = 0; attempt < 40; attempt++)
                {
                    page = await HttpTestHelpers.SendAsync<RequestHistoryPage>(rig.Host.AdminClient, HttpMethod.Get, "/v1.0/api/request-history?pageSize=100&pathContains=prompt-documents", null, 200, token).ConfigureAwait(false);
                    if (page.Objects.Count >= 3 && page.Objects.Any(entry => entry.StatusCode == 401)) break;
                    await Task.Delay(25, token).ConfigureAwait(false);
                }
                Assert.True(page.Objects.Count >= 3 && page.Objects.Any(entry => entry.StatusCode == 401), "History captures authorized and pre-handler rejected requests");
                foreach (RequestHistoryEntry entry in page.Objects)
                {
                    RequestHistoryEntry detail = await HttpTestHelpers.SendAsync<RequestHistoryEntry>(rig.Host.AdminClient, HttpMethod.Get, "/v1.0/api/request-history/" + entry.Id, null, 200, token).ConfigureAwait(false);
                    Assert.True(!(detail.RequestBody ?? String.Empty).Contains("PRIVATE_HISTORY_MARKDOWN_SENTINEL", StringComparison.Ordinal) && !(detail.ResponseBody ?? String.Empty).Contains("PRIVATE_HISTORY_MARKDOWN_SENTINEL", StringComparison.Ordinal), "Markdown omitted from request and response capture");
                }
            }
        }

        private static Task<PromptDocumentView> CreateAsync(IntegrationRig rig, string name, string markdown, CancellationToken token)
        {
            return HttpTestHelpers.SendAsync<PromptDocumentView>(rig.Anderson, HttpMethod.Post, "/v1.0/api/prompt-documents", new { name, description = "Synthetic schema reference.", markdown, visibility = "Tenant" }, 201, token);
        }
    }
}
