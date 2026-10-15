"""Harness bootstrap: provisions a fresh tenant and a tenant administrator using the system administrator, starts a
stub OpenAI-compatible model server for the chat tests, and deletes the tenant when the session ends.

Environment:
    HUGO_SDK_SERVER          Server base URL (default http://127.0.0.1:8800).
    HUGO_SDK_ADMIN_EMAIL     System administrator email (default admin@hugo.local).
    HUGO_SDK_ADMIN_PASSWORD  System administrator password (default password).
    HUGO_SDK_ADMIN_TENANT    System administrator tenant (default default).
    HUGO_SDK_ADMIN_API_KEY   The server's Auth.AdminApiKey (or HUGO_ADMIN_API_KEY); the admin API key test skips without it.

The token routes are rate limited (Auth.RateLimitPerMinute, default 10 per minute per source IP and per identity).
Harness clients retry a 429 after the server's Retry-After, so the harness works against the default limit; a full run
can pause for a minute or two while a window resets.
"""

from __future__ import annotations

import os
import secrets
import uuid
from datetime import datetime, timezone
from typing import Any, Iterator

import pytest

from hugo_sdk import AsyncHugoClient, HugoClient

from stub_llm import StubLlm

SERVER = os.environ.get("HUGO_SDK_SERVER", "http://127.0.0.1:8800").rstrip("/")
ADMIN_EMAIL = os.environ.get("HUGO_SDK_ADMIN_EMAIL", "admin@hugo.local")
ADMIN_PASSWORD = os.environ.get("HUGO_SDK_ADMIN_PASSWORD", "password")
ADMIN_TENANT = os.environ.get("HUGO_SDK_ADMIN_TENANT", "default")
ADMIN_API_KEY = os.environ.get("HUGO_SDK_ADMIN_API_KEY") or os.environ.get("HUGO_ADMIN_API_KEY") or ""

# Retries for 429 RateLimited on harness clients. Each wait honors Retry-After (60 seconds on the token routes).
RATE_LIMIT_RETRIES = 4


def unique(prefix: str) -> str:
    return f"{prefix}-{uuid.uuid4().hex[:8]}"


def strong_password() -> str:
    return "Sdk-" + secrets.token_urlsafe(18)


def new_client(**kwargs: Any) -> HugoClient:
    """A harness client that waits out the token route rate limit instead of failing."""
    kwargs.setdefault("rate_limit_retries", RATE_LIMIT_RETRIES)
    return HugoClient(SERVER, **kwargs)


def new_async_client(**kwargs: Any) -> AsyncHugoClient:
    kwargs.setdefault("rate_limit_retries", RATE_LIMIT_RETRIES)
    return AsyncHugoClient(SERVER, **kwargs)


def admin_login() -> HugoClient:
    """Log in as the system administrator. must_change_password is advisory (D-048), so nothing else is needed."""
    client = new_client()
    client.login(ADMIN_EMAIL, ADMIN_PASSWORD, ADMIN_TENANT)
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
        client = new_client()
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


@pytest.fixture(scope="session")
def stub_llm() -> Iterator[StubLlm]:
    """A stub OpenAI-compatible model server on 127.0.0.1."""
    stub = StubLlm()
    yield stub
    stub.close()


class ModelSetup:
    """A BearerToken key secret, an OpenAICompatible inference endpoint on the stub, and a profile that uses it."""

    def __init__(self, secret: Any, endpoint: Any, profile: Any):
        self.secret = secret
        self.endpoint = endpoint
        self.profile = profile


@pytest.fixture(scope="session")
def model_setup(client: HugoClient, stub_llm: StubLlm) -> Iterator[ModelSetup]:
    secret = client.secrets.create({"name": unique("stub-key"), "kind": "BearerToken", "values": {"token": "stub-api-key"}})
    endpoint = client.model_endpoints.create({"name": unique("stub:model"), "kind": "Inference", "provider": "OpenAICompatible",
                                              "baseUrl": stub_llm.base_url, "model": "stub-model", "apiKeySecretId": secret.id,
                                              "supportsStreaming": True, "maxRetries": 0, "timeoutSeconds": 30})
    profile = client.agent_profiles.create({"name": unique("chat"), "displayName": "SDK chat", "inferenceEndpointId": endpoint.id})
    yield ModelSetup(secret, endpoint, profile)
    for action in (lambda: client.agent_profiles.delete(profile.id), lambda: client.model_endpoints.delete(endpoint.id),
                   lambda: client.secrets.delete(secret.id)):
        try:
            action()
        except Exception:  # noqa: BLE001 - best effort cleanup; the tenant is deleted at the end anyway
            pass
