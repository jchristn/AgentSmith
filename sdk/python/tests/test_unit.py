"""Offline tests: SSE parsing, request signing, errors, models, and query encoding. No server needed."""

import hashlib
import hmac
from datetime import datetime, timezone

import httpx
import pytest

from hugo_sdk import CHAT_EVENT_TYPES, HugoClient, HugoError, __version__, canonicalize, models, parse_text, sign
from hugo_sdk.sse import ChatEvent, SseParser
from hugo_sdk.transport import AuthState, Call, SyncTransport, clean_query


def test_version_mirrors_directory_build_props():
    assert __version__ == "0.1.0-alpha"


def test_sse_parser_events_comments_and_multiline():
    events = parse_text(": comment\nevent: stream.opened\ndata: {\"tenantId\":\"t\"}\n\n"
                        "id: 7\nevent: secret.created\ndata: line1\ndata: line2\n\n"
                        "data: no-event\n\nretry: 10\n\ndata: tail")
    assert [e.event for e in events] == ["stream.opened", "secret.created", "message", "message"]
    assert events[0].json() == {"tenantId": "t"}
    assert events[1].id == "7" and events[1].data == "line1\nline2"
    assert events[3].data == "tail"


def test_sse_parser_crlf_and_no_space():
    parser = SseParser()
    assert parser.feed("event:turn.completed\r") is None
    assert parser.feed("data:{}\r") is None
    event = parser.feed("\r")
    assert event is not None and event.event == "turn.completed" and event.json() == {}
    chat = ChatEvent.from_sse(event)
    assert chat.is_terminal


def test_canonical_string_and_signature_match_server_algorithm():
    body = b'{"a":1}'
    canonical = canonicalize("post", "/v1.0/api/x", "a=1&b=2", "ten_1", "2026-01-01T00:00:00Z", "abcd", body)
    expected = "POST\n/v1.0/api/x\na=1&b=2\nten_1\n2026-01-01T00:00:00Z\nabcd\n" + hashlib.sha256(body).hexdigest()
    assert canonical == expected
    assert sign("secret", canonical) == hmac.new(b"secret", expected.encode(), hashlib.sha256).hexdigest()
    empty = canonicalize("GET", "/p", "", "", "d", "n", None)
    assert empty.endswith(hashlib.sha256(b"").hexdigest())


def test_signed_headers_are_present():
    t = SyncTransport("http://127.0.0.1:1", auth=AuthState(mode="signed", access_key="ak", secret_key="sk", tenant_id="ten"))
    _, _, headers = t._prepare(Call("GET", "/v1.0/api/tenants/ten/secrets", {"maxResults": 5}))
    assert headers["x-access-key"] == "ak" and headers["x-tenant-id"] == "ten"
    assert len(headers["x-nonce"]) == 32 and headers["x-date"].endswith("Z")
    canonical = canonicalize("GET", "/v1.0/api/tenants/ten/secrets", "maxResults=5", "ten", headers["x-date"], headers["x-nonce"], None)
    assert headers["x-signature"] == sign("sk", canonical)
    t.close()


def test_error_parsing_prefers_body_request_id():
    e = HugoError(404, '{"error":"NotFound","code":"RouteNotFound","message":"No route.","requestId":"r1"}', "hdr")
    assert e.status == 404 and e.code == "RouteNotFound" and e.request_id == "r1" and e.is_route_not_found
    e2 = HugoError(400, '{"error":"BadRequest","code":"ValidationFailed","message":"Bad","fields":{"name":"Required"}}', "hdr")
    assert e2.request_id == "hdr" and e2.fields == {"name": "Required"}
    e3 = HugoError(502, "gateway down")
    assert e3.code is None and "gateway" in e3.message


def test_models_ignore_unknown_fields_and_parse_dates():
    secret = models.Secret.from_dict({"id": "sec_1", "name": "n", "createdUtc": "2026-10-15T02:36:48.1234567Z",
                                      "referrers": [{"referrerType": "Resource", "x": 1}], "brandNew": True})
    assert secret.id == "sec_1" and secret.created_utc.year == 2026 and secret.created_utc.tzinfo is not None
    assert secret.referrers[0].referrer_type == "Resource"
    assert secret["brandNew"] is True
    profile = models.AgentProfile.from_dict({"exposeA2A": True, "exposeOpenAI": True})
    assert profile.expose_a2a and profile.expose_openai
    assert profile.to_dict()["exposeA2A"] is True


def test_enumeration_parse():
    page = models.EnumerationResult.parse({"success": True, "maxResults": 5, "totalRecords": 1, "objects": [{"id": "u"}]}, models.User)
    assert page.total_records == 1 and page.objects[0].id == "u"


def test_query_encoding():
    q = clean_query({"a": None, "b": True, "c": "x y", "d": datetime(2026, 1, 2, tzinfo=timezone.utc), "e": ""})
    assert q == "b=true&c=x%20y&d=2026-01-02T00%3A00%3A00.000Z"


def test_tenant_override_adds_signed_query_parameter():
    t = SyncTransport("http://127.0.0.1:1", auth=AuthState(mode="signed", access_key="ak", secret_key="sk", tenant_id="ten",
                                                           tenant_override="ten_other"))
    url, _, headers = t._prepare(Call("GET", "/v1.0/api/resources", {"maxResults": 5}))
    assert url.endswith("/v1.0/api/resources?maxResults=5&tenantId=ten_other")
    canonical = canonicalize("GET", "/v1.0/api/resources", "maxResults=5&tenantId=ten_other", "ten", headers["x-date"], headers["x-nonce"], None)
    assert headers["x-signature"] == sign("sk", canonical)
    url2, _, _ = t._prepare(Call("GET", "/v1.0/api/tenants/ten/users"))
    assert "tenantId" not in url2
    t.close()


def test_prompt_template_view_global_layer():
    view = models.PromptTemplateView.from_dict({"key": "k", "activeLayer": "Global", "global": {"layer": "Global", "exists": True, "version": 3}})
    assert view.global_.exists and view.global_.version == 3 and view.tenant is None


def _scripted(statuses):
    """An httpx client whose responses follow the given (status, retry_after) list; records each request."""
    seen = []

    def handler(request):
        seen.append(request)
        status, retry_after = statuses[min(len(seen) - 1, len(statuses) - 1)]
        headers = {"Retry-After": retry_after} if retry_after is not None else {}
        body = '{"error":"TooManyRequests","code":"RateLimited","message":"slow down"}' if status == 429 else '{"status":"healthy"}'
        return httpx.Response(status, text=body, headers=headers)

    return httpx.Client(transport=httpx.MockTransport(handler)), seen


def test_rate_limit_retry_is_off_by_default():
    http, seen = _scripted([(429, "0")])
    with HugoClient("http://127.0.0.1:1", http_client=http) as c:
        with pytest.raises(HugoError) as err:
            c.health()
    assert err.value.status == 429 and err.value.code == "RateLimited" and err.value.retry_after == "0"
    assert len(seen) == 1


def test_rate_limit_retry_honors_retry_after(monkeypatch):
    waits = []
    monkeypatch.setattr("hugo_sdk.transport.time.sleep", lambda s: waits.append(s))
    http, seen = _scripted([(429, "3"), (429, None), (200, None)])
    with HugoClient("http://127.0.0.1:1", http_client=http, rate_limit_retries=3, rate_limit_max_wait=2.5) as c:
        assert c.health().status == "healthy"
    assert len(seen) == 3
    assert waits == [2.5, 2.0], "Retry-After capped by rate_limit_max_wait, then backoff 2**attempt without the header"


def test_rate_limit_retry_gives_up(monkeypatch):
    monkeypatch.setattr("hugo_sdk.transport.time.sleep", lambda s: None)
    http, seen = _scripted([(429, "1")])
    with HugoClient("http://127.0.0.1:1", http_client=http, rate_limit_retries=2) as c:
        with pytest.raises(HugoError) as err:
            c.health()
    assert err.value.status == 429 and len(seen) == 3


def test_new_models_parse():
    result = models.AgentTurnResult.from_dict({"turnId": "t", "finalText": "hi", "finishReason": "Completed",
                                               "toolCalls": [{"callId": "c", "toolName": "x", "decision": "Allowed"}],
                                               "messages": [{"role": "Assistant", "content": "hi", "tokensOut": 3}]})
    assert result.final_text == "hi" and result.tool_calls[0].decision == "Allowed" and result.messages[0].tokens_out == 3
    report = models.AnalyticsReport.from_dict({"kind": "overview", "totals": {"turns": 2}, "buckets": [{"turns": 2}],
                                               "models": [{"modelEndpointId": "mep_1", "turns": 2}]})
    assert report.totals.turns == 2 and report.buckets[0].turns == 2 and report.models[0].model_endpoint_id == "mep_1"
    check = models.ModelHealthCheck.from_dict({"status": "Healthy", "source": "Sweep", "checkedUtc": "2026-10-15T00:00:00Z"})
    assert check.source == "Sweep" and check.checked_utc.year == 2026
    assert models.Credential.from_dict({"inheritOwnerBypass": True}).inherit_owner_bypass is True
    assert models.AuthSession.from_dict({"principalName": "Sdk Owner"}).principal_name == "Sdk Owner"
    assert models.AgentProfile.from_dict({"bindingCount": 3}).binding_count == 3
    assert models.Conversation.from_dict({"a2AContextId": "ctx"}).a2a_context_id == "ctx"
    ask = models.AgentDelegationResult.from_dict({"status": "ApprovalPending", "approvalId": "apr_1", "toolTrace": ["a"]})
    assert ask.status == "ApprovalPending" and ask.tool_trace == ["a"]


def test_chat_event_types_include_stream_closed():
    assert "stream.closed" in CHAT_EVENT_TYPES and "turn.completed" in CHAT_EVENT_TYPES
