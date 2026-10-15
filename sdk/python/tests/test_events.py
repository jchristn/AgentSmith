"""Server-sent events: the stream opens, and a secret change arrives as an event (sync and async)."""

import asyncio
import queue
import threading

import pytest

from conftest import new_async_client, new_client, unique


def test_events_stream_sync_receives_secret_created(client, harness):
    listener = harness.owner_client()
    received: "queue.Queue" = queue.Queue()

    def pump():
        try:
            for event in listener.events.stream():
                received.put(event)
        except Exception as e:  # noqa: BLE001 - the listener is closed from the test thread
            received.put(e)

    thread = threading.Thread(target=pump, daemon=True)
    thread.start()
    try:
        opened = received.get(timeout=15)
        assert not isinstance(opened, Exception), opened
        assert opened.event == "stream.opened" and opened.json()["tenantId"] == harness.tenant_id
        name = unique("evt")
        secret = client.secrets.create({"name": name, "kind": "Generic", "values": {"value": "x"}})
        try:
            found = None
            while found is None:
                event = received.get(timeout=20)
                assert not isinstance(event, Exception), event
                if event.event == "secret.created" and event.json().get("name") == name:
                    found = event
            assert found.json()["id"] == secret.id and found.id
            assert "x" not in (found.json().get("values") or {})
        finally:
            client.secrets.delete(secret.id)
    finally:
        listener.close()


async def test_events_stream_async_receives_secret_deleted(harness):
    async with new_async_client() as listener, new_async_client() as actor:
        await listener.login(harness.owner_email, harness.owner_password, harness.tenant_id)
        actor.use_token(listener.token, harness.tenant_id)
        name = unique("aevt")
        stream = listener.events.stream()
        opened = await asyncio.wait_for(stream.__anext__(), 15)
        assert opened.event == "stream.opened"
        secret = await actor.secrets.create({"name": name, "kind": "Generic", "values": {"value": "y"}})
        await actor.secrets.delete(secret.id)

        async def wait_for_delete():
            async for event in stream:
                if event.event == "secret.deleted" and event.json().get("id") == secret.id:
                    return event
            return None

        event = await asyncio.wait_for(wait_for_delete(), 20)
        assert event is not None and event.json()["name"] == name
        await stream.aclose()


def test_events_requires_auth():
    with new_client() as anon:
        with pytest.raises(Exception) as err:
            next(iter(anon.events.stream()))
        assert getattr(err.value, "status", None) == 401
