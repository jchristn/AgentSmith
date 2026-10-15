"""Authentication: health, tokens, refresh, logout, password change, access keys, signed requests, typed errors."""

import pytest

from hugo_sdk import HugoClient, HugoError

from conftest import SERVER, strong_password, unique


def test_health_anonymous():
    with HugoClient(SERVER) as anon:
        health = anon.health()
        assert health.status == "healthy" and health.version


def test_info(client):
    info = client.info()
    assert info.product == "Hugo" and info.version and info.route_count > 0


def test_tenant_lookup(harness):
    with HugoClient(SERVER) as anon:
        tenants = anon.lookup_tenants(harness.owner_email)
        assert any(t.tenant_id == harness.tenant_id for t in tenants)


def test_login_validate_details_refresh_logout(harness):
    c = HugoClient(SERVER)
    issued = c.login(harness.owner_email, harness.owner_password, harness.tenant_id)
    assert issued.token and c.tenant_id == harness.tenant_id and not issued.must_change_password
    assert c.validate_token().valid
    details = c.token_details()
    assert details.principal_type == "User" and details.email == harness.owner_email and details.is_tenant_admin
    old = c.token
    refreshed = c.refresh()
    assert refreshed.token and c.token == refreshed.token and refreshed.token != old
    assert c.validate_token().valid
    c.logout()
    assert c.token is None
    c.use_token(refreshed.token, harness.tenant_id)
    with pytest.raises(HugoError) as err:
        c.token_details()
    assert err.value.status == 401
    c.close()


def test_invalid_login_raises_typed_error(harness):
    with HugoClient(SERVER) as c:
        with pytest.raises(HugoError) as err:
            c.login(harness.owner_email, "wrong-password-123", harness.tenant_id)
        assert err.value.status == 401 and err.value.code == "InvalidCredentials"


def test_password_change_required_flow(client, harness):
    email = unique("mustchange") + "@sdk-python.test"
    first = strong_password()
    user = client.users.create({"email": email, "password": first, "firstName": "Must", "lastName": "Change", "mustChangePassword": True})
    try:
        with HugoClient(SERVER) as c:
            assert c.login(email, first, harness.tenant_id).must_change_password
            with pytest.raises(HugoError) as err:
                c.secrets.list()
            assert err.value.status == 403 and err.value.code == "PasswordChangeRequired" and err.value.request_id
            second = strong_password()
            changed = c.change_password(first, second)
            assert not changed.must_change_password
            assert c.users.me().email == email
            with pytest.raises(HugoError):
                c.change_password("not-the-password", strong_password())
    finally:
        client.users.delete(user.id)


def test_access_key_direct_and_session_exchange(client, harness):
    created = client.credentials.create({"name": unique("direct"), "userId": harness.owner_id, "authMode": "Hybrid"})
    try:
        assert created.secret_key and created.access_key and created.credential.id
        with HugoClient(SERVER) as c:
            c.use_access_key(created.access_key, created.secret_key)
            details = c.token_details()
            assert details.principal_type == "Credential" and details.credential_id == created.credential.id
        with HugoClient(SERVER) as c:
            issued = c.login_with_access_key(created.access_key, created.secret_key)
            assert issued.token and c.tenant_id == harness.tenant_id
            assert c.token_details().principal_type == "Credential"
    finally:
        client.credentials.delete(created.credential.id)


def test_signed_requests(client, harness):
    created = client.credentials.create({"name": unique("signed"), "userId": harness.owner_id, "authMode": "SignedRequest"})
    try:
        with HugoClient(SERVER) as c:
            c.use_signed_requests(created.access_key, created.secret_key, harness.tenant_id)
            details = c.token_details()
            assert details.auth_scheme == "AccessKeySignature"
            page = c.secrets.list(max_results=5, search="none")
            assert page.success
            c.use_access_key(created.access_key, created.secret_key)
            with pytest.raises(HugoError) as err:
                c.token_details()
            assert err.value.status == 401
        with HugoClient(SERVER) as bad:
            bad.use_signed_requests(created.access_key, "wrong-secret", harness.tenant_id)
            with pytest.raises(HugoError) as err:
                bad.token_details()
            assert err.value.status == 401
    finally:
        client.credentials.delete(created.credential.id)


def test_typed_not_found_error(client):
    with pytest.raises(HugoError) as err:
        client.secrets.get("sec_does_not_exist")
    e = err.value
    assert e.status == 404 and e.code == "NotFound" and e.message and e.request_id and e.error == "NotFound"
    assert not e.is_route_not_found
