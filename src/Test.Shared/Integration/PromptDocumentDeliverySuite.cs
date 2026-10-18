namespace Test.Shared.Integration
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Agent;
    using Hugo.Core.Database;
    using Hugo.Core.Enums;
    using Hugo.Core.Instructions;
    using Hugo.Core.Models;
    using Hugo.Core.Requests;
    using Hugo.Core.Responses;
    using Hugo.Core.Security;
    using Hugo.Core.Serialization;
    using Hugo.Core.Services;
    using Hugo.Server.Services;
    using Test.Shared.Infrastructure;
    using Test.Shared.Agent.StubLlm;
    using Test.Shared.Integration.Mcp;
    using Touchstone.Core;

    /// <summary>Fresh document delivery, source maps and bounded continuation acceptance on both database providers.</summary>
    public static class PromptDocumentDeliverySuite
    {
        /// <summary>Build provider-specific acceptance suites.</summary>
        public static List<TestSuiteDescriptor> Build()
        {
            List<TestSuiteDescriptor> suites = new List<TestSuiteDescriptor>();
            foreach (DatabaseTypeEnum provider in TestEnvironment.Providers())
            {
                string id = "agent-document-delivery-" + provider.ToString().ToLowerInvariant();
                suites.Add(new TestSuiteDescriptor(id, "Prompt document delivery (" + provider + ")", new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(id, "bounded-list-pages", "Metadata pages shrink to their serialized JSON byte budget with progress or a structured single-entry error", ct => ListBudgetAsync(provider, ct)),
                    new TestCaseDescriptor(id, "assembled-context-budget", "Inline context and Reference results cannot invoke a provider beyond its input plus reserved output budget", ct => ContextBudgetAsync(provider, ct)),
                    new TestCaseDescriptor(id, "public-grants-and-clone", "Public attachment delivery requires live explicit grants and cloned pins get fresh private attachment IDs", ct => PublicAsync(provider, ct)),
                    new TestCaseDescriptor(id, "hosted-read", "A hosted model receives the Reference manifest and reads its body through the normal tool loop", ct => HostedAsync(provider, ct)),
                    new TestCaseDescriptor(id, "mcp-read", "MCP discovers current-profile document tools and reads only its attached documents", ct => McpAsync(provider, ct)),
                    new TestCaseDescriptor(id, "fresh-compiler-source-map", "Inline literal text is fresh after edits and removal, while Reference bodies remain absent", ct => CompositionAsync(provider, ct)),
                    new TestCaseDescriptor(id, "utf8-cursor-and-budget", "UTF-8 chunks reconstruct exact content; tampered, wrong-profile and changed-revision cursors fail safely", ct => ChunkAsync(provider, ct)),
                    new TestCaseDescriptor(id, "tool-schema-and-preview", "Strict current-profile tools hide document bodies from progress previews and reject target overrides", ct => ToolsAsync(provider, ct))
                }));
            }
            return suites;
        }

        private static async Task ListBudgetAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                RequestContext principal = await PrincipalAsync(rig, token).ConfigureAwait(false);
                for (int i = 0; i < 5; i++)
                {
                    PromptDocumentView document = await rig.Context.Get<PromptDocumentService>().CreateAsync(principal, rig.TenantId, new PromptDocumentRequest { Name = "page-" + i + ".md", Description = new string('d', 200), Markdown = "Bounded metadata fixture." }, token).ConfigureAwait(false);
                    await AttachAsync(rig, document.Id, "Reference", token).ConfigureAwait(false);
                }
                rig.Context.Settings.PromptDocuments.MaxPromptDocumentReadBytes = 2048;
                PromptDocumentDeliveryService delivery = rig.Context.Get<PromptDocumentDeliveryService>();
                HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
                string? cursor = null;
                int pages = 0;
                do
                {
                    AgentDocumentListResponse page = await delivery.ListAsync(principal, rig.TenantId, rig.Hugo.Id, new AgentDocumentListRequest { Cursor = cursor }, token).ConfigureAwait(false);
                    Assert.True(Encoding.UTF8.GetByteCount(Serializer.Serialize(page)) <= 2048, "actual serialized metadata response respects the configured output byte limit");
                    Assert.True(page.Items.Count > 0, "every nonterminal page makes progress");
                    foreach (AgentPromptDocumentView item in page.Items) Assert.True(seen.Add(item.DocumentId), "pages do not duplicate entries");
                    cursor = page.NextCursor;
                    Assert.True(++pages <= 5, "cursor iteration terminates");
                } while (cursor != null);
                Assert.True(seen.Count == 5 && pages > 1, "all metadata remains discoverable through bounded pages");
                PromptDocumentView large = await rig.Context.Get<PromptDocumentService>().CreateAsync(principal, rig.TenantId, new PromptDocumentRequest { Name = "large-metadata.md", Description = new string('d', 1024), Markdown = "Short body." }, token).ConfigureAwait(false);
                AgentProfile profile = (await rig.Context.Database.AgentProfiles.ReadAsync(rig.TenantId, rig.Hugo.Id, token).ConfigureAwait(false))!;
                await rig.Context.Get<PromptDocumentService>().SaveAttachmentAsync(principal, rig.TenantId, rig.Hugo.Id, null, new AgentPromptDocumentRequest { DocumentId = large.Id, ExpectedDocumentSetVersion = profile.DocumentSetVersion, DisplayOrder = 0 }, token).ConfigureAwait(false);
                List<AgentPromptDocument> all = await rig.Context.Database.AgentPromptDocuments.ListAsync(rig.TenantId, rig.Hugo.Id, token).ConfigureAwait(false);
                profile = (await rig.Context.Database.AgentProfiles.ReadAsync(rig.TenantId, rig.Hugo.Id, token).ConfigureAwait(false))!;
                await rig.Context.Get<PromptDocumentService>().ReorderAsync(principal, rig.TenantId, rig.Hugo.Id, new AgentPromptDocumentOrderRequest { ExpectedDocumentSetVersion = profile.DocumentSetVersion, AttachmentIds = all.OrderByDescending(a => a.DocumentId == large.Id).Select(a => a.Id).ToList() }, token).ConfigureAwait(false);
                rig.Context.Settings.PromptDocuments.MaxPromptDocumentReadBytes = 1024;
                bool rejected = false;
                try { await delivery.ListAsync(principal, rig.TenantId, rig.Hugo.Id, new AgentDocumentListRequest(), token).ConfigureAwait(false); }
                catch (PromptDocumentException error) { rejected = error.Code == "document_read_budget_exceeded"; }
                Assert.True(rejected, "an individual entry that cannot fit returns a structured error rather than an empty continuation loop");
            }
        }

        private static async Task ContextBudgetAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (StubLlmServer stub = new StubLlmServer())
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, settings => settings.Agent.ToolRetrievalThreshold = 1000, token).ConfigureAwait(false))
            {
                RequestContext principal = await PrincipalAsync(rig, token).ConfigureAwait(false);
                PromptDocumentView document = await DocumentAsync(rig, "budget.md", new string('R', 40000), token).ConfigureAwait(false);
                AgentPromptDocumentView attachment = await AttachAsync(rig, document.Id, "Inline", token).ConfigureAwait(false);
                ModelEndpoint endpoint = await HttpTestHelpers.SendAsync<ModelEndpoint>(rig.Anderson, HttpMethod.Post, "/v1.0/api/model-endpoints", new { name = "document-budget-model", provider = "OpenAICompatible", baseUrl = stub.BaseUrl, model = stub.ModelName, maxRetries = 0, contextWindowTokens = 8192, maxOutputTokens = 128 }, 201, token).ConfigureAwait(false);
                await HttpTestHelpers.SendAsync<AgentProfile>(rig.Anderson, HttpMethod.Put, "/v1.0/api/agent-profiles/" + rig.Hugo.Id, new { inferenceEndpointId = endpoint.Id, maxIterations = 4 }, 200, token).ConfigureAwait(false);
                int calls = 0;
                stub.Script.Responder = request => { Interlocked.Increment(ref calls); return StubLlmResponse.ToolCall(AgentDocumentTools.ReadDocument, "{\"documentId\":\"" + document.Id + "\",\"maxBytes\":50000}"); };
                Conversation conversation = await HttpTestHelpers.SendAsync<Conversation>(rig.Anderson, HttpMethod.Post, "/v1.0/api/conversations", new { agentProfileId = rig.Hugo.Id, title = "Already titled budget fixture" }, 201, token).ConfigureAwait(false);
                AgentTurnResult result = await HttpTestHelpers.SendAsync<AgentTurnResult>(rig.Anderson, HttpMethod.Post, "/v1.0/api/conversations/" + conversation.Id + "/messages", new { message = "Read the attached workflow." }, 200, token).ConfigureAwait(false);
                Assert.True(result.ErrorCode == "context_budget_exceeded" && calls == 0, "oversized Inline context fails before any provider invocation; code=" + result.ErrorCode + "; calls=" + calls);
                rig.Context.Settings.PromptDocuments.MaxInlinePromptDocumentBytes = 1024;
                Conversation limited = await HttpTestHelpers.SendAsync<Conversation>(rig.Anderson, HttpMethod.Post, "/v1.0/api/conversations", new { agentProfileId = rig.Hugo.Id, title = "Already titled Inline limit fixture" }, 201, token).ConfigureAwait(false);
                AgentTurnResult limitedResult = await HttpTestHelpers.SendAsync<AgentTurnResult>(rig.Anderson, HttpMethod.Post, "/v1.0/api/conversations/" + limited.Id + "/messages", new { message = "Read the attached workflow." }, 200, token).ConfigureAwait(false);
                Assert.True(limitedResult.ErrorCode == "document_inline_limit" && calls == 0, "document limit code survives the runtime and no provider sees oversized content");
                rig.Context.Settings.PromptDocuments.MaxInlinePromptDocumentBytes = 65536;
                AgentProfile profile = (await rig.Context.Database.AgentProfiles.ReadAsync(rig.TenantId, rig.Hugo.Id, token).ConfigureAwait(false))!;
                await rig.Context.Get<PromptDocumentService>().SaveAttachmentAsync(principal, rig.TenantId, rig.Hugo.Id, attachment.Id, new AgentPromptDocumentRequest { ExpectedVersion = attachment.Version, ExpectedDocumentSetVersion = profile.DocumentSetVersion, DeliveryMode = PromptDocumentDeliveryModeEnum.Reference }, token).ConfigureAwait(false);
                Conversation next = await HttpTestHelpers.SendAsync<Conversation>(rig.Anderson, HttpMethod.Post, "/v1.0/api/conversations", new { agentProfileId = rig.Hugo.Id, title = "Already titled reference budget fixture" }, 201, token).ConfigureAwait(false);
                result = await HttpTestHelpers.SendAsync<AgentTurnResult>(rig.Anderson, HttpMethod.Post, "/v1.0/api/conversations/" + next.Id + "/messages", new { message = "Read the attached workflow." }, 200, token).ConfigureAwait(false);
                Assert.True(result.ErrorCode == "context_budget_exceeded" && calls == 1, "Reference manifest fits, then accumulated read output is rejected before the next provider invocation; code=" + result.ErrorCode + "; calls=" + calls);
            }
        }

        private static async Task PublicAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                PromptDocumentView document = await DocumentAsync(rig, "publication.md", "Pinned publication revision one.", token).ConfigureAwait(false);
                PromptDocumentView revised = await HttpTestHelpers.SendAsync<PromptDocumentView>(rig.Anderson, HttpMethod.Put, "/v1.0/api/prompt-documents/" + document.Id, new { expectedVersion = document.Version, markdown = "Current revision two." }, 200, token).ConfigureAwait(false);
                AgentPromptDocumentView attachment = await HttpTestHelpers.SendAsync<AgentPromptDocumentView>(rig.Anderson, HttpMethod.Post, "/v1.0/api/agent-profiles/" + rig.Hugo.Id + "/documents", new { documentId = document.Id, deliveryMode = "Inline", revisionPolicy = "Pinned", pinnedRevision = 1, allowPublicAccess = true, expectedDocumentSetVersion = 0 }, 201, token).ConfigureAwait(false);
                CredentialCreateResponse credential = await McpFixture.CredentialAsync(rig, "document-publication", rig.Hugo.Id, "TenantAdmin", token).ConfigureAwait(false);
                string profilePath = "/v1.0/api/agent-profiles/" + rig.Hugo.Id;
                TestResponse denied = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Put, profilePath, new { chatEnabled = true, chatAccessMode = "Public", chatCredentialId = credential.Credential.Id }, token).ConfigureAwait(false);
                Assert.Equal(400, denied.StatusCode, "broad document grants cannot substitute for explicit per-document publication grants");
                Role role = new Role { TenantId = rig.TenantId, Name = "PublicationDocumentReader" };
                await rig.Context.Database.Roles.CreateAsync(role, token).ConfigureAwait(false);
                Permission permission = new Permission { TenantId = rig.TenantId, Name = "PublicationDocumentRead", ResourceTypes = new List<string> { "PromptDocument" }, OperationTypes = new List<string> { "Read" } };
                await rig.Context.Database.Permissions.CreateAsync(permission, token).ConfigureAwait(false);
                await rig.Context.Database.RolePermissionMaps.CreateAsync(new RolePermissionMap { TenantId = rig.TenantId, RoleId = role.Id, PermissionId = permission.Id }, token).ConfigureAwait(false);
                CredentialScopeAssignment grant = new CredentialScopeAssignment { TenantId = rig.TenantId, CredentialId = credential.Credential.Id, RoleId = role.Id, ResourceScope = ResourceScopeEnum.Resource, ResourceId = document.Id };
                await rig.Context.Database.CredentialScopeAssignments.CreateAsync(grant, token).ConfigureAwait(false);
                rig.Context.Authorization.Invalidate();
                AgentProfile published = await HttpTestHelpers.SendAsync<AgentProfile>(rig.Anderson, HttpMethod.Put, profilePath, new { chatEnabled = true, chatAccessMode = "Public", chatCredentialId = credential.Credential.Id }, 200, token).ConfigureAwait(false);
                RequestContext owner = await PrincipalAsync(rig, token).ConfigureAwait(false);
                RequestContext visitor = new RequestContext { IsAuthenticated = true, PrincipalType = PrincipalTypeEnum.Credential, TenantId = rig.TenantId, UserId = owner.UserId, User = owner.User, CredentialId = credential.Credential.Id, Credential = credential.Credential, IsPublicChat = true, PublicChatProfileId = rig.Hugo.Id, PublicChatRevision = (await rig.Context.Database.AgentProfiles.ReadAsync(rig.TenantId, published.Id, token).ConfigureAwait(false))!.ChatRevision };
                PromptDocumentDeliveryService delivery = rig.Context.Get<PromptDocumentDeliveryService>();
                AgentDocumentReadResponse read = await delivery.ReadAsync(visitor, rig.TenantId, rig.Hugo.Id, new AgentDocumentReadRequest { DocumentId = document.Id }, null, token).ConfigureAwait(false);
                Assert.Equal("Pinned publication revision one.", read.Markdown, "public delivery honors immutable pinned revision rather than Latest");
                AgentProfile copy = await HttpTestHelpers.SendAsync<AgentProfile>(rig.Anderson, HttpMethod.Post, profilePath + "/duplicate", new { name = "private-document-copy" }, 201, token).ConfigureAwait(false);
                AgentPromptDocumentList attachments = await HttpTestHelpers.SendAsync<AgentPromptDocumentList>(rig.Anderson, HttpMethod.Get, "/v1.0/api/agent-profiles/" + copy.Id + "/documents", null, 200, token).ConfigureAwait(false);
                Assert.True(attachments.Items.Count == 1 && attachments.Items[0].Id != attachment.Id && !attachments.Items[0].AllowPublicAccess && attachments.Items[0].PinnedRevision == 1 && attachments.Items[0].DeliveryMode == PromptDocumentDeliveryModeEnum.Inline, "clone preserves pin and mode but resets publication opt-in and identifiers");
                Assert.True(!copy.ChatEnabled, "cloned profile remains unpublished");
                User reader = new User { TenantId = rig.TenantId, Email = "document-cloner@fixture.local", Active = true };
                await rig.Context.Database.Users.CreateAsync(reader, token).ConfigureAwait(false);
                Role profileRole = new Role { TenantId = rig.TenantId, Name = "ProfileOnlyCloner" };
                await rig.Context.Database.Roles.CreateAsync(profileRole, token).ConfigureAwait(false);
                Permission profilePermission = new Permission { TenantId = rig.TenantId, Name = "ProfileCloneOnly", ResourceTypes = new List<string> { "AgentProfile" }, OperationTypes = new List<string> { "All" } };
                await rig.Context.Database.Permissions.CreateAsync(profilePermission, token).ConfigureAwait(false);
                await rig.Context.Database.RolePermissionMaps.CreateAsync(new RolePermissionMap { TenantId = rig.TenantId, RoleId = profileRole.Id, PermissionId = profilePermission.Id }, token).ConfigureAwait(false);
                await rig.Context.Database.UserRoleAssignments.CreateAsync(new UserRoleAssignment { TenantId = rig.TenantId, UserId = reader.Id, RoleId = profileRole.Id }, token).ConfigureAwait(false);
                RequestContext cloner = new RequestContext { IsAuthenticated = true, PrincipalType = PrincipalTypeEnum.User, TenantId = rig.TenantId, UserId = reader.Id, User = reader };
                bool deniedClone = false;
                try { await rig.Context.Get<AgentProfileService>().DuplicateAsync(cloner, rig.TenantId, rig.Hugo.Id, "forbidden-document-copy", token).ConfigureAwait(false); }
                catch (ForbiddenException) { deniedClone = true; }
                Assert.True(deniedClone && await rig.Context.Database.AgentProfiles.ReadByNameAsync(rig.TenantId, "forbidden-document-copy", token).ConfigureAwait(false) == null, "profile-only permission cannot clone unreadable attachments or leave a partial copy");
                Role denialRole = new Role { TenantId = rig.TenantId, Name = "DocumentOwnerCeilingDeny" };
                await rig.Context.Database.Roles.CreateAsync(denialRole, token).ConfigureAwait(false);
                Permission denial = new Permission { TenantId = rig.TenantId, Name = "DocumentOwnerReadDeny", PermissionType = PermissionTypeEnum.Deny, ResourceTypes = new List<string> { "PromptDocument" }, OperationTypes = new List<string> { "Read" } };
                await rig.Context.Database.Permissions.CreateAsync(denial, token).ConfigureAwait(false);
                await rig.Context.Database.RolePermissionMaps.CreateAsync(new RolePermissionMap { TenantId = rig.TenantId, RoleId = denialRole.Id, PermissionId = denial.Id }, token).ConfigureAwait(false);
                await rig.Context.Database.UserRoleAssignments.CreateAsync(new UserRoleAssignment { TenantId = rig.TenantId, UserId = owner.UserId!, RoleId = denialRole.Id, ResourceScope = ResourceScopeEnum.Resource, ResourceId = document.Id }, token).ConfigureAwait(false);
                User storedOwner = owner.User!; storedOwner.IsTenantAdmin = false; storedOwner.IsAdmin = false;
                await rig.Context.Database.Users.UpdateAsync(storedOwner, token).ConfigureAwait(false);
                bool ownerDenied = false;
                try { await delivery.ReadAsync(visitor, rig.TenantId, rig.Hugo.Id, new AgentDocumentReadRequest { DocumentId = document.Id }, null, token).ConfigureAwait(false); }
                catch (NotFoundException) { ownerDenied = true; }
                catch (ForbiddenException) { ownerDenied = true; }
                Assert.True(ownerDenied, "credential's explicit grant cannot exceed its freshly loaded owner's denied document permissions");
                storedOwner.IsTenantAdmin = true;
                await rig.Context.Database.Users.UpdateAsync(storedOwner, token).ConfigureAwait(false);
                await rig.Context.Database.CredentialScopeAssignments.DeleteAsync(grant.Id, token).ConfigureAwait(false);
                bool revoked = false;
                try { await delivery.ReadAsync(visitor, rig.TenantId, rig.Hugo.Id, new AgentDocumentReadRequest { DocumentId = document.Id }, null, token).ConfigureAwait(false); }
                catch (NotFoundException) { revoked = true; }
                catch (ForbiddenException) { revoked = true; }
                Assert.True(revoked, "revoking explicit grant blocks the next chunk despite remaining TenantAdmin wildcard permissions");
            }
        }

        private static async Task HostedAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (StubLlmServer stub = new StubLlmServer())
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, settings => settings.Agent.ToolRetrievalThreshold = 1000, token).ConfigureAwait(false))
            {
                const string body = "Reference workflow: inspect the maintenance ledger before changing configuration.";
                PromptDocumentView document = await DocumentAsync(rig, "hosted.md", body, token).ConfigureAwait(false);
                await AttachAsync(rig, document.Id, "Reference", token).ConfigureAwait(false);
                ModelEndpoint endpoint = await HttpTestHelpers.SendAsync<ModelEndpoint>(rig.Anderson, HttpMethod.Post, "/v1.0/api/model-endpoints", new { name = "document-model", provider = "OpenAICompatible", baseUrl = stub.BaseUrl, model = stub.ModelName, maxRetries = 0 }, 201, token).ConfigureAwait(false);
                await HttpTestHelpers.SendAsync<AgentProfile>(rig.Anderson, HttpMethod.Put, "/v1.0/api/agent-profiles/" + rig.Hugo.Id, new { inferenceEndpointId = endpoint.Id, maxIterations = 4 }, 200, token).ConfigureAwait(false);
                int calls = 0;
                stub.Script.Responder = request =>
                {
                    if (request.Messages.Any(m => m.Content != null && m.Content.Contains("Write a short title", StringComparison.Ordinal))) return StubLlmResponse.Message("Document fixture");
                    Assert.True(request.ToolNames.Contains(AgentDocumentTools.ReadDocument), "hosted loop offers the scoped read tool");
                    int step = Interlocked.Increment(ref calls);
                    if (step == 1)
                    {
                        Assert.True(request.Messages.Any(m => m.Role == "system" && m.Content != null && m.Content.Contains(document.Id, StringComparison.Ordinal)), "fresh manifest is available before the first planning call");
                        Assert.True(!request.Messages.Any(m => m.Content != null && m.Content.Contains(body, StringComparison.Ordinal)), "Reference body is absent before tool retrieval");
                        return StubLlmResponse.ToolCall(AgentDocumentTools.ReadDocument, "{\"documentId\":\"" + document.Id + "\"}");
                    }
                    Assert.True(request.Messages.Any(m => m.Role == "tool" && m.Content != null && m.Content.Contains(body, StringComparison.Ordinal)), "authorized result enters actual model history");
                    return StubLlmResponse.Message("The maintenance ledger comes first.");
                };
                Conversation conversation = await HttpTestHelpers.SendAsync<Conversation>(rig.Anderson, HttpMethod.Post, "/v1.0/api/conversations", new { agentProfileId = rig.Hugo.Id, title = "Already titled document fixture" }, 201, token).ConfigureAwait(false);
                AgentTurnResult result = await HttpTestHelpers.SendAsync<AgentTurnResult>(rig.Anderson, HttpMethod.Post, "/v1.0/api/conversations/" + conversation.Id + "/messages", new { message = "Read the attached workflow and state the first step." }, 200, token).ConfigureAwait(false);
                Assert.True(calls == 2 && result.FinalText.Contains("ledger", StringComparison.Ordinal), "normal hosted retrieval completes successfully");
                calls = 0;
                TestResponse openai = await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Post, "/v1.0/openai/v1/chat/completions", new { model = rig.Hugo.Name, stream = false, messages = new[] { new { role = "user", content = "Read the attached workflow and state the first step." } } }, token).ConfigureAwait(false);
                Assert.True(openai.StatusCode == 200 && openai.Body.Contains("ledger", StringComparison.Ordinal) && calls == 2, "OpenAI compatibility uses the same scoped document retrieval loop: " + openai.Body);
                calls = 0;
                using (Voltaic.A2A.A2AClient client = new Voltaic.A2A.A2AClient(rig.Host.BaseUrl + "/v1.0/a2a/" + rig.TenantId + "/" + rig.Hugo.Name, rig.Anderson))
                {
                    Voltaic.A2A.SendMessageRequest message = new Voltaic.A2A.SendMessageRequest();
                    message.Message.Role = Voltaic.A2A.Role.User;
                    message.Message.MessageId = Guid.NewGuid().ToString("N");
                    message.Message.Parts.Add(Voltaic.A2A.Part.FromText("Read the attached workflow and state the first step."));
                    Voltaic.A2A.SendMessageResponse response = await client.SendMessageAsync(message, token).ConfigureAwait(false);
                    Assert.True(response.Task?.Status.State == Voltaic.A2A.TaskState.Completed && calls == 2, "A2A executes the shared scoped document tool loop");
                }
            }
        }

        private static async Task McpAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (McpFixture fixture = await McpFixture.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                PromptDocumentView document = await DocumentAsync(fixture.Rig, "mcp.md", "MCP authorized attached guidance.", token).ConfigureAwait(false);
                await AttachAsync(fixture.Rig, document.Id, "Reference", token).ConfigureAwait(false);
                using (McpTestClient client = await fixture.ConnectAsync(fixture.AndersonHeaders(), token, "/mcp/Hugo").ConfigureAwait(false))
                {
                    McpTestToolList tools = await client.ListToolsAsync(token).ConfigureAwait(false);
                    Assert.True(tools.Find(AgentDocumentTools.ListDocuments) != null && tools.Find(AgentDocumentTools.ReadDocument) != null, "MCP discovers scoped document tools");
                    McpTestToolResult read = await client.CallToolAsync(AgentDocumentTools.ReadDocument, new { documentId = document.Id }, token).ConfigureAwait(false);
                    Assert.True(read.IsError != true && read.Text.Contains("MCP authorized attached guidance.", StringComparison.Ordinal), "MCP reads the selected profile document");
                }
                using (McpTestClient other = await fixture.ConnectAsync(fixture.AndersonHeaders(), token, "/mcp/" + fixture.Ops.Name).ConfigureAwait(false))
                {
                    McpTestToolResult read = await other.CallToolAsync(AgentDocumentTools.ReadDocument, new { documentId = document.Id }, token).ConfigureAwait(false);
                    Assert.True(read.IsError == true, "an unattached document cannot cross selected profiles");
                }
            }
        }

        private static async Task<RequestContext> PrincipalAsync(IntegrationRig rig, CancellationToken token)
        {
            User user = (await rig.Context.Database.Users.ReadByEmailAsync(rig.TenantId, "anderson@hugo.local", token).ConfigureAwait(false))!;
            return new RequestContext { IsAuthenticated = true, TenantId = rig.TenantId, UserId = user.Id, User = user, PrincipalType = PrincipalTypeEnum.User, IsTenantAdmin = true };
        }

        private static Task<PromptDocumentView> DocumentAsync(IntegrationRig rig, string name, string content, CancellationToken token)
        {
            return HttpTestHelpers.SendAsync<PromptDocumentView>(rig.Anderson, HttpMethod.Post, "/v1.0/api/prompt-documents", new { name, description = "Fixture workflow guidance.", markdown = content }, 201, token);
        }

        private static async Task<AgentPromptDocumentView> AttachAsync(IntegrationRig rig, string documentId, string mode, CancellationToken token)
        {
            AgentProfile profile = (await rig.Context.Database.AgentProfiles.ReadAsync(rig.TenantId, rig.Hugo.Id, token).ConfigureAwait(false))!;
            return await HttpTestHelpers.SendAsync<AgentPromptDocumentView>(rig.Anderson, HttpMethod.Post, "/v1.0/api/agent-profiles/" + profile.Id + "/documents", new { documentId, deliveryMode = mode, expectedDocumentSetVersion = profile.DocumentSetVersion }, 201, token).ConfigureAwait(false);
        }

        private static async Task CompositionAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                RequestContext principal = await PrincipalAsync(rig, token).ConfigureAwait(false);
                const string literal = "Workflow literal {{tenant.secret}} and {{> forbidden.include}} remain uninterpreted.";
                PromptDocumentView inline = await DocumentAsync(rig, "workflow.md", literal, token).ConfigureAwait(false);
                AgentPromptDocumentView attachment = await AttachAsync(rig, inline.Id, "Inline", token).ConfigureAwait(false);
                PromptDocumentView reference = await DocumentAsync(rig, "reference.txt", "REFERENCE_BODY_MUST_NOT_APPEAR_IN_PROMPT", token).ConfigureAwait(false);
                await AttachAsync(rig, reference.Id, "Reference", token).ConfigureAwait(false);
                InstructionCompiler compiler = rig.Context.Get<InstructionCompiler>();
                CompiledInstructions first = await compiler.CompileAsync(rig.TenantId, rig.Hugo.Id, principal, InvocationSurfaceEnum.Chat, token).ConfigureAwait(false);
                Assert.True(principal.SuppressHistoryBodies, "compiled document bodies are excluded from request-history capture too");
                Assert.True(first.Text.Contains(literal, StringComparison.Ordinal), "document braces are literal text");
                Assert.True(!first.Text.Contains("REFERENCE_BODY_MUST_NOT_APPEAR_IN_PROMPT", StringComparison.Ordinal), "Reference content never enters compilation");
                InstructionSegment segment = first.Segments.Single(s => s.DocumentId == inline.Id);
                Assert.True(first.Text.Substring(segment.Start, segment.Length).Contains(literal, StringComparison.Ordinal), "source map offsets identify actual document content");
                await HttpTestHelpers.SendAsync<PromptDocumentView>(rig.Anderson, HttpMethod.Put, "/v1.0/api/prompt-documents/" + inline.Id, new { expectedVersion = inline.Version, markdown = "Fresh replacement workflow." }, 200, token).ConfigureAwait(false);
                CompiledInstructions second = await compiler.CompileAsync(rig.TenantId, rig.Hugo.Id, principal, InvocationSurfaceEnum.Chat, token).ConfigureAwait(false);
                Assert.True(second.Text.Contains("Fresh replacement workflow.", StringComparison.Ordinal) && !second.Text.Contains(literal, StringComparison.Ordinal), "Latest revision replaces cached content");
                AgentProfile current = (await rig.Context.Database.AgentProfiles.ReadAsync(rig.TenantId, rig.Hugo.Id, token).ConfigureAwait(false))!;
                using (HttpResponseMessage response = await rig.Anderson.DeleteAsync("/v1.0/api/agent-profiles/" + rig.Hugo.Id + "/documents/" + attachment.Id + "?expectedVersion=" + attachment.Version + "&expectedDocumentSetVersion=" + current.DocumentSetVersion, token).ConfigureAwait(false)) Assert.Equal(204, (int)response.StatusCode, "detach returns no content");
                CompiledInstructions third = await compiler.CompileAsync(rig.TenantId, rig.Hugo.Id, principal, InvocationSurfaceEnum.Chat, token).ConfigureAwait(false);
                Assert.True(!third.Text.Contains("Fresh replacement workflow.", StringComparison.Ordinal), "detached content disappears from next compilation");
            }
        }

        private static async Task ChunkAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                RequestContext principal = await PrincipalAsync(rig, token).ConfigureAwait(false);
                string content = String.Concat(Enumerable.Repeat("A😀漢字\n", 160));
                PromptDocumentView document = await DocumentAsync(rig, "unicode.md", content, token).ConfigureAwait(false);
                await AttachAsync(rig, document.Id, "Reference", token).ConfigureAwait(false);
                PromptDocumentDeliveryService delivery = rig.Context.Get<PromptDocumentDeliveryService>();
                AgentTurnRequest turn = new AgentTurnRequest { TenantId = rig.TenantId, ProfileId = rig.Hugo.Id, PrincipalId = principal.PrincipalId };
                AgentDocumentReadResponse chunk = await delivery.ReadAsync(principal, rig.TenantId, rig.Hugo.Id, new AgentDocumentReadRequest { DocumentId = document.Id, MaxBytes = 103 }, turn, token).ConfigureAwait(false);
                string cursor = chunk.NextCursor!;
                StringBuilder text = new StringBuilder(chunk.Markdown);
                while (!chunk.Complete)
                {
                    chunk = await delivery.ReadAsync(principal, rig.TenantId, rig.Hugo.Id, new AgentDocumentReadRequest { Cursor = chunk.NextCursor, MaxBytes = 103 }, turn, token).ConfigureAwait(false);
                    Assert.Equal(Encoding.UTF8.GetByteCount(chunk.Markdown), chunk.ReturnedBytes, "returned bytes describe complete UTF-8 characters");
                    text.Append(chunk.Markdown);
                }
                Assert.Equal(content, text.ToString(), "chunks reconstruct exact content without replacement characters");
                bool rejected = false;
                try { await delivery.ReadAsync(principal, rig.TenantId, rig.Hugo.Id, new AgentDocumentReadRequest { Cursor = cursor.Substring(0, cursor.Length - 5) + "xxxxx" }, turn, token).ConfigureAwait(false); }
                catch (ValidationException) { rejected = true; }
                Assert.True(rejected, "tampered cursor is rejected safely");
                AgentProfile other = await HttpTestHelpers.SendAsync<AgentProfile>(rig.Anderson, HttpMethod.Post, "/v1.0/api/agent-profiles", new { name = "other-document-agent" }, 201, token).ConfigureAwait(false);
                rejected = false;
                try { await delivery.ReadAsync(principal, rig.TenantId, other.Id, new AgentDocumentReadRequest { Cursor = cursor }, null, token).ConfigureAwait(false); }
                catch (ValidationException) { rejected = true; }
                Assert.True(rejected, "cursor cannot cross profiles");
                await HttpTestHelpers.SendAsync<PromptDocumentView>(rig.Anderson, HttpMethod.Put, "/v1.0/api/prompt-documents/" + document.Id, new { expectedVersion = document.Version, markdown = content + "New revision." }, 200, token).ConfigureAwait(false);
                rejected = false;
                try { await delivery.ReadAsync(principal, rig.TenantId, rig.Hugo.Id, new AgentDocumentReadRequest { Cursor = cursor }, turn, token).ConfigureAwait(false); }
                catch (PromptDocumentException error) { rejected = error.Code == "document_version_changed"; }
                Assert.True(rejected, "Latest change prevents mixed-revision continuation");
                rig.Context.Settings.PromptDocuments.MaxPromptDocumentBytesPerTurn = 1024;
                rejected = false;
                try { await delivery.ReadAsync(principal, rig.TenantId, rig.Hugo.Id, new AgentDocumentReadRequest { DocumentId = document.Id, MaxBytes = 1000 }, new AgentTurnRequest(), token).ConfigureAwait(false); }
                catch (PromptDocumentException error) { rejected = error.Code == "document_read_budget_exceeded"; }
                Assert.True(rejected, "serialized metadata and content count against turn budget");
            }
        }

        private static async Task ToolsAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                RequestContext principal = await PrincipalAsync(rig, token).ConfigureAwait(false);
                PromptDocumentView document = await DocumentAsync(rig, "tools.md", "Sensitive authorized workflow prose.", token).ConfigureAwait(false);
                await AttachAsync(rig, document.Id, "Reference", token).ConfigureAwait(false);
                AgentDocumentTools tools = rig.Context.Get<AgentDocumentTools>();
                AgentTurnRequest turn = new AgentTurnRequest { TenantId = rig.TenantId, ProfileId = rig.Hugo.Id };
                AgentToolExecution result = (await tools.ExecuteAsync(principal, turn, new AgentToolCall { ToolName = AgentDocumentTools.ReadDocument, ArgumentsJson = "{\"documentId\":\"" + document.Id + "\"}" }, token).ConfigureAwait(false))!;
                Assert.True(!result.Result.IsError && result.SuppressOutputPreview, "authorized body is returned only to model history with progress preview suppressed");
                result = (await tools.ExecuteAsync(principal, turn, new AgentToolCall { ToolName = AgentDocumentTools.ReadDocument, ArgumentsJson = "{\"documentId\":\"" + document.Id + "\",\"profileId\":\"another\"}" }, token).ConfigureAwait(false))!;
                Assert.True(result.Result.IsError && result.DecisionCode == "invalid_argument", "callers cannot override profile targeting");
                PromptDocumentPreview preview = await rig.Context.Get<PromptDocumentDeliveryService>().PreviewAsync(principal, rig.TenantId, rig.Hugo.Id, "Authenticated", token).ConfigureAwait(false);
                Assert.True(!preview.CompiledText.Contains("Sensitive authorized workflow prose.", StringComparison.Ordinal), "Reference preview remains metadata-only");
            }
        }
    }
}
