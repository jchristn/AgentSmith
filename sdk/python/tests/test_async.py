"""The async client has the same surface as the sync client."""

import pytest

from hugo_sdk import AsyncHugoClient, HugoError

from conftest import SERVER, unique


async def test_async_login_details_refresh(harness):
    async with AsyncHugoClient(SERVER) as c:
        issued = await c.login(harness.owner_email, harness.owner_password, harness.tenant_id)
        assert issued.token and c.tenant_id == harness.tenant_id
        details = await c.token_details()
        assert details.email == harness.owner_email
        refreshed = await c.refresh()
        assert c.token == refreshed.token
        assert (await c.health()).status == "healthy"


async def test_async_secrets_and_users(harness):
    async with AsyncHugoClient(SERVER) as c:
        await c.login(harness.owner_email, harness.owner_password, harness.tenant_id)
        secret = await c.secrets.create({"name": unique("async"), "kind": "Generic", "values": {"value": "z"}})
        try:
            page = await c.secrets.list(search=secret.name)
            assert [s.id for s in page.objects] == [secret.id]
        finally:
            await c.secrets.delete(secret.id)
        users = await c.users.list()
        assert any(u.email == harness.owner_email for u in users.objects)
        history = await c.request_history.list(page_size=5)
        assert history.objects


async def test_async_typed_error(harness):
    async with AsyncHugoClient(SERVER) as c:
        await c.login(harness.owner_email, harness.owner_password, harness.tenant_id)
        with pytest.raises(HugoError) as err:
            await c.roles.get("rol_missing")
        assert err.value.status == 404 and err.value.request_id


async def test_async_auto_refresh_when_near_expiry(harness):
    async with AsyncHugoClient(SERVER, refresh_skew_seconds=10 ** 7) as c:
        await c.login(harness.owner_email, harness.owner_password, harness.tenant_id)
        first = c.token
        await c.users.me()
        assert c.token != first, "the token should have been refreshed before the call"
