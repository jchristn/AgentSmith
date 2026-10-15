"""Secrets vault: CRUD, values, referrers, parse, and reveal with re-authentication."""

import pytest

from hugo_sdk import HugoError

from conftest import unique

KUBECONFIG = """apiVersion: v1
kind: Config
clusters:
- name: dev
  cluster:
    server: https://127.0.0.1:6443
contexts:
- name: dev-admin
  context:
    cluster: dev
    user: admin
    namespace: shop
current-context: dev-admin
users:
- name: admin
  user:
    token: abc123
"""


def test_secrets_crud_values_referrers(client):
    secret = client.secrets.create({"name": unique("pw"), "kind": "Password", "description": "sdk", "values": {"password": "hunter2-sdk"}})
    try:
        assert secret.id and secret.kind == "Password" and "password" in secret.fields
        assert "hunter2" not in str(secret.raw)
        fetched = client.secrets.get(secret.id)
        assert fetched.name == secret.name
        updated = client.secrets.update(secret.id, {"name": secret.name, "kind": "Password", "description": "changed"})
        assert updated.description == "changed"
        with_values = client.secrets.set_values(secret.id, {"username": "root"})
        assert sorted(with_values.fields) == ["password", "username"]
        removed = client.secrets.set_values(secret.id, {}, remove=["username"])
        assert removed.fields == ["password"]
        assert client.secrets.referrers(secret.id) == []
        assert any(s.id == secret.id for s in client.secrets.list(search=secret.name).objects)
    finally:
        client.secrets.delete(secret.id)
    with pytest.raises(HugoError) as err:
        client.secrets.get(secret.id)
    assert err.value.status == 404


def test_secret_parse_kubeconfig(client):
    parsed = client.secrets.parse(KUBECONFIG, file_name="kubeconfig")
    assert parsed.kind == "Kubeconfig"
    assert parsed.current_context == "dev-admin"
    assert any(c.namespace == "shop" for c in parsed.contexts)


def test_secret_reveal_requires_reauthentication(harness):
    c = harness.owner_client()
    try:
        secret = c.secrets.create({"name": unique("reveal"), "kind": "ApiToken", "values": {"token": "tok-sdk-123"}})
        try:
            with pytest.raises(HugoError) as err:
                c.secrets.reveal(secret.id, "sdk harness check")
            assert err.value.status == 403 and err.value.code == "ReauthenticationRequired"
            reauth = c.reauthenticate(harness.owner_password)
            assert reauth.reveal_allowed_until_utc > reauth.reauthenticated_utc
            assert c.token_details().reauthenticated_utc is not None
            revealed = c.secrets.reveal(secret.id, "sdk harness check")
            assert revealed.values == {"token": "tok-sdk-123"} and revealed.secret_id == secret.id
            assert c.secrets.get(secret.id).last_revealed_utc is not None
        finally:
            c.secrets.delete(secret.id)
    finally:
        c.close()


def test_reauthenticate_wrong_password(harness):
    c = harness.owner_client()
    try:
        with pytest.raises(HugoError) as err:
            c.reauthenticate("definitely-wrong")
        assert err.value.status == 401
    finally:
        c.close()
