"""Hugo clients. ``HugoClient`` is blocking; ``AsyncHugoClient`` has the same surface with awaitable methods."""

from __future__ import annotations

from typing import Any, Dict, Optional

import httpx

from . import api
from . import models as m
from .transport import DEFAULT_BASE_URL, AsyncTransport, AuthState, Call, SyncTransport


class _ClientCore:
    """Authentication and sub-clients shared by both clients."""

    def _setup(self, transport: Any) -> None:
        self.transport = transport
        self.system = api.SystemApi(transport)
        self.tenants = api.TenantsApi(transport)
        self.users = api.UsersApi(transport)
        self.credentials = api.CredentialsApi(transport)
        self.roles = api.RolesApi(transport)
        self.permissions = api.PermissionsApi(transport)
        self.role_permissions = api.RolePermissionsApi(transport)
        self.user_role_assignments = api.UserRoleAssignmentsApi(transport)
        self.credential_scope_assignments = api.CredentialScopeAssignmentsApi(transport)
        self.sessions = api.SessionsApi(transport)
        self.audit = api.AuditApi(transport)
        self.request_history = api.RequestHistoryApi(transport)
        self.secrets = api.SecretsApi(transport)
        self.prompt_templates = api.PromptTemplatesApi(transport)
        self.events = api.EventsApi(transport)
        self.settings = api.SettingsApi(transport)
        self.connectors = api.ConnectorsApi(transport)
        self.resources = api.ResourcesApi(transport)
        self.agent_profiles = api.AgentProfilesApi(transport)
        self.model_endpoints = api.ModelEndpointsApi(transport)
        self.tool_invocations = api.ToolInvocationsApi(transport)
        self.approvals = api.ApprovalsApi(transport)
        self.lockdown = api.LockdownApi(transport)
        self.prompt_layers = api.PromptLayersApi(transport)
        # Preview: conversations and chat streaming are verified when the Phase 7 routes land.
        self.conversations = api.ConversationsApi(transport)

    # -- state ----------------------------------------------------------------------------------------------------------

    @property
    def base_url(self) -> str:
        return self.transport.base_url

    @property
    def tenant_id(self) -> Optional[str]:
        """Tenant used for tenant-scoped routes. Set by login; can be changed (for example by a system administrator)."""
        return self.transport.auth.tenant_id

    @tenant_id.setter
    def tenant_id(self, value: Optional[str]) -> None:
        self.transport.auth.tenant_id = value

    @property
    def tenant_override(self) -> Optional[str]:
        """System administrators only: when set, every /v1.0/api/... request outside /v1.0/api/tenants carries
        ?tenantId=<override>, so the flat domain routes (resources, agent profiles, approvals, ...) act on that tenant.
        Included in the signed query string for signed requests."""
        return self.transport.auth.tenant_override

    @tenant_override.setter
    def tenant_override(self, value: Optional[str]) -> None:
        self.transport.auth.tenant_override = value or None

    @property
    def token(self) -> Optional[str]:
        """Current bearer token, if any."""
        return self.transport.auth.token

    @property
    def auth(self) -> AuthState:
        return self.transport.auth

    # -- auth modes -----------------------------------------------------------------------------------------------------

    def use_token(self, token: str, tenant_id: Optional[str] = None) -> None:
        """Send ``Authorization: Bearer <token>``. Automatic refresh is off for tokens supplied this way."""
        a = self.transport.auth
        a.mode, a.token, a.refreshable, a.token_expires_utc = "token", token, False, None
        if tenant_id:
            a.tenant_id = tenant_id

    def use_access_key(self, access_key: str, secret_key: str, tenant_id: Optional[str] = None) -> None:
        """Send x-access-key and x-secret-key on every request (credentials in DirectHeader or Hybrid mode)."""
        a = self.transport.auth
        a.mode, a.access_key, a.secret_key, a.token = "keys", access_key, secret_key, None
        if tenant_id:
            a.tenant_id = tenant_id

    def use_signed_requests(self, access_key: str, secret_key: str, tenant_id: str) -> None:
        """Sign every request with HMAC-SHA256 (credentials in SignedRequest or Hybrid mode). ``tenant_id`` must be the
        credential's tenant; it is part of the signature."""
        if not tenant_id:
            raise ValueError("tenant_id is required for signed requests")
        a = self.transport.auth
        a.mode, a.access_key, a.secret_key, a.tenant_id, a.token = "signed", access_key, secret_key, tenant_id, None

    def use_admin_api_key(self, api_key: str, tenant_id: Optional[str] = None) -> None:
        """Send the system administrator API key (x-api-key) and optional x-tenant-id."""
        a = self.transport.auth
        a.mode, a.admin_api_key, a.token = "admin", api_key, None
        if tenant_id:
            a.tenant_id = tenant_id

    def clear_auth(self) -> None:
        """Forget every credential; requests are sent anonymously."""
        tenant = self.transport.auth.tenant_id
        a = AuthState(tenant_id=tenant, auto_refresh=self.transport.auth.auto_refresh,
                      refresh_skew_seconds=self.transport.auth.refresh_skew_seconds,
                      extra_headers=self.transport.auth.extra_headers, tenant_override=self.transport.auth.tenant_override)
        self.transport.auth = a

    # -- token routes ---------------------------------------------------------------------------------------------------

    def _run(self, call: Call) -> Any:
        return self.transport.run(call)

    def login(self, email: str, password: str, tenant_id: Optional[str] = None) -> Any:
        """POST /v1.0/token with x-email, x-password, and x-tenant-id. Stores the token and tenant. When the result has
        must_change_password, only ``change_password`` and token routes work until the password changes."""
        headers = {"x-email": email, "x-password": password, "x-tenant-id": tenant_id}
        self.transport.auth.mode = "none"
        return self._run(Call("POST", "/v1.0/token", headers=headers, parse=m.TokenResponse.from_dict, after=self.transport.store_token))

    def login_with_access_key(self, access_key: str, secret_key: str) -> Any:
        """Exchange credential keys for a session token (SessionExchange, DirectHeader, or Hybrid credentials)."""
        headers = {"x-access-key": access_key, "x-secret-key": secret_key}
        self.transport.auth.mode = "none"
        return self._run(Call("POST", "/v1.0/token", headers=headers, parse=m.TokenResponse.from_dict, after=self.transport.store_token))

    def refresh(self) -> Any:
        """POST /v1.0/token/refresh; replaces the stored token."""
        return self._run(Call("POST", "/v1.0/token/refresh", parse=m.TokenResponse.from_dict, after=self.transport.store_token))

    def logout(self) -> Any:
        """DELETE /v1.0/token (revoke the session) and forget the token."""

        def after(_: Any) -> None:
            self.transport.auth.token = None
            self.transport.auth.mode = "none"

        return self._run(Call("DELETE", "/v1.0/token", parse=m.TokenValidResponse.from_dict, after=after))

    def validate_token(self) -> Any:
        """GET /v1.0/token."""
        return self._run(Call("GET", "/v1.0/token", parse=m.TokenValidResponse.from_dict))

    def token_details(self) -> Any:
        """GET /v1.0/token/details: the authenticated principal."""
        return self._run(Call("GET", "/v1.0/token/details", parse=m.TokenDetails.from_dict))

    def reauthenticate(self, password: str) -> Any:
        """POST /v1.0/token/reauthenticate. Required before ``secrets.reveal`` on user sessions."""
        return self._run(Call("POST", "/v1.0/token/reauthenticate", body={"password": password}, parse=m.ReauthenticateResponse.from_dict))

    def change_password(self, current_password: str, new_password: str) -> Any:
        """POST /v1.0/api/users/me/password."""
        return self.users.change_password(current_password, new_password)

    def lookup_tenants(self, email: str) -> Any:
        """POST /v1.0/tenants/lookup: tenants an email can sign in to."""
        return self._run(Call("POST", "/v1.0/tenants/lookup", body={"email": email},
                              parse=lambda d: [m.TenantLookupEntry.from_dict(x) for x in (d or [])]))

    def health(self) -> Any:
        return self.system.health()

    def info(self) -> Any:
        return self.system.info()


class HugoClient(_ClientCore):
    """Blocking Hugo client.

    Example::

        client = HugoClient("http://127.0.0.1:8800")
        client.login("anderson@hugo.local", "password")
        for secret in client.secrets.list().objects:
            print(secret.name)
    """

    def __init__(self, base_url: str = DEFAULT_BASE_URL, timeout: float = 300.0, http_client: Optional[httpx.Client] = None,
                 auto_refresh: bool = True, refresh_skew_seconds: int = 60, headers: Optional[Dict[str, str]] = None,
                 tenant_override: Optional[str] = None):
        auth = AuthState(auto_refresh=auto_refresh, refresh_skew_seconds=refresh_skew_seconds, extra_headers=dict(headers or {}),
                         tenant_override=tenant_override)
        self._setup(SyncTransport(base_url, timeout, http_client, auth))

    def close(self) -> None:
        self.transport.close()

    def __enter__(self) -> "HugoClient":
        return self

    def __exit__(self, *exc: Any) -> None:
        self.close()


class AsyncHugoClient(_ClientCore):
    """Asynchronous Hugo client; every method returns an awaitable, and streams are async iterators.

    Example::

        async with AsyncHugoClient("http://127.0.0.1:8800") as client:
            await client.login("anderson@hugo.local", "password")
            async for event in client.events.stream():
                print(event.event, event.json())
    """

    def __init__(self, base_url: str = DEFAULT_BASE_URL, timeout: float = 300.0, http_client: Optional[httpx.AsyncClient] = None,
                 auto_refresh: bool = True, refresh_skew_seconds: int = 60, headers: Optional[Dict[str, str]] = None,
                 tenant_override: Optional[str] = None):
        auth = AuthState(auto_refresh=auto_refresh, refresh_skew_seconds=refresh_skew_seconds, extra_headers=dict(headers or {}),
                         tenant_override=tenant_override)
        self._setup(AsyncTransport(base_url, timeout, http_client, auth))

    async def aclose(self) -> None:
        await self.transport.aclose()

    async def __aenter__(self) -> "AsyncHugoClient":
        return self

    async def __aexit__(self, *exc: Any) -> None:
        await self.aclose()
