"""Sub-clients for every Hugo REST area.

The same classes serve the sync and async clients: each method builds a ``Call`` and hands it to the transport. With
``HugoClient`` a method returns the result; with ``AsyncHugoClient`` it returns an awaitable (and streaming methods
return async iterators).

Bodies accept a dictionary with the server's camelCase field names, or a model instance.
"""

from __future__ import annotations

import asyncio
import time
from datetime import datetime
from typing import Any, Dict, List, Optional, Union

from . import models as m
from .sse import ChatEvent
from .transport import AsyncTransport, Call, seg

Body = Union[Dict[str, Any], m.Model]


def _enum(max_results: Optional[int] = None, skip: Optional[int] = None, ordering: Optional[str] = None,
          search: Optional[str] = None, active_only: Optional[bool] = None, include_built_in: Optional[bool] = None,
          **extra: Any) -> Dict[str, Any]:
    params: Dict[str, Any] = {"maxResults": max_results, "skip": skip, "ordering": ordering, "search": search,
                              "activeOnly": active_only, "includeBuiltIn": include_built_in}
    params.update(extra)
    return params


def _page(item: type):
    return lambda d: m.EnumerationResult.parse(d, item)


def _paged(item: type):
    return lambda d: m.PagedResult.parse(d, item)


def _list(item: type):
    return lambda d: [item.from_dict(x) if isinstance(x, dict) else x for x in (d or [])]


def _flex(item: type):
    """Parse either a bare array or an enumeration result into a list of items."""

    def parse(d: Any) -> List[Any]:
        if isinstance(d, dict) and "objects" in d:
            d = d.get("objects")
        return [item.from_dict(x) if isinstance(x, dict) else x for x in (d or [])]

    return parse


class _Api:
    def __init__(self, transport: Any):
        self._t = transport

    def _run(self, call: Call) -> Any:
        return self._t.run(call)

    def _tp(self, suffix: str = "") -> str:
        return self._t.tenant_path(suffix)

    @property
    def _is_async(self) -> bool:
        return isinstance(self._t, AsyncTransport)


class _Crud(_Api):
    """List, get, create, update, delete for a collection. ``_tenant`` selects /v1.0/api/tenants/{tenantId}/<segment>."""

    _segment = ""
    _model: type = m.Model
    _tenant = True

    def _base(self) -> str:
        return self._tp("/" + self._segment) if self._tenant else "/v1.0/api/" + self._segment

    def _item(self, id: str) -> str:
        return self._base() + "/" + seg(id)

    def list(self, max_results: Optional[int] = None, skip: Optional[int] = None, ordering: Optional[str] = None,
             search: Optional[str] = None, active_only: Optional[bool] = None, include_built_in: Optional[bool] = None,
             **filters: Any) -> Any:
        """List records. Returns an EnumerationResult; extra keyword arguments are sent as query filters."""
        return self._run(Call("GET", self._base(), _enum(max_results, skip, ordering, search, active_only, include_built_in, **filters),
                              parse=_page(self._model)))

    def get(self, id: str) -> Any:
        """Read a record."""
        return self._run(Call("GET", self._item(id), parse=self._model.from_dict))

    def create(self, body: Body) -> Any:
        """Create a record."""
        return self._run(Call("POST", self._base(), body=body, parse=self._model.from_dict))

    def update(self, id: str, body: Body) -> Any:
        """Update a record."""
        return self._run(Call("PUT", self._item(id), body=body, parse=self._model.from_dict))

    def delete(self, id: str) -> Any:
        """Delete a record (204)."""
        return self._run(Call("DELETE", self._item(id)))


# ---------------------------------------------------------------------------------------------------------------------
# Identity and admin
# ---------------------------------------------------------------------------------------------------------------------


class TenantsApi(_Crud):
    """Tenants (/v1.0/api/tenants). Creating and deleting tenants requires a system administrator."""

    _segment = "tenants"
    _model = m.Tenant
    _tenant = False


class UsersApi(_Crud):
    """Users in the client's tenant, plus the signed-in user's own routes."""

    _segment = "users"
    _model = m.User

    def me(self) -> Any:
        """GET /v1.0/api/users/me."""
        return self._run(Call("GET", "/v1.0/api/users/me", parse=m.User.from_dict))

    def update_me(self, body: Body) -> Any:
        """PUT /v1.0/api/users/me (first name, last name, preferred locale, default agent profile)."""
        return self._run(Call("PUT", "/v1.0/api/users/me", body=body, parse=m.User.from_dict))

    def change_password(self, current_password: str, new_password: str) -> Any:
        """POST /v1.0/api/users/me/password. Clears must-change-password and revokes the user's other sessions."""
        return self._run(Call("POST", "/v1.0/api/users/me/password",
                              body={"currentPassword": current_password, "newPassword": new_password}, parse=m.User.from_dict))

    def permissions(self, id: str) -> Any:
        """Effective permissions of a user."""
        return self._run(Call("GET", self._item(id) + "/permissions", parse=m.EffectivePermissions.from_dict))

    def reset_password(self, id: str, new_password: str, must_change_password: Optional[bool] = None) -> Any:
        """Administrator password reset. Sets the advisory must-change-password flag on the user (the server default
        when the target is someone else) unless must_change_password is False; sent as the mustChangePassword query
        parameter. The flag blocks nothing: the dashboard shows a skippable prompt."""
        return self._run(Call("POST", self._item(id) + "/password", {"mustChangePassword": must_change_password},
                              body={"newPassword": new_password}, parse=m.User.from_dict))


class CredentialsApi(_Crud):
    """Credentials (access keys). Create and rotate return the secret key once."""

    _segment = "credentials"
    _model = m.Credential

    def create(self, body: Body) -> Any:
        return self._run(Call("POST", self._base(), body=body, parse=m.CredentialCreateResponse.from_dict))

    def rotate(self, id: str) -> Any:
        """Rotate the secret key; the new secret is returned once."""
        return self._run(Call("POST", self._item(id) + "/rotate", parse=m.CredentialCreateResponse.from_dict))

    def permissions(self, id: str) -> Any:
        """Effective permissions of a credential."""
        return self._run(Call("GET", self._item(id) + "/permissions", parse=m.EffectivePermissions.from_dict))


class RolesApi(_Crud):
    """Roles, including built-in roles."""

    _segment = "roles"
    _model = m.Role

    def clone(self, id: str, body: Body) -> Any:
        """Clone a role (including built-ins) into a custom role."""
        return self._run(Call("POST", self._item(id) + "/clone", body=body, parse=m.Role.from_dict))


class PermissionsApi(_Crud):
    """Permissions, including built-ins."""

    _segment = "permissions"
    _model = m.Permission


class RolePermissionsApi(_Crud):
    """Role to permission mappings (list filter role_id)."""

    _segment = "role-permissions"
    _model = m.RolePermissionMap

    def list(self, role_id: Optional[str] = None, **kwargs: Any) -> Any:  # type: ignore[override]
        return super().list(roleId=role_id, **kwargs)


class UserRoleAssignmentsApi(_Crud):
    """User role assignments (list filter user_id)."""

    _segment = "user-role-assignments"
    _model = m.UserRoleAssignment

    def list(self, user_id: Optional[str] = None, **kwargs: Any) -> Any:  # type: ignore[override]
        return super().list(userId=user_id, **kwargs)


class CredentialScopeAssignmentsApi(_Crud):
    """Credential scope assignments (list filter credential_id)."""

    _segment = "credential-scope-assignments"
    _model = m.CredentialScopeAssignment

    def list(self, credential_id: Optional[str] = None, **kwargs: Any) -> Any:  # type: ignore[override]
        return super().list(credentialId=credential_id, **kwargs)


class SessionsApi(_Crud):
    """Authentication sessions in the tenant (list, get, revoke). Each session carries principal_name."""

    _segment = "sessions"
    _model = m.AuthSession

    def list(self, principal_type: Optional[str] = None, user_id: Optional[str] = None, credential_id: Optional[str] = None,
             **kwargs: Any) -> Any:  # type: ignore[override]
        """List sessions. Filters: principal_type (Administrator, User, Credential), user_id, credential_id."""
        return super().list(principalType=principal_type, userId=user_id, credentialId=credential_id, **kwargs)

    def revoke(self, id: str) -> Any:
        """Revoke a session."""
        return self.delete(id)


def _audit_params(event_type: Optional[str], principal_id: Optional[str], result: Optional[str], from_utc: Optional[datetime],
                  to_utc: Optional[datetime], page_number: Optional[int], page_size: Optional[int]) -> Dict[str, Any]:
    return {"eventType": event_type, "principalId": principal_id, "result": result, "fromUtc": from_utc, "toUtc": to_utc,
            "pageNumber": page_number, "pageSize": page_size}


class AuditApi(_Api):
    """Audit records in the client's tenant."""

    def list(self, event_type: Optional[str] = None, principal_id: Optional[str] = None, result: Optional[str] = None,
             from_utc: Optional[datetime] = None, to_utc: Optional[datetime] = None, page_number: Optional[int] = None,
             page_size: Optional[int] = None) -> Any:
        """List audit records. Returns a PagedResult of AuditRecord."""
        return self._run(Call("GET", self._tp("/audit"), _audit_params(event_type, principal_id, result, from_utc, to_utc, page_number, page_size),
                              parse=_paged(m.AuditRecord)))

    def summary(self, event_type: Optional[str] = None, principal_id: Optional[str] = None, result: Optional[str] = None,
                from_utc: Optional[datetime] = None, to_utc: Optional[datetime] = None) -> Any:
        """Count matching records by event type. Returns AuditSummary."""
        return self._run(Call("GET", self._tp("/audit/summary"),
                              _audit_params(event_type, principal_id, result, from_utc, to_utc, None, None),
                              parse=m.AuditSummary.from_dict))

    def get(self, id: str) -> Any:
        return self._run(Call("GET", self._tp("/audit/" + seg(id)), parse=m.AuditRecord.from_dict))

    def delete(self, id: str) -> Any:
        return self._run(Call("DELETE", self._tp("/audit/" + seg(id))))

    def delete_many(self, event_type: Optional[str] = None, principal_id: Optional[str] = None, result: Optional[str] = None,
                    from_utc: Optional[datetime] = None, to_utc: Optional[datetime] = None) -> Any:
        """Delete audit records matching the filter. Returns DeleteCountResponse."""
        return self._run(Call("DELETE", self._tp("/audit"), _audit_params(event_type, principal_id, result, from_utc, to_utc, None, None),
                              parse=m.DeleteCountResponse.from_dict))


class RequestHistoryApi(_Api):
    """Captured requests (/v1.0/api/request-history)."""

    _base = "/v1.0/api/request-history"

    @staticmethod
    def _params(method, status_code, path_contains, from_utc, to_utc, page_number, page_size, tenant_id, user_id) -> Dict[str, Any]:
        return {"method": method, "statusCode": status_code, "pathContains": path_contains, "fromUtc": from_utc, "toUtc": to_utc,
                "pageNumber": page_number, "pageSize": page_size, "tenantId": tenant_id, "userId": user_id}

    def list(self, method: Optional[str] = None, status_code: Optional[int] = None, path_contains: Optional[str] = None,
             from_utc: Optional[datetime] = None, to_utc: Optional[datetime] = None, page_number: Optional[int] = None,
             page_size: Optional[int] = None, tenant_id: Optional[str] = None, user_id: Optional[str] = None) -> Any:
        """List captured requests. Returns a PagedResult of RequestHistoryEntry."""
        return self._run(Call("GET", self._base, self._params(method, status_code, path_contains, from_utc, to_utc, page_number,
                                                              page_size, tenant_id, user_id), parse=_paged(m.RequestHistoryEntry)))

    def summary(self, from_utc: Optional[datetime] = None, to_utc: Optional[datetime] = None, bucket_minutes: Optional[int] = None) -> Any:
        """Time-bucketed request counts."""
        return self._run(Call("GET", self._base + "/summary", {"fromUtc": from_utc, "toUtc": to_utc, "bucketMinutes": bucket_minutes},
                              parse=m.RequestHistorySummary.from_dict))

    def get(self, id: str) -> Any:
        return self._run(Call("GET", self._base + "/" + seg(id), parse=m.RequestHistoryEntry.from_dict))

    def delete(self, id: str) -> Any:
        return self._run(Call("DELETE", self._base + "/" + seg(id)))

    def delete_many(self, method: Optional[str] = None, status_code: Optional[int] = None, path_contains: Optional[str] = None,
                    from_utc: Optional[datetime] = None, to_utc: Optional[datetime] = None, tenant_id: Optional[str] = None,
                    user_id: Optional[str] = None) -> Any:
        """Delete captured requests matching the filter. Returns DeleteCountResponse."""
        return self._run(Call("DELETE", self._base, self._params(method, status_code, path_contains, from_utc, to_utc, None, None,
                                                                 tenant_id, user_id), parse=m.DeleteCountResponse.from_dict))


class SecretsApi(_Crud):
    """Secrets vault. Values are write-only except through ``reveal``, which needs a recent ``client.reauthenticate``."""

    _segment = "secrets"
    _model = m.Secret

    def list(self, kind: Optional[str] = None, **kwargs: Any) -> Any:  # type: ignore[override]
        """List secrets. Filter kind: one of models.SECRET_KINDS (an unknown kind answers 400 ValidationFailed)."""
        return super().list(kind=kind, **kwargs)

    def set_values(self, id: str, values: Dict[str, str], remove: Optional[List[str]] = None) -> Any:
        """Replace the supplied field values; omitted fields keep their stored values; ``remove`` deletes fields."""
        return self._run(Call("PUT", self._item(id) + "/values", body={"values": values, "remove": remove}, parse=m.Secret.from_dict))

    def referrers(self, id: str) -> Any:
        """Resources and model endpoints that use the secret."""
        return self._run(Call("GET", self._item(id) + "/referrers", parse=_list(m.SecretReferrer)))

    def parse(self, content: str, passphrase: Optional[str] = None, file_name: Optional[str] = None) -> Any:
        """Describe an SSH key, service account JSON, kubeconfig, or PEM certificate without storing it."""
        return self._run(Call("POST", self._base() + "/parse", body={"content": content, "passphrase": passphrase, "fileName": file_name},
                              parse=m.SecretImportResult.from_dict))

    def reveal(self, id: str, reason: str) -> Any:
        """Reveal plaintext values. Requires the Reveal permission and, for user sessions, ``client.reauthenticate``
        within Secrets.RevealReauthMinutes (otherwise 403 ReauthenticationRequired). Audited."""
        return self._run(Call("POST", self._item(id) + "/reveal", body={"reason": reason}, parse=m.SecretRevealResponse.from_dict))


class PromptTemplatesApi(_Crud):
    """Prompt templates (tenant and built-in) with versions."""

    _segment = "prompt-templates"
    _model = m.PromptTemplate

    def versions(self, id: str) -> Any:
        return self._run(Call("GET", self._item(id) + "/versions", parse=_list(m.PromptTemplateVersion)))

    def version(self, id: str, version: int) -> Any:
        return self._run(Call("GET", self._item(id) + "/versions/" + seg(str(version)), parse=m.PromptTemplateVersion.from_dict))

    def restore(self, id: str, version: int) -> Any:
        """Restore a version as a new version."""
        return self._run(Call("POST", self._item(id) + "/restore/" + seg(str(version)), parse=m.PromptTemplate.from_dict))

    def clone(self, id: str, name: str) -> Any:
        return self._run(Call("POST", self._item(id) + "/clone", body={"name": name}, parse=m.PromptTemplate.from_dict))

    def reset(self, id: str) -> Any:
        """Reset a cloned built-in template to the shipped default."""
        return self._run(Call("POST", self._item(id) + "/reset", parse=m.PromptTemplate.from_dict))

    def render(self, id: str, context: Optional[Dict[str, Any]] = None) -> Any:
        """Render a stored template with sample data."""
        return self._run(Call("POST", self._item(id) + "/render", body={"context": context or {}}, parse=m.PromptRenderResponse.from_dict))

    def render_ad_hoc(self, content: str, context: Optional[Dict[str, Any]] = None) -> Any:
        """Render ad hoc template content with sample data."""
        return self._run(Call("POST", self._base() + "/render", body={"content": content, "context": context or {}},
                              parse=m.PromptRenderResponse.from_dict))


class EventsApi(_Api):
    """Server-sent event stream of the caller's tenant (stream.opened, heartbeat, secret.*, settings.changed, ...)."""

    def stream(self) -> Any:
        """Iterate SseEvent objects until the server closes the stream or the caller stops iterating. Sync clients get an
        iterator; async clients get an async iterator."""
        return self._t.stream(Call("GET", "/v1.0/api/events"))


class SettingsApi(_Api):
    """Server settings (system administrators)."""

    def get(self) -> Any:
        return self._run(Call("GET", "/v1.0/api/settings", parse=m.SettingsView.from_dict))

    def update(self, values: Dict[str, Any]) -> Any:
        """Update settings by field path, for example {"RequestHistory.RetentionDays": "14"}."""
        body = {"values": [{"path": k, "value": None if v is None else str(v)} for k, v in values.items()]}
        return self._run(Call("PUT", "/v1.0/api/settings", body=body, parse=m.SettingsSaveResponse.from_dict))

    def test_database(self) -> Any:
        return self._run(Call("POST", "/v1.0/api/settings/database/test", parse=m.DatabaseTestResponse.from_dict))


class SystemApi(_Api):
    """Health, server information, observability status, master key rotation, and restart."""

    def health(self) -> Any:
        """GET /v1.0/api/health (anonymous)."""
        return self._run(Call("GET", "/v1.0/api/health", parse=m.HealthResponse.from_dict))

    def live(self) -> Any:
        return self._run(Call("GET", "/v1.0/api/health/live", parse=m.HealthResponse.from_dict))

    def info(self) -> Any:
        """GET /v1.0/api/info."""
        return self._run(Call("GET", "/v1.0/api/info", parse=m.ServerInfo.from_dict))

    def server_info(self) -> Any:
        return self._run(Call("GET", "/v1.0/api/server-info", parse=m.ServerInfo.from_dict))

    def observability(self) -> Any:
        return self._run(Call("GET", "/v1.0/api/observability/status", parse=_list(m.ObservabilityServiceStatus)))

    def master_key_status(self) -> Any:
        return self._run(Call("GET", "/v1.0/api/system/master-key", parse=m.MasterKeyRotationStatus.from_dict))

    def rotate_master_key(self) -> Any:
        return self._run(Call("POST", "/v1.0/api/system/master-key/rotate", parse=m.MasterKeyRotationStatus.from_dict))

    def restart(self) -> Any:
        """Restart the server process (the container runtime restarts it)."""
        return self._run(Call("POST", "/v1.0/api/system/restart", parse=m.RestartResponse.from_dict))


# ---------------------------------------------------------------------------------------------------------------------
# Domain
# ---------------------------------------------------------------------------------------------------------------------


class ConnectorsApi(_Api):
    """Connector descriptors with settings schema, secret slots, and tools."""

    def list(self) -> Any:
        return self._run(Call("GET", "/v1.0/api/connectors", parse=_flex(m.ConnectorView)))

    def get(self, type: str) -> Any:
        return self._run(Call("GET", "/v1.0/api/connectors/" + seg(type), parse=m.ConnectorView.from_dict))


class ResourcesApi(_Crud):
    """Resources. The tenant comes from the token (or tenant_override for system administrators)."""

    _segment = "resources"
    _model = m.Resource
    _tenant = False

    def list(self, connector_type: Optional[str] = None, environment: Optional[str] = None, health: Optional[str] = None,
             **kwargs: Any) -> Any:  # type: ignore[override]
        """List resources. Filters: connector_type, environment, health (Healthy, Unhealthy, Unknown)."""
        return super().list(connectorType=connector_type, environment=environment, health=health, **kwargs)

    def test(self, id: str) -> Any:
        """Test a saved resource's connection and record the result."""
        return self._run(Call("POST", self._item(id) + "/test", parse=m.ResourceTestResponse.from_dict))

    def test_unsaved(self, body: Body) -> Any:
        """Validate and test unsaved resource settings."""
        return self._run(Call("POST", self._base() + "/test", body=body, parse=m.ResourceTestResponse.from_dict))

    def duplicate(self, id: str, name: str) -> Any:
        return self._run(Call("POST", self._item(id) + "/duplicate", body={"name": name}, parse=m.Resource.from_dict))

    def referrers(self, id: str) -> Any:
        return self._run(Call("GET", self._item(id) + "/referrers", parse=_list(m.ResourceReferrer)))

    def invoke(self, id: str, tool_name: str, arguments: Optional[Dict[str, Any]] = None) -> Any:
        """Invoke a tool on a resource directly (202 with the approval while approval is pending)."""
        return self._run(Call("POST", self._item(id) + "/invoke", body={"toolName": tool_name, "arguments": arguments or {}},
                              parse=m.ToolInvocationOutcome.from_dict))

    def knowledge(self, id: str) -> Any:
        """List a resource's knowledge entries."""
        return self._run(Call("GET", self._item(id) + "/knowledge", parse=_list(m.ResourceKnowledge)))

    def knowledge_scope(self, id: str, scope: str) -> Any:
        return self._run(Call("GET", self._item(id) + "/knowledge/" + seg(scope), parse=_list(m.ResourceKnowledge)))

    def get_knowledge(self, id: str, scope: str, key: str = "_root") -> Any:
        return self._run(Call("GET", self._item(id) + "/knowledge/" + seg(scope) + "/" + seg(key), parse=m.ResourceKnowledge.from_dict))

    def put_knowledge(self, id: str, scope: str, content: str, key: Optional[str] = None, source: Optional[str] = None) -> Any:
        """Write a knowledge entry (the scope root when key is None)."""
        path = self._item(id) + "/knowledge/" + seg(scope) + ("/" + seg(key) if key else "")
        return self._run(Call("PUT", path, body={"content": content, "source": source}, parse=m.ResourceKnowledge.from_dict))

    def delete_knowledge(self, id: str, scope: str, key: Optional[str] = None) -> Any:
        path = self._item(id) + "/knowledge/" + seg(scope) + ("/" + seg(key) if key else "")
        return self._run(Call("DELETE", path))

    def knowledge_versions(self, id: str, scope: str, key: str = "_root") -> Any:
        return self._run(Call("GET", self._item(id) + "/knowledge/" + seg(scope) + "/" + seg(key) + "/versions",
                              parse=_list(m.ResourceKnowledgeVersion)))


class AgentProfilesApi(_Crud):
    """Agent profiles with bindings, compiled instructions, and caller tool views."""

    _segment = "agent-profiles"
    _model = m.AgentProfile
    _tenant = False

    def duplicate(self, id: str, name: str) -> Any:
        return self._run(Call("POST", self._item(id) + "/duplicate", body={"name": name}, parse=m.AgentProfile.from_dict))

    def ask(self, id: str, message: str, conversation_id: Optional[str] = None) -> Any:
        """POST /v1.0/api/agent-profiles/{id}/ask: delegate a task to the profile's own agent loop and wait for the
        answer. Approvals wait up to Approvals.McpWaitSeconds, then the result reports ApprovalPending. Returns an
        AgentDelegationResult; a turn already running in the conversation answers 409 TurnInProgress."""
        return self._run(Call("POST", self._item(id) + "/ask", body={"message": message, "conversationId": conversation_id},
                              parse=m.AgentDelegationResult.from_dict))

    def compiled_instructions(self, id: str, surface: Optional[str] = None) -> Any:
        """Compiled instructions as a caller would receive them (surface Chat, Mcp, A2A, or OpenAI)."""
        return self._run(Call("GET", self._item(id) + "/compiled-instructions", {"surface": surface}, parse=m.CompiledInstructions.from_dict))

    def compiled_instructions_text(self, id: str, surface: Optional[str] = None) -> Any:
        """Compiled instructions as plain text (GET ?text=true)."""
        return self._run(Call("GET", self._item(id) + "/compiled-instructions", {"surface": surface, "text": True},
                              raw_text=True, accept="text/plain"))

    def tools(self, id: str, surface: Optional[str] = None) -> Any:
        """Tools as the caller would see them on a surface (Chat, Mcp, A2A, OpenAI)."""
        return self._run(Call("GET", self._item(id) + "/tools", {"surface": surface}, parse=m.CallerToolCatalog.from_dict))

    def card(self, id: str) -> Any:
        """Preview of the A2A agent card."""
        return self._run(Call("GET", self._item(id) + "/card", parse=m.AgentCardPreview.from_dict))

    def list_bindings(self, id: str) -> Any:
        return self._run(Call("GET", self._item(id) + "/bindings", parse=_flex(m.Binding)))

    def get_binding(self, id: str, binding_id: str) -> Any:
        return self._run(Call("GET", self._item(id) + "/bindings/" + seg(binding_id), parse=m.Binding.from_dict))

    def create_binding(self, id: str, body: Body) -> Any:
        """Bind a resource: {"resourceId", "alias", "accessMode": ReadOnly|ReadWriteWithApproval|ReadWrite, ...}."""
        return self._run(Call("POST", self._item(id) + "/bindings", body=body, parse=m.Binding.from_dict))

    def update_binding(self, id: str, binding_id: str, body: Body) -> Any:
        return self._run(Call("PUT", self._item(id) + "/bindings/" + seg(binding_id), body=body, parse=m.Binding.from_dict))

    def delete_binding(self, id: str, binding_id: str) -> Any:
        return self._run(Call("DELETE", self._item(id) + "/bindings/" + seg(binding_id)))

    def get_tool_rules(self, id: str, binding_id: str) -> Any:
        return self._run(Call("GET", self._item(id) + "/bindings/" + seg(binding_id) + "/tool-rules", parse=_list(m.BindingToolRule)))

    def set_tool_rules(self, id: str, binding_id: str, rules: List[Body]) -> Any:
        """Replace a binding's tool rules: [{"toolName", "rule": Allow|Deny|RequireApproval}]."""
        return self._run(Call("PUT", self._item(id) + "/bindings/" + seg(binding_id) + "/tool-rules", body=rules, parse=m.Binding.from_dict))


class PromptLayersApi(_Api):
    """Layered prompt templates by key (/v1.0/api/prompt-templates): Built-in, Global, and Tenant layers. Saving the
    Global layer requires a system administrator."""

    _base = "/v1.0/api/prompt-templates"

    def _key(self, key: str) -> str:
        return self._base + "/" + seg(key)

    def list(self) -> Any:
        """Every template key with its layers and the layer in effect."""
        return self._run(Call("GET", self._base, parse=_flex(m.PromptTemplateView)))

    def get(self, key: str) -> Any:
        return self._run(Call("GET", self._key(key), parse=m.PromptTemplateView.from_dict))

    def save(self, key: str, content: str, change_note: Optional[str] = None, layer: str = "Tenant") -> Any:
        """Save content at a layer (Tenant, or Global for system administrators); creates a version."""
        return self._run(Call("PUT", self._key(key), {"layer": layer}, body={"content": content, "changeNote": change_note},
                              parse=m.PromptTemplateView.from_dict))

    def reset(self, key: str, layer: str = "Tenant") -> Any:
        """Reset Tenant to the Global default, or Global to the Built-in."""
        return self._run(Call("POST", self._key(key) + "/reset", {"layer": layer}, parse=m.PromptTemplateView.from_dict))

    def versions(self, key: str, layer: str = "Tenant") -> Any:
        """Version history of a layer, newest first."""
        return self._run(Call("GET", self._key(key) + "/versions", {"layer": layer}, parse=_list(m.PromptTemplateVersion)))

    def builtin(self, key: str) -> Any:
        """The shipped Built-in content of a key."""
        return self._run(Call("GET", self._key(key) + "/builtin", parse=m.BuiltInTemplate.from_dict))

    def diff(self, key: str, layer: str = "Tenant") -> Any:
        """Line diff of a layer against the layer below it."""
        return self._run(Call("GET", self._key(key) + "/diff", {"layer": layer}, parse=_list(m.PromptDiffLine)))

    def render(self, content: str, context: Optional[Dict[str, Any]] = None) -> Any:
        """Render content with sample data; includes resolve through the layers."""
        return self._run(Call("POST", self._base + "/render", body={"content": content, "context": context or {}},
                              parse=m.PromptRenderResponse.from_dict))


class ModelEndpointsApi(_Crud):
    """Inference and embedding endpoints."""

    _segment = "model-endpoints"
    _model = m.ModelEndpoint
    _tenant = False

    def list(self, kind: Optional[str] = None, provider: Optional[str] = None, health: Optional[str] = None,
             **kwargs: Any) -> Any:  # type: ignore[override]
        """List endpoints. Filters: kind (Inference, Embedding), provider (Ollama, OpenAI, OpenAICompatible, ...), health
        (Healthy, Unhealthy, Unknown). An unknown value answers 400 ValidationFailed listing the allowed values."""
        return super().list(kind=kind, provider=provider, health=health, **kwargs)

    def validate(self, id: str) -> Any:
        """Validate a saved endpoint with a tiny prompt (or embedding) and record its health."""
        return self._run(Call("POST", self._item(id) + "/validate", parse=m.ModelValidationResult.from_dict))

    def validate_draft(self, body: Body) -> Any:
        """POST /v1.0/api/model-endpoints/validate: validate unsaved settings (the create body) with a tiny prompt or
        embedding. Nothing is stored. Returns a ModelValidationResult."""
        return self._run(Call("POST", self._base() + "/validate", body=body, parse=m.ModelValidationResult.from_dict))

    def models(self, id: str) -> Any:
        """Models the provider reports (502 ProviderUnreachable or ProviderTimeout when it cannot be reached)."""
        return self._run(Call("GET", self._item(id) + "/models"))

    def health_history(self, id: str, max_results: Optional[int] = None) -> Any:
        """Recent health checks (Validate and the server's Sweep), newest first, as ModelHealthCheck objects.
        max_results is 1 to 1000 (server default 50)."""
        return self._run(Call("GET", self._item(id) + "/health-history", {"maxResults": max_results}, parse=_list(m.ModelHealthCheck)))


def _tool_call(tool_name: str, arguments: Optional[Dict[str, Any]], profile: Optional[str], profile_id: Optional[str],
               resource: Optional[str], resource_id: Optional[str]) -> Dict[str, Any]:
    return {"toolName": tool_name, "arguments": arguments or {}, "profile": profile, "profileId": profile_id,
            "resource": resource, "resourceId": resource_id}


class ToolInvocationsApi(_Api):
    """Tool invocation records, dry runs, and direct invocation through the full pipeline."""

    _base = "/v1.0/api/tool-invocations"

    def list(self, max_results: Optional[int] = None, skip: Optional[int] = None, effect: Optional[str] = None,
             from_utc: Optional[datetime] = None, to_utc: Optional[datetime] = None, **filters: Any) -> Any:
        """List invocation records. Filters: effect (Read, Write, Destructive), from_utc, to_utc, and as camelCase keyword
        arguments agentProfileId, resourceId, toolName, decision, status, surface, conversationId, principalId, and since
        (an older alias of fromUtc)."""
        return self._run(Call("GET", self._base, _enum(max_results, skip, effect=effect, fromUtc=from_utc, toUtc=to_utc, **filters),
                              parse=_page(m.ToolInvocation)))

    def get(self, id: str) -> Any:
        return self._run(Call("GET", self._base + "/" + seg(id), parse=m.ToolInvocation.from_dict))

    def summary(self, since: Optional[datetime] = None) -> Any:
        return self._run(Call("GET", self._base + "/summary", {"since": since}, parse=m.ToolInvocationSummary.from_dict))

    def dry_run(self, tool_name: str, arguments: Optional[Dict[str, Any]] = None, profile: Optional[str] = None,
                profile_id: Optional[str] = None, resource: Optional[str] = None, resource_id: Optional[str] = None) -> Any:
        """Resolve, authorize, validate, classify, and apply rules only; returns the decision (nothing runs)."""
        return self._run(Call("POST", self._base + "/dry-run", body=_tool_call(tool_name, arguments, profile, profile_id, resource, resource_id),
                              parse=m.ToolInvocationOutcome.from_dict))

    def invoke(self, tool_name: str, arguments: Optional[Dict[str, Any]] = None, profile: Optional[str] = None,
               profile_id: Optional[str] = None, resource: Optional[str] = None, resource_id: Optional[str] = None) -> Any:
        """Invoke a tool as a profile. While approval is pending the outcome carries approval_id (HTTP 202)."""
        return self._run(Call("POST", self._base, body=_tool_call(tool_name, arguments, profile, profile_id, resource, resource_id),
                              parse=m.ToolInvocationOutcome.from_dict))


class ApprovalsApi(_Api):
    """Approvals for Write and Destructive tool calls."""

    _base = "/v1.0/api/approvals"

    def list(self, max_results: Optional[int] = None, skip: Optional[int] = None, status: Optional[str] = None,
             effect: Optional[str] = None, from_utc: Optional[datetime] = None, to_utc: Optional[datetime] = None,
             **filters: Any) -> Any:
        """List approvals. Filters: status, effect (Read, Write, Destructive), from_utc, to_utc, and as camelCase keyword
        arguments agentProfileId, resourceId, principalId, toolName."""
        return self._run(Call("GET", self._base, _enum(max_results, skip, status=status, effect=effect, fromUtc=from_utc, toUtc=to_utc, **filters),
                              parse=_page(m.Approval)))

    def get(self, id: str) -> Any:
        return self._run(Call("GET", self._base + "/" + seg(id), parse=m.Approval.from_dict))

    def approve(self, id: str, note: Optional[str] = None) -> Any:
        return self._run(Call("POST", self._base + "/" + seg(id) + "/approve", body={"note": note}, parse=m.Approval.from_dict))

    def reject(self, id: str, note: Optional[str] = None) -> Any:
        return self._run(Call("POST", self._base + "/" + seg(id) + "/reject", body={"note": note}, parse=m.Approval.from_dict))

    def cancel(self, id: str, note: Optional[str] = None) -> Any:
        return self._run(Call("POST", self._base + "/" + seg(id) + "/cancel", body={"note": note}, parse=m.Approval.from_dict))

    def await_decision(self, id: str, timeout: float = 900.0, poll_interval: float = 2.0) -> Any:
        """Poll until the approval leaves Pending or the timeout passes. Returns the last Approval read."""
        if self._is_async:
            return self._await_async(id, timeout, poll_interval)
        deadline = time.monotonic() + timeout
        while True:
            approval = self.get(id)
            if approval.is_decided or time.monotonic() >= deadline:
                return approval
            time.sleep(poll_interval)

    async def _await_async(self, id: str, timeout: float, poll_interval: float) -> Any:
        loop = asyncio.get_running_loop()
        deadline = loop.time() + timeout
        while True:
            approval = await self.get(id)
            if approval.is_decided or loop.time() >= deadline:
                return approval
            await asyncio.sleep(poll_interval)


class LockdownApi(_Api):
    """Tenant lockdown at /v1.0/api/tenant/lockdown."""

    _path = "/v1.0/api/tenant/lockdown"

    def get(self) -> Any:
        return self._run(Call("GET", self._path, parse=m.LockdownState.from_dict))

    def enable(self, reason: Optional[str] = None) -> Any:
        """Deny every non-Read call in the tenant and cancel pending approvals."""
        return self._run(Call("POST", self._path, body={"reason": reason}, parse=m.LockdownState.from_dict))

    def disable(self) -> Any:
        return self._run(Call("DELETE", self._path, parse=m.LockdownState.from_dict))


class AnalyticsApi(_Api):
    """Tenant analytics (/v1.0/api/analytics/...). Each report covers from..to (default: the last 24 hours) in buckets of
    ``interval`` minute, hour, or day (chosen by window length when omitted). Returns an AnalyticsReport."""

    def _report(self, kind: str, from_utc: Optional[datetime], to_utc: Optional[datetime], interval: Optional[str]) -> Any:
        return self._run(Call("GET", "/v1.0/api/analytics/" + kind, {"from": from_utc, "to": to_utc, "interval": interval},
                              parse=m.AnalyticsReport.from_dict))

    def overview(self, from_utc: Optional[datetime] = None, to_utc: Optional[datetime] = None, interval: Optional[str] = None) -> Any:
        """Conversations, turns, tokens, tool calls, approvals, and latency per bucket."""
        return self._report("overview", from_utc, to_utc, interval)

    def tokens(self, from_utc: Optional[datetime] = None, to_utc: Optional[datetime] = None, interval: Optional[str] = None) -> Any:
        """Token usage per bucket with window totals."""
        return self._report("tokens", from_utc, to_utc, interval)

    def tools(self, from_utc: Optional[datetime] = None, to_utc: Optional[datetime] = None, interval: Optional[str] = None) -> Any:
        """Tool calls, errors, and approvals per bucket, with per-tool rows."""
        return self._report("tools", from_utc, to_utc, interval)

    def models(self, from_utc: Optional[datetime] = None, to_utc: Optional[datetime] = None, interval: Optional[str] = None) -> Any:
        """Turns, tokens, and latency per model endpoint, with per-bucket totals."""
        return self._report("models", from_utc, to_utc, interval)


class ConversationsApi(_Crud):
    """Conversations and chat. ``send_message`` returns an AgentTurnResult; ``stream_message`` yields ChatEvent objects."""

    _segment = "conversations"
    _model = m.Conversation
    _tenant = False

    def list(self, agent_profile_id: Optional[str] = None, source: Optional[str] = None, **kwargs: Any) -> Any:  # type: ignore[override]
        """List the caller's conversations (tenant administrators see all). Filters: agent_profile_id, source (Dashboard,
        A2A, OpenAI, Mcp, Rest), search. Returns an EnumerationResult of Conversation."""
        return super().list(agentProfileId=agent_profile_id, source=source, **kwargs)

    def create(self, body: Optional[Body] = None) -> Any:  # type: ignore[override]
        """Start a conversation: {"agentProfileId", "title"}; the default profile is used when agentProfileId is omitted."""
        return self._run(Call("POST", self._base(), body=body if body is not None else {}, parse=m.Conversation.from_dict))

    def messages(self, id: str, max_results: Optional[int] = None, skip: Optional[int] = None) -> Any:
        """Messages in order (compacted ones included). Returns an EnumerationResult of ConversationMessage; the server
        returns up to 1000 when max_results is omitted."""
        return self._run(Call("GET", self._item(id) + "/messages", _enum(max_results, skip), parse=_page(m.ConversationMessage)))

    def send_message(self, id: str, message: str) -> Any:
        """Send a user message and run a turn; returns the AgentTurnResult. Needs Execute on the agent profile; a turn
        already running answers 409 TurnInProgress."""
        return self._run(Call("POST", self._item(id) + "/messages", body={"message": message}, parse=m.AgentTurnResult.from_dict))

    def stream_message(self, id: str, message: str) -> Any:
        """Send a user message and stream the turn as ChatEvent objects (turn.started, assistant.delta, assistant.thinking,
        tool.proposed, tool.started, tool.completed, approval.required, approval.resolved, turn.completed, error, and a
        final stream.closed carrying conversationId, turnId, finishReason, and title)."""
        source = self._t.stream(Call("POST", self._item(id) + "/messages", body={"message": message}))
        if self._is_async:
            return _achat(source)
        return (ChatEvent.from_sse(e) for e in source)

    def cancel(self, id: str) -> Any:
        """Cancel the turn running in the conversation. Returns ConversationCancelResponse (cancelled False when idle)."""
        return self._run(Call("POST", self._item(id) + "/cancel", parse=m.ConversationCancelResponse.from_dict))

    def export(self, id: str, format: str = "md") -> Any:
        """Export as Markdown (md) or JSON (json)."""
        return self._run(Call("GET", self._item(id) + "/export", {"format": format}, raw_text=True, accept="*/*"))


async def _achat(source: Any) -> Any:
    async for event in source:
        yield ChatEvent.from_sse(event)
