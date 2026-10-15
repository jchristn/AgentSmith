"""Conversations, chat turns (JSON and streamed), ask, analytics, and model validation against the stub model server
(tests/stub_llm.py), so no external model is needed."""

import json
from datetime import datetime, timedelta, timezone

import pytest

from hugo_sdk import HugoError

from conftest import unique


def test_model_endpoint_validation_against_stub(client, model_setup, stub_llm):
    draft = client.model_endpoints.validate_draft({"name": "draft:stub", "provider": "OpenAICompatible", "baseUrl": stub_llm.base_url,
                                                   "model": "stub-model", "apiKeySecretId": model_setup.secret.id,
                                                   "maxRetries": 0, "timeoutSeconds": 30})
    assert draft.success, draft.error
    assert draft.kind == "Inference" and draft.latency_ms is not None
    result = client.model_endpoints.validate(model_setup.endpoint.id)
    assert result.success, result.error
    assert client.model_endpoints.get(model_setup.endpoint.id).health_status == "Healthy"
    history = client.model_endpoints.health_history(model_setup.endpoint.id)
    assert history and history[0].status == "Healthy" and history[0].source == "Validate"
    assert any(e.id == model_setup.endpoint.id for e in client.model_endpoints.list(provider="OpenAICompatible", health="Healthy").objects)
    assert "stub-model" in json.dumps(client.model_endpoints.models(model_setup.endpoint.id))
    sent = [r for r in stub_llm.chat_requests if r.get("model") == "stub-model"]
    assert sent, "the stub saw the validation prompt"


def test_conversation_crud_and_json_turn(client, model_setup, stub_llm):
    profile = model_setup.profile
    conversation = client.conversations.create({"agentProfileId": profile.id, "title": "SDK JSON turn"})
    try:
        assert conversation.id and conversation.agent_profile_id == profile.id and conversation.source == "Dashboard"
        assert client.conversations.get(conversation.id).title == "SDK JSON turn"
        renamed = client.conversations.update(conversation.id, {"title": "Renamed"})
        assert renamed.title == "Renamed"

        result = client.conversations.send_message(conversation.id, "Hello, Agent Smith.")
        assert result.finish_reason == "Completed", result.error_message
        assert result.final_text == stub_llm.reply
        assert result.conversation_id == conversation.id and result.turn_id
        assert result.tokens_in > 0 and result.tokens_out > 0 and result.iterations >= 1

        page = client.conversations.messages(conversation.id)
        assert page.success and page.total_records >= 2
        roles = [m.role for m in page.objects]
        assert roles[0] == "User" and "Assistant" in roles
        assert page.objects[0].content == "Hello, Agent Smith." and page.objects[0].conversation_id == conversation.id
        assistant = next(m for m in page.objects if m.role == "Assistant")
        assert assistant.content == stub_llm.reply and assistant.turn_id == result.turn_id
        assert [m.sequence for m in page.objects] == sorted(m.sequence for m in page.objects)
        first = client.conversations.messages(conversation.id, max_results=1)
        assert len(first.objects) == 1 and not first.end_of_results

        listed = client.conversations.list(agent_profile_id=profile.id, max_results=100)
        assert any(c.id == conversation.id and c.message_count >= 2 for c in listed.objects)
        assert any(c.id == conversation.id for c in client.conversations.list(source="Dashboard", max_results=100).objects)
        assert not any(c.id == conversation.id for c in client.conversations.list(source="A2A", max_results=100).objects)

        markdown = client.conversations.export(conversation.id)
        assert stub_llm.reply in markdown
        exported = json.loads(client.conversations.export(conversation.id, "json"))
        assert stub_llm.reply in json.dumps(exported)

        idle = client.conversations.cancel(conversation.id)
        assert idle.conversation_id == conversation.id and idle.cancelled is False
    finally:
        client.conversations.delete(conversation.id)
    with pytest.raises(HugoError) as err:
        client.conversations.get(conversation.id)
    assert err.value.status == 404


def test_chat_streaming(client, model_setup, stub_llm):
    conversation = client.conversations.create({"agentProfileId": model_setup.profile.id})
    try:
        events = list(client.conversations.stream_message(conversation.id, "Stream to me."))
        names = [e.event for e in events]
        assert names[0] == "turn.started"
        assert "assistant.delta" in names and "turn.completed" in names
        assert names[-1] == "stream.closed"
        text = "".join(e.json()["text"] for e in events if e.event == "assistant.delta")
        completed = next(e for e in events if e.event == "turn.completed")
        assert completed.is_terminal and completed.json()["finalText"] == stub_llm.reply
        assert text == stub_llm.reply, "deltas concatenate to the reply"
        closed = events[-1].json()
        assert closed["conversationId"] == conversation.id and closed["finishReason"]
    finally:
        client.conversations.delete(conversation.id)


async def test_chat_streaming_async(harness, model_setup, stub_llm, client):
    from conftest import new_async_client

    async with new_async_client() as c:
        c.use_token(client.token, harness.tenant_id)
        conversation = await c.conversations.create({"agentProfileId": model_setup.profile.id})
        try:
            names = [e.event async for e in c.conversations.stream_message(conversation.id, "Async stream.")]
            assert "turn.completed" in names and names[-1] == "stream.closed"
            result = await c.conversations.send_message(conversation.id, "And once more as JSON.")
            assert result.final_text == stub_llm.reply
        finally:
            await c.conversations.delete(conversation.id)


def test_ask(client, model_setup, stub_llm):
    answer = client.agent_profiles.ask(model_setup.profile.id, "Summarize the state of the system.")
    assert answer.status == "Completed" and answer.final_text == stub_llm.reply and answer.conversation_id
    followup = client.agent_profiles.ask(model_setup.profile.id, "And now?", conversation_id=answer.conversation_id)
    assert followup.status == "Completed" and followup.conversation_id == answer.conversation_id
    messages = client.conversations.messages(answer.conversation_id)
    assert sum(1 for m in messages.objects if m.role == "User") == 2
    with pytest.raises(HugoError) as err:
        client.agent_profiles.ask("prf_" + unique("missing"), "hello")
    assert err.value.status == 404


def test_analytics(client, model_setup):
    # The chat tests above ran turns in this tenant; a fresh conversation makes this test independent of order.
    conversation = client.conversations.create({"agentProfileId": model_setup.profile.id})
    client.conversations.send_message(conversation.id, "Count me.")
    now = datetime.now(timezone.utc)
    overview = client.analytics.overview(from_utc=now - timedelta(hours=1), to_utc=now + timedelta(minutes=1))
    assert overview.kind == "overview" and overview.interval == "minute"
    assert overview.totals is not None and overview.totals.turns >= 1 and overview.totals.input_tokens > 0
    assert overview.conversations >= 1 and overview.buckets
    assert sum(b.turns for b in overview.buckets) == overview.totals.turns
    tokens = client.analytics.tokens(interval="hour")
    assert tokens.kind == "tokens" and tokens.interval == "hour" and tokens.totals.output_tokens > 0
    models = client.analytics.models(from_utc=now - timedelta(days=2), interval="day")
    assert models.kind == "models" and any(row.model_endpoint_id == model_setup.endpoint.id and row.turns >= 1 for row in models.models)
    tools = client.analytics.tools()
    assert tools.kind == "tools" and isinstance(tools.tools, list)
    with pytest.raises(HugoError) as err:
        client.analytics.overview(interval="fortnight")
    assert err.value.status == 400
    with pytest.raises(HugoError) as err:
        client.analytics.overview(from_utc=now, to_utc=now - timedelta(hours=1))
    assert err.value.status == 400
    client.conversations.delete(conversation.id)
