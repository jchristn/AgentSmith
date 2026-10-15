"""Identity and RBAC: tenants, users, credentials, roles, permissions, mappings, assignments, sessions."""

import pytest

from hugo_sdk import HugoClient, HugoError

from conftest import ADMIN_TENANT, SERVER, strong_password, unique


def test_tenants_admin_crud(admin):
    created = admin.tenants.create({"name": unique("sdk-python-extra"), "description": "temporary"})
    try:
        assert created.id and not created.lockdown
        assert admin.tenants.get(created.id).name == created.name
        updated = admin.tenants.update(created.id, {"name": created.name, "description": "changed"})
        assert updated.description == "changed"
        page = admin.tenants.list(max_results=1000)
        assert any(t.id == created.id for t in page.objects)
    finally:
        admin.tenants.delete(created.id)
    with pytest.raises(HugoError) as err:
        admin.tenants.get(created.id)
    assert err.value.status in (403, 404)


def test_tenant_visible_to_owner(client, harness):
    tenant = client.tenants.get(harness.tenant_id)
    assert tenant.id == harness.tenant_id


def test_users_crud_me_permissions_reset(client, harness):
    email = unique("user") + "@sdk-python.test"
    password = strong_password()
    user = client.users.create({"email": email, "password": password, "firstName": "Ann", "lastName": "Example"})
    try:
        assert user.email == email and user.tenant_id == harness.tenant_id
        assert client.users.get(user.id).first_name == "Ann"
        updated = client.users.update(user.id, {"firstName": "Anne", "lastName": "Example", "email": email})
        assert updated.first_name == "Anne"
        page = client.users.list(search="user-")
        assert any(u.id == user.id for u in page.objects)
        perms = client.users.permissions(user.id)
        assert perms.principal_id == user.id and isinstance(perms.has_permission, list)
        reset = client.users.reset_password(user.id, strong_password())
        assert reset.must_change_password
        reset2 = client.users.reset_password(user.id, password + "x", must_change_password=False)
        assert not reset2.must_change_password
        with HugoClient(SERVER) as c:
            c.login(email, password + "x", harness.tenant_id)
            assert c.users.me().id == user.id
            assert c.users.update_me({"firstName": "Annie", "lastName": "Example", "preferredLocale": "en"}).first_name == "Annie"
            assert c.users.permissions(user.id).principal_id == user.id
    finally:
        client.users.delete(user.id)
    with pytest.raises(HugoError) as err:
        client.users.get(user.id)
    assert err.value.status == 404


def test_me_as_owner(client, harness):
    me = client.users.me()
    assert me.email == harness.owner_email and me.is_tenant_admin


def test_credentials_crud_rotate_permissions(client, harness):
    created = client.credentials.create({"name": unique("cred"), "userId": harness.owner_id, "authMode": "DirectHeader"})
    cid = created.credential.id
    try:
        assert created.credential.secret_key_last4 == created.secret_key[-4:]
        fetched = client.credentials.get(cid)
        assert fetched.access_key == created.access_key and fetched.auth_mode == "DirectHeader"
        updated = client.credentials.update(cid, {"name": "renamed-" + cid[-6:], "userId": harness.owner_id, "authMode": "DirectHeader"})
        assert updated.name.startswith("renamed-")
        assert any(c.id == cid for c in client.credentials.list().objects)
        rotated = client.credentials.rotate(cid)
        assert rotated.secret_key and rotated.secret_key != created.secret_key
        with HugoClient(SERVER) as c:
            c.use_access_key(created.access_key, created.secret_key)
            with pytest.raises(HugoError):
                c.token_details()
            c.use_access_key(rotated.access_key or created.access_key, rotated.secret_key)
            assert c.token_details().credential_id == cid
        perms = client.credentials.permissions(cid)
        assert perms.principal_type == "Credential"
    finally:
        client.credentials.delete(cid)


def test_roles_crud_and_clone(client):
    role = client.roles.create({"name": unique("role"), "description": "sdk"})
    try:
        assert client.roles.get(role.id).name == role.name
        assert client.roles.update(role.id, {"name": role.name, "description": "updated"}).description == "updated"
        roles = client.roles.list(max_results=1000)
        built_in = [r for r in roles.objects if r.is_built_in]
        assert built_in
        clone = client.roles.clone(built_in[0].id, {"name": unique("clone")})
        try:
            assert not clone.is_built_in
        finally:
            client.roles.delete(clone.id)
    finally:
        client.roles.delete(role.id)


def test_permissions_crud(client):
    perm = client.permissions.create({"name": unique("perm"), "permissionType": "Permit", "resourceTypes": ["Secret"], "operationTypes": ["Read"]})
    try:
        assert perm.resource_types == ["Secret"] and perm.operation_types == ["Read"]
        assert client.permissions.get(perm.id).name == perm.name
        updated = client.permissions.update(perm.id, {"name": perm.name, "permissionType": "Permit", "resourceTypes": ["Secret"], "operationTypes": ["Read", "Update"]})
        assert sorted(updated.operation_types) == ["Read", "Update"]
        assert any(p.id == perm.id for p in client.permissions.list(max_results=1000).objects)
    finally:
        client.permissions.delete(perm.id)


def test_role_permission_maps(client):
    role = client.roles.create({"name": unique("maprole")})
    perm = client.permissions.create({"name": unique("mapperm"), "permissionType": "Permit", "resourceTypes": ["Audit"], "operationTypes": ["Read"]})
    try:
        mapping = client.role_permissions.create({"roleId": role.id, "permissionId": perm.id})
        assert mapping.role_id == role.id and mapping.permission_id == perm.id
        assert any(x.id == mapping.id for x in client.role_permissions.list(role_id=role.id).objects)
        client.role_permissions.delete(mapping.id)
        assert not any(x.id == mapping.id for x in client.role_permissions.list(role_id=role.id).objects)
    finally:
        client.permissions.delete(perm.id)
        client.roles.delete(role.id)


def test_user_role_assignments(client):
    user = client.users.create({"email": unique("assign") + "@sdk-python.test", "password": strong_password(), "firstName": "A", "lastName": "B"})
    role = client.roles.create({"name": unique("assignrole")})
    try:
        assignment = client.user_role_assignments.create({"userId": user.id, "roleId": role.id, "resourceScope": "Tenant"})
        assert assignment.user_id == user.id and assignment.role_id == role.id
        assert client.user_role_assignments.get(assignment.id).id == assignment.id
        updated = client.user_role_assignments.update(assignment.id, {"userId": user.id, "roleId": role.id, "resourceScope": "Tenant", "inheritsToChildren": True})
        assert updated.inherits_to_children
        assert any(a.id == assignment.id for a in client.user_role_assignments.list(user_id=user.id).objects)
        client.user_role_assignments.delete(assignment.id)
    finally:
        client.roles.delete(role.id)
        client.users.delete(user.id)


def test_credential_scope_assignments(client, harness):
    created = client.credentials.create({"name": unique("scoped"), "userId": harness.owner_id, "authMode": "DirectHeader"})
    role = client.roles.create({"name": unique("scoperole")})
    try:
        cid = created.credential.id
        scope = client.credential_scope_assignments.create({"credentialId": cid, "roleId": role.id, "resourceScope": "Tenant"})
        assert scope.credential_id == cid
        assert client.credential_scope_assignments.get(scope.id).role_id == role.id
        updated = client.credential_scope_assignments.update(scope.id, {"credentialId": cid, "roleId": role.id, "resourceScope": "Tenant", "inheritsToChildren": True})
        assert updated.inherits_to_children
        assert any(s.id == scope.id for s in client.credential_scope_assignments.list(credential_id=cid).objects)
        client.credential_scope_assignments.delete(scope.id)
    finally:
        client.roles.delete(role.id)
        client.credentials.delete(created.credential.id)


def test_sessions_list_get_revoke(client, harness):
    other = harness.owner_client()
    try:
        session_id = other.token_details().session_id
        sessions = client.sessions.list(max_results=1000)
        assert any(s.id == session_id for s in sessions.objects)
        assert client.sessions.get(session_id).user_id == harness.owner_id
        client.sessions.revoke(session_id)
        with pytest.raises(HugoError) as err:
            other.token_details()
        assert err.value.status == 401
    finally:
        other.close()


def test_admin_can_target_other_tenant(admin, harness):
    admin.tenant_id = harness.tenant_id
    try:
        users = admin.users.list()
        assert any(u.email == harness.owner_email for u in users.objects)
    finally:
        admin.tenant_id = ADMIN_TENANT
