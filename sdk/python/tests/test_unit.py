"""Offline tests: SSE parsing, request signing, errors, models, and query encoding. No server needed."""

import hashlib
import hmac
from datetime import datetime, timezone

from hugo_sdk import HugoError, __version__, canonicalize, models, parse_text, sign
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
