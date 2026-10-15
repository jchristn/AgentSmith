namespace Test.Automated.Suites
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Hugo.Sdk;
    using Hugo.Sdk.Enumeration;
    using Hugo.Sdk.Http;
    using Hugo.Sdk.Models;
    using Hugo.Sdk.Responses;

    /// <summary>
    /// Offline tests of the SSE parser, request signer, enumeration query, and error type.
    /// </summary>
    public class UnitSuite : TestSuite
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Name
        {
            get
            {
                return "Unit";
            }
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override async Task RunAsync(TestRunner runner, HarnessContext ctx)
        {
            await runner.RunAsync("SSE parser handles events, comments, multi-line data, and EOF flush", async () =>
            {
                string text = "event: stream.opened\ndata: {\"tenantId\":\"t\"}\n\n: comment\ndata: line1\ndata:line2\n\nid: 7\nevent: assistant.delta\ndata: x";
                List<SseEvent> events = await SseReader.ParseAsync(text).ConfigureAwait(false);
                Check.Equal(3, events.Count, "event count");
                Check.Equal("stream.opened", events[0].Event, "first event name");
                Check.Equal("t", events[0].DataAs<Dictionary<string, string>>()!["tenantId"], "first event data");
                Check.Equal<string?>(null, events[1].Event, "second event name");
                Check.Equal("line1\nline2", events[1].Data, "multi-line data");
                Check.Equal("7", events[2].Id, "third event id");
                Check.Equal("x", events[2].Data, "flushed data");
                ChatEvent chat = ChatEvent.FromSse(events[2]);
                Check.Equal(ChatEventTypes.AssistantDelta, chat.Type, "chat event type");
                Check.True(!chat.IsTerminal, "assistant.delta is not terminal");
            }).ConfigureAwait(false);

            await runner.RunAsync("Request signer canonical string and signature", () =>
            {
                string canonical = RequestSigner.Canonicalize("get", "/v1.0/api/x", "a=1", "ten_1", "2026-01-01T00:00:00Z", "abc", null);
                Check.Equal("GET\n/v1.0/api/x\na=1\nten_1\n2026-01-01T00:00:00Z\nabc\ne3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", canonical, "canonical string");
                Check.Equal("2d4db3eca715cf6e2d91e8136f9b7ce78f2d438cab83c369806e1d4ebd9d1766", RequestSigner.Sign("secret", canonical), "signature");
                Check.Equal(32, RequestSigner.NewNonce().Length, "nonce length");
                return Task.CompletedTask;
            }).ConfigureAwait(false);

            await runner.RunAsync("Enumeration query clamps values", () =>
            {
                EnumerationQuery query = new EnumerationQuery();
                query.MaxResults = 5000;
                query.Skip = -3;
                Check.Equal(1000, query.MaxResults, "max results clamp");
                Check.Equal(0, query.Skip, "skip clamp");
                Dictionary<string, string?> parameters = QueryBuilder.From(query);
                Check.Equal("1000", parameters["maxResults"], "maxResults parameter");
                return Task.CompletedTask;
            }).ConfigureAwait(false);

            await runner.RunAsync("HugoException exposes code, request id, and route-not-found", () =>
            {
                ErrorResponse body = new ErrorResponse();
                body.Error = "NotFound";
                body.Code = "RouteNotFound";
                body.Message = "No route matches the request.";
                HugoException e = new HugoException(404, body, "{}", "req-header");
                Check.True(e.IsRouteNotFound, "IsRouteNotFound");
                Check.Equal("req-header", e.RequestId, "request id falls back to the header");
                Check.True(e.Message.Contains("RouteNotFound"), "message names the code");
                return Task.CompletedTask;
            }).ConfigureAwait(false);
        }

        #endregion
    }
}
