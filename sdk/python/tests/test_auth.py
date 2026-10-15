"""Authentication: health, tokens, refresh, logout, the advisory password change flag, access keys, signed requests,
the administrator API key, credential bypass rules, and typed errors."""

import pytest

from hugo_sdk import HugoError

from conftest import ADMIN_API_KEY, ADMIN_TENANT, new_client, strong_password, unique


def test_health_anonymous():
    with new_client() as anon:
        health = anon.health()
        assert health.status == "healthy" and health.version


def test_info(client):
    info = client.info()
    assert info.product == "Hugo" and info.version and info.route_count > 0


def test_tenant_lookup(harness):
    with new_client() as anon:
        tenants = anon.lookup_tenants(harness.owner_email)
        assert any(t.tenant_id == harness.tenant_id for t in tenants)


def test_login_validate_details_refresh_logout(harness):
    c = new_client()
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
    with new_client() as c:
        with pytest.raises(HugoError) as err:
            c.login(harness.owner_email, "wrong-password-123", harness.tenant_id)
        assert err.value.status == 401 and err.value.code == "InvalidCredentials"


def test_must_change_password_is_advisory(client, harness):
    """D-048: the flag is reported on sign-in, every route still works, and changing the password clears it."""
    email = unique("mustchange") + "@sdk-python.test"
    first = strong_password()
    user = client.users.create({"email": email, "password": first, "firstName": "Must", "lastName": "Change",
                                "isTenantAdmin": True, "mustChangePassword": True})
    try:
        with new_client() as c:
            assert c.login(email, first, harness.tenant_id).must_change_password
            assert c.token_details().must_change_password
            assert c.secrets.list(max_results=1).success
            assert c.users.me().email == email
            changed = c.change_password(first, strong_password())
            assert not changed.must_change_password
            assert not c.users.me().must_change_password
            with pytest.raises(HugoError):
                c.change_password("not-the-password", strong_password())
    finally:
        client.users.delete(user.id)


def test_access_key_direct_and_session_exchange(client, harness):
    created = client.credentials.create({"name": unique("direct"), "userId": harness.owner_id, "authMode": "Hybrid", "roleName": "Viewer"})
    try:
        assert created.secret_key and created.access_key and created.credential.id
        with new_client() as c:
            c.use_access_key(created.access_key, created.secret_key, harness.tenant_id)
            details = c.token_details()
            assert details.principal_type == "Credential" and details.credential_id == created.credential.id
        with new_client() as c:
            issued = c.login_with_access_key(created.access_key, created.secret_key)
            assert issued.token and c.tenant_id == harness.tenant_id
            assert c.token_details().principal_type == "Credential"
            assert c.secrets.list(max_results=1).success
    finally:
        client.credentials.delete(created.credential.id)


def test_session_exchange_credentials_only_work_at_the_token_route(client, harness):
    created = client.credentials.create({"name": unique("exchange"), "userId": harness.owner_id, "authMode": "SessionExchange",
                                         "roleName": "Viewer"})
    try:
        with new_client() as c:
            c.use_access_key(created.access_key, created.secret_key, harness.tenant_id)
            with pytest.raises(HugoError) as err:
                c.token_details()
            assert err.value.status == 401
            issued = c.login_with_access_key(created.access_key, created.secret_key)
            assert issued.token and c.token_details().credential_id == created.credential.id
    finally:
        client.credentials.delete(created.credential.id)


def test_credential_without_roles_has_no_access(client, harness):
    """Credentials no longer inherit the owner's administrator bypass: without a role assignment they reach nothing."""
    created = client.credentials.create({"name": unique("norole"), "userId": harness.owner_id, "authMode": "DirectHeader"})
    try:
        assert created.credential.inherit_owner_bypass is False
        with new_client() as c:
            c.use_access_key(created.access_key, created.secret_key, harness.tenant_id)
            assert c.token_details().principal_type == "Credential"
            with pytest.raises(HugoError) as err:
                c.secrets.list()
            assert err.value.status == 403
    finally:
        client.credentials.delete(created.credential.id)


def test_credential_inherit_owner_bypass(client, harness):
    """Tenant administrators can opt a credential into its owner's bypass (audited); a credential cannot change it."""
    created = client.credentials.create({"name": unique("bypass"), "userId": harness.owner_id, "authMode": "DirectHeader",
                                         "inheritOwnerBypass": True})
    cid = created.credential.id
    try:
        assert created.credential.inherit_owner_bypass is True
        assert client.credentials.get(cid).inherit_owner_bypass is True
        audited = client.audit.list(event_type="CredentialBypassChanged", page_size=50)
        assert any(r.event_type == "CredentialBypassChanged" for r in audited.objects)
        body = {"name": created.credential.name, "userId": harness.owner_id, "authMode": "DirectHeader"}
        with new_client() as c:
            c.use_access_key(created.access_key, created.secret_key, harness.tenant_id)
            assert c.secrets.list(max_results=1).success, "the owner's tenant administrator bypass applies"
            with pytest.raises(HugoError) as err:
                c.credentials.update(cid, dict(body, inheritOwnerBypass=False))
            assert err.value.status == 403 and err.value.code == "BypassRequiresAdministrator"
        off = client.credentials.update(cid, dict(body, inheritOwnerBypass=False))
        assert off.inherit_owner_bypass is False
        with new_client() as c:
            c.use_access_key(created.access_key, created.secret_key, harness.tenant_id)
            with pytest.raises(HugoError) as err:
                c.secrets.list()
            assert err.value.status == 403
    finally:
        client.credentials.delete(cid)


def test_admin_api_key(harness):
    if not ADMIN_API_KEY:
        pytest.skip("set HUGO_SDK_ADMIN_API_KEY (or HUGO_ADMIN_API_KEY) to the server's admin API key to run this test")
    with new_client() as c:
        c.use_admin_api_key(ADMIN_API_KEY, ADMIN_TENANT)
        assert c.token_details().principal_type == "Administrator"
        assert any(t.id == harness.tenant_id for t in c.tenants.list(max_results=1000).objects)
        c.use_admin_api_key("not-the-admin-key-0123456789", ADMIN_TENANT)
        with pytest.raises(HugoError) as err:
            c.tenants.list()
        assert err.value.status == 401


def test_signed_requests(client, harness):
    created = client.credentials.create({"name": unique("signed"), "userId": harness.owner_id, "authMode": "SignedRequest", "roleName": "Viewer"})
    try:
        with new_client() as c:
            c.use_signed_requests(created.access_key, created.secret_key, harness.tenant_id)
            details = c.token_details()
            assert details.auth_scheme == "AccessKeySignature"
            page = c.secrets.list(max_results=5, search="none")
            assert page.success
            c.use_access_key(created.access_key, created.secret_key, harness.tenant_id)
            with pytest.raises(HugoError) as err:
                c.token_details()
            assert err.value.status == 401
        with new_client() as bad:
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
