namespace Test.Shared.Integration
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Agent;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Responses;
    using Test.Shared.Infrastructure;
    using Test.Shared.Integration.Mcp;
    using Touchstone.Core;

    /// <summary>Feedback management pagination, request association and principal isolation on both database providers.</summary>
    public static class FeedbackReviewSuite
    {
        /// <summary>Build provider-specific feedback review cases.</summary>
        /// <returns>Suites.</returns>
        public static List<TestSuiteDescriptor> Build()
        {
            return TestEnvironment.Providers().Select(provider =>
            {
                string id = "feedback-review-" + provider.ToString().ToLowerInvariant();
                return new TestSuiteDescriptor(id, "Feedback review (" + provider + ")", new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(id, "association-pagination", "Feedback pages preserve exact rated response, user request, telemetry and clearing semantics", ct => AssociationAsync(provider, ct)),
                    new TestCaseDescriptor(id, "isolation", "Feedback management honors tenant, profile visibility, credential ownership and Read permissions", ct => IsolationAsync(provider, ct))
                });
            }).ToList();
        }

        private static async Task AssociationAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                Conversation conversation = await HttpTestHelpers.SendAsync<Conversation>(rig.Anderson, HttpMethod.Post, "/v1.0/api/conversations", new { agentProfileId = rig.Hugo.Id }, 201, token).ConfigureAwait(false);
                List<ConversationMessage> messages = new List<ConversationMessage>
                {
                    new ConversationMessage { Role = AgentMessageRoleEnum.User, TurnId = "turn-a", Content = "Exact first request" },
                    new ConversationMessage { Role = AgentMessageRoleEnum.Assistant, TurnId = "turn-a", Content = "Tool planning", ToolCalls = new List<ConversationToolCall> { new ConversationToolCall { CallId = "call-one", Name = "echo_read", ArgumentsJson = "{}" } } },
                    new ConversationMessage { Role = AgentMessageRoleEnum.Assistant, TurnId = "turn-a", Content = "Rated first response", ThinkingContent = "private reasoning", InputTokens = 111, OutputTokens = 22, TimeToFirstTokenMs = 18, DurationMs = 234, FinishReason = "completed" },
                    new ConversationMessage { Role = AgentMessageRoleEnum.User, Content = "Legacy request without turn ID" },
                    new ConversationMessage { Role = AgentMessageRoleEnum.Assistant, Content = "Legacy response" },
                    new ConversationMessage { Role = AgentMessageRoleEnum.Assistant, Content = "Unrated response" }
                };
                await rig.Context.Database.Messages.AppendAsync(rig.TenantId, conversation.Id, messages, token).ConfigureAwait(false);
                await rig.Context.Database.Messages.UpdateFeedbackAsync(rig.TenantId, conversation.Id, messages[2].Id, "Negative", "Explain this", token).ConfigureAwait(false);
                await rig.Context.Database.Messages.UpdateFeedbackAsync(rig.TenantId, conversation.Id, messages[4].Id, null, "Comment only", token).ConfigureAwait(false);
                string list = "/v1.0/api/feedback?agentProfileId=" + rig.Hugo.Id;
                EnumerationResult<FeedbackReview> first = await HttpTestHelpers.SendAsync<EnumerationResult<FeedbackReview>>(rig.Anderson, HttpMethod.Get, list + "&maxResults=1", null, 200, token).ConfigureAwait(false);
                Assert.Equal(2L, first.TotalRecords, "unrated messages are not feedback");
                Assert.Equal(1, first.Objects.Count, "page size applies in SQL");
                Assert.Equal(messages[4].Id, first.Objects[0].Id, "latest submission sorts first");
                Assert.Equal("Legacy request without turn ID", first.Objects[0].RequestText, "legacy history associates nearest preceding user");
                EnumerationResult<FeedbackReview> second = await HttpTestHelpers.SendAsync<EnumerationResult<FeedbackReview>>(rig.Anderson, HttpMethod.Get, list + "&maxResults=1&skip=1", null, 200, token).ConfigureAwait(false);
                FeedbackReview detail = await HttpTestHelpers.SendAsync<FeedbackReview>(rig.Anderson, HttpMethod.Get, "/v1.0/api/feedback/" + messages[2].Id, null, 200, token).ConfigureAwait(false);
                Assert.Equal(detail.Id, second.Objects.Single().MessageId, "detail and page refer to exact rated message");
                Assert.Equal("Exact first request", detail.RequestText, "same turn request is selected, not most recent conversation request");
                Assert.Equal("Rated first response", detail.ResponseText, "only the rated assistant response is returned");
                Assert.True(detail.InputTokens == 111 && detail.OutputTokens == 22 && detail.TimeToFirstTokenMs == 18 && detail.DurationMs == 234 && detail.FinishReason == "completed", "persisted metrics are unchanged");
                Assert.Equal(1L, detail.ToolCallCount, "tool calls correlate to the rated turn");
                Assert.Equal(400, (await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Get, "/v1.0/api/feedback", null, token).ConfigureAwait(false)).StatusCode, "specific agent is required");
                await rig.Context.Database.Messages.UpdateFeedbackAsync(rig.TenantId, conversation.Id, messages[4].Id, null, null, token).ConfigureAwait(false);
                EnumerationResult<FeedbackReview> cleared = await HttpTestHelpers.SendAsync<EnumerationResult<FeedbackReview>>(rig.Anderson, HttpMethod.Get, list, null, 200, token).ConfigureAwait(false);
                Assert.Equal(1L, cleared.TotalRecords, "cleared feedback disappears from totals and rows");
                Assert.Equal(404, (await HttpTestHelpers.SendAsync(rig.Anderson, HttpMethod.Get, "/v1.0/api/feedback/" + messages[4].Id, null, token).ConfigureAwait(false)).StatusCode, "cleared feedback detail disappears");
            }
        }

        private static async Task IsolationAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            await using (IntegrationRig rig = await IntegrationRig.StartAsync(provider, null, token).ConfigureAwait(false))
            {
                CredentialCreateResponse allowed = await McpFixture.CredentialAsync(rig, "feedback-reader", rig.Hugo.Id, "ProfileUser", token).ConfigureAwait(false);
                CredentialCreateResponse denied = await McpFixture.CredentialAsync(rig, "feedback-no-grants", rig.Hugo.Id, null, token).ConfigureAwait(false);
                using (HttpClient caller = rig.Host.CredentialClient(allowed.AccessKey, allowed.SecretKey))
                using (HttpClient unscoped = rig.Host.CredentialClient(denied.AccessKey, denied.SecretKey))
                using (HttpClient anonymous = new HttpClient { BaseAddress = new Uri(rig.Host.BaseUrl) })
                {
                    Conversation own = await HttpTestHelpers.SendAsync<Conversation>(caller, HttpMethod.Post, "/v1.0/api/conversations", new { agentProfileId = rig.Hugo.Id }, 201, token).ConfigureAwait(false);
                    Conversation other = await HttpTestHelpers.SendAsync<Conversation>(rig.Anderson, HttpMethod.Post, "/v1.0/api/conversations", new { agentProfileId = rig.Hugo.Id }, 201, token).ConfigureAwait(false);
                    ConversationMessage ownReply = new ConversationMessage { Role = AgentMessageRoleEnum.Assistant, Content = "Owned reply" };
                    ConversationMessage otherReply = new ConversationMessage { Role = AgentMessageRoleEnum.Assistant, Content = "Other visitor reply" };
                    await rig.Context.Database.Messages.AppendAsync(rig.TenantId, own.Id, new List<ConversationMessage> { ownReply }, token).ConfigureAwait(false);
                    await rig.Context.Database.Messages.AppendAsync(rig.TenantId, other.Id, new List<ConversationMessage> { otherReply }, token).ConfigureAwait(false);
                    await rig.Context.Database.Messages.UpdateFeedbackAsync(rig.TenantId, own.Id, ownReply.Id, "Positive", null, token).ConfigureAwait(false);
                    await rig.Context.Database.Messages.UpdateFeedbackAsync(rig.TenantId, other.Id, otherReply.Id, "Negative", null, token).ConfigureAwait(false);
                    string list = "/v1.0/api/feedback?agentProfileId=" + rig.Hugo.Id;
                    EnumerationResult<FeedbackReview> page = await HttpTestHelpers.SendAsync<EnumerationResult<FeedbackReview>>(caller, HttpMethod.Get, list, null, 200, token).ConfigureAwait(false);
                    Assert.True(page.TotalRecords == 1 && page.Objects.Single().Id == ownReply.Id, "credential sees only its conversations even when owner owns other chats");
                    Assert.Equal(404, (await HttpTestHelpers.SendAsync(caller, HttpMethod.Get, "/v1.0/api/feedback/" + otherReply.Id, null, token).ConfigureAwait(false)).StatusCode, "foreign conversation feedback detail is hidden");
                    Assert.Equal(403, (await HttpTestHelpers.SendAsync(unscoped, HttpMethod.Get, list, null, token).ConfigureAwait(false)).StatusCode, "missing Conversation Read is denied");
                    Assert.Equal(401, (await HttpTestHelpers.SendAsync(anonymous, HttpMethod.Get, list, null, token).ConfigureAwait(false)).StatusCode, "anonymous visitors cannot review feedback");
                    EnumerationResult<FeedbackReview> foreignTenant = await rig.Context.Database.Messages.EnumerateFeedbackAsync("foreign-tenant", rig.Hugo.Id, null, null, null, new EnumerationQuery(), token).ConfigureAwait(false);
                    Assert.Equal(0L, foreignTenant.TotalRecords, "tenant filter applies to query totals and rows");
                    AgentProfile privateProfile = await HttpTestHelpers.SendAsync<AgentProfile>(rig.Anderson, HttpMethod.Post, "/v1.0/api/agent-profiles", new { name = "foreign-private", visibility = "Private" }, 201, token).ConfigureAwait(false);
                    privateProfile.OwnerUserId = "another-owner";
                    await rig.Context.Database.AgentProfiles.UpdateAsync(privateProfile, token).ConfigureAwait(false);
                    Assert.Equal(404, (await HttpTestHelpers.SendAsync(caller, HttpMethod.Get, "/v1.0/api/feedback?agentProfileId=" + privateProfile.Id, null, token).ConfigureAwait(false)).StatusCode, "private profile visibility is enforced");
                }
            }
        }
    }
}
