"""Harness bootstrap: provisions a fresh tenant and a tenant administrator using the system administrator, and deletes
the tenant when the session ends.

Environment:
    HUGO_SDK_SERVER          Server base URL (default http://127.0.0.1:8800).
    HUGO_SDK_ADMIN_EMAIL     System administrator email (default admin@hugo.local).
    HUGO_SDK_ADMIN_PASSWORD  System administrator password (default password).
    HUGO_SDK_ADMIN_TENANT    System administrator tenant (default default).
"""

from __future__ import annotations

import os
import secrets
import uuid
from datetime import datetime, timezone
from typing import Any, Callable, Dict, Iterator

import pytest

from hugo_sdk import HugoClient, HugoError

SERVER = os.environ.get("HUGO_SDK_SERVER", "http://127.0.0.1:8800").rstrip("/")
ADMIN_EMAIL = os.environ.get("HUGO_SDK_ADMIN_EMAIL", "admin@hugo.local")
ADMIN_PASSWORD = os.environ.get("HUGO_SDK_ADMIN_PASSWORD", "password")
ADMIN_TENANT = os.environ.get("HUGO_SDK_ADMIN_TENANT", "default")

DOMAIN_SKIP_REASON = "route not available on this server yet (Phase 7 preview)"


def unique(prefix: str) -> str:
    return f"{prefix}-{uuid.uuid4().hex[:8]}"


def strong_password() -> str:
    return "Sdk-" + secrets.token_urlsafe(18)


def admin_login() -> HugoClient:
    """Log in as the system administrator. When the seeded account must change its password, change it to a temporary
    password and back to the configured one on the same session, which clears the flag and keeps reruns working."""
    client = HugoClient(SERVER)
    issued = client.login(ADMIN_EMAIL, ADMIN_PASSWORD, ADMIN_TENANT)
    if issued.must_change_password:
        temporary = f"{ADMIN_PASSWORD}-sdk-tmp-{secrets.token_hex(4)}"
        client.change_password(ADMIN_PASSWORD, temporary)
        client.change_password(temporary, ADMIN_PASSWORD)
    return client


class Harness:
    """Provisioned state shared by the tests."""

    def __init__(self, admin: HugoClient, tenant_id: str, owner_email: str, owner_password: str, owner_id: str):
        self.admin = admin
        self.tenant_id = tenant_id
        self.owner_email = owner_email
        self.owner_password = owner_password
        self.owner_id = owner_id

    def owner_client(self) -> HugoClient:
        """A new client logged in as the tenant administrator (its own session)."""
        client = HugoClient(SERVER)
        client.login(self.owner_email, self.owner_password, self.tenant_id)
        return client


@pytest.fixture(scope="session")
def harness() -> Iterator[Harness]:
    admin = admin_login()
    stamp = datetime.now(timezone.utc).strftime("%Y%m%d%H%M%S")
    tenant = admin.tenants.create({"name": f"sdk-python-{stamp}-{secrets.token_hex(2)}", "description": "Hugo Python SDK harness"})
    try:
        password = strong_password()
        email = "owner@sdk-python.test"
        admin.tenant_id = tenant.id
        owner = admin.users.create({"email": email, "password": password, "firstName": "Sdk", "lastName": "Owner",
                                    "isTenantAdmin": True, "mustChangePassword": False})
        admin.tenant_id = ADMIN_TENANT
        yield Harness(admin, tenant.id, email, password, owner.id)
    finally:
        try:
            admin.tenant_id = ADMIN_TENANT
            admin.tenants.delete(tenant.id)
        finally:
            admin.close()


@pytest.fixture(scope="session")
def admin(harness: Harness) -> HugoClient:
    """System administrator client (tenant 'default')."""
    return harness.admin


@pytest.fixture(scope="session")
def client(harness: Harness) -> Iterator[HugoClient]:
    """Tenant administrator client for the provisioned tenant."""
    c = harness.owner_client()
    yield c
    c.close()


def domain_call(fn: Callable[[], Any]) -> Any:
    """Call a preview domain route; skip the test when the server does not have it yet."""
    try:
        return fn()
    except HugoError as e:
        if e.status == 404:
            pytest.skip(f"{DOMAIN_SKIP_REASON}: {e.code or 'NotFound'}")
        raise


def results_summary() -> Dict[str, str]:
    return {"server": SERVER}
