"""Typed response models. Every model is a dataclass that ignores unknown fields, so newer servers stay compatible.

Field names are snake_case versions of the server's camelCase JSON. Each instance keeps the original JSON in ``raw``.
Enumerations are plain strings so values added by newer servers never fail to parse.
"""

from __future__ import annotations

import dataclasses
import re
import sys
from datetime import datetime, timezone
from typing import Any, Dict, Generic, List, Optional, Type, TypeVar, Union, get_type_hints

if sys.version_info >= (3, 8):
    from typing import get_args, get_origin
else:  # pragma: no cover
    from typing_extensions import get_args, get_origin

T = TypeVar("T", bound="Model")

_FRACTION = re.compile(r"\.(\d+)")


def parse_datetime(value: Any) -> Optional[datetime]:
    """Parse an ISO 8601 timestamp (with Z or an offset, any fraction length). Returns None for empty values."""
    if value is None or value == "":
        return None
    if isinstance(value, datetime):
        return value
    text = str(value).strip()
    if text.endswith("Z") or text.endswith("z"):
        text = text[:-1] + "+00:00"
    text = _FRACTION.sub(lambda m: "." + (m.group(1) + "000000")[:6], text, count=1)
    try:
        parsed = datetime.fromisoformat(text)
    except ValueError:
        return None
    if parsed.tzinfo is None:
        parsed = parsed.replace(tzinfo=timezone.utc)
    return parsed


def format_datetime(value: datetime) -> str:
    """Format a datetime as ISO 8601 UTC with a Z suffix."""
    if value.tzinfo is None:
        value = value.replace(tzinfo=timezone.utc)
    return value.astimezone(timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.%f")[:-3] + "Z"


def _key(name: str) -> str:
    return name.replace("_", "").lower()


_HINTS: Dict[type, Dict[str, Any]] = {}


def _hints(cls: type) -> Dict[str, Any]:
    cached = _HINTS.get(cls)
    if cached is None:
        cached = get_type_hints(cls, globalns=globals())
        _HINTS[cls] = cached
    return cached


def _convert(tp: Any, value: Any) -> Any:
    if value is None:
        return None
    origin = get_origin(tp)
    if origin is Union:
        args = [a for a in get_args(tp) if a is not type(None)]
        return _convert(args[0], value) if len(args) == 1 else value
    if origin in (list, List):
        (item,) = get_args(tp) or (Any,)
        return [_convert(item, v) for v in value] if isinstance(value, list) else value
    if origin in (dict, Dict):
        args = get_args(tp)
        item = args[1] if len(args) == 2 else Any
        return {k: _convert(item, v) for k, v in value.items()} if isinstance(value, dict) else value
    if isinstance(tp, type) and issubclass(tp, Model):
        return tp.from_dict(value) if isinstance(value, dict) else value
    if tp is datetime:
        return parse_datetime(value)
    if tp is float and isinstance(value, (int, float)):
        return float(value)
    return value


@dataclasses.dataclass
class Model:
    """Base model. ``from_dict`` maps camelCase keys to fields case-insensitively and ignores unknown keys."""

    raw: Dict[str, Any] = dataclasses.field(default_factory=dict, repr=False, compare=False)

    @classmethod
    def from_dict(cls: Type[T], data: Optional[Dict[str, Any]]) -> T:
        if data is None:
            data = {}
        hints = _hints(cls)
        lookup = {_key(f.metadata.get("json") or f.name): f.name for f in dataclasses.fields(cls) if f.name != "raw"}
        kwargs: Dict[str, Any] = {}
        for key, value in data.items():
            name = lookup.get(_key(key))
            if name is None:
                continue
            kwargs[name] = _convert(hints.get(name, Any), value)
        instance = cls(**kwargs)
        instance.raw = dict(data)
        return instance

    def to_dict(self) -> Dict[str, Any]:
        """Serialize to the server's camelCase JSON, omitting None values and ``raw``."""
        out: Dict[str, Any] = {}
        for f in dataclasses.fields(self):
            if f.name == "raw":
                continue
            value = getattr(self, f.name)
            if value is None:
                continue
            out[f.metadata.get("json", _camel(f.name))] = _plain(value)
        return out

    def __getitem__(self, key: str) -> Any:
        """Dictionary-style access to the original JSON (camelCase keys)."""
        return self.raw[key]

    def get(self, key: str, default: Any = None) -> Any:
        """Dictionary-style access to the original JSON (camelCase keys)."""
        return self.raw.get(key, default)


def _camel(name: str) -> str:
    parts = name.split("_")
    return parts[0] + "".join(p[:1].upper() + p[1:] for p in parts[1:])


def _plain(value: Any) -> Any:
    if isinstance(value, Model):
        return value.to_dict()
    if isinstance(value, datetime):
        return format_datetime(value)
    if isinstance(value, list):
        return [_plain(v) for v in value]
    if isinstance(value, dict):
        return {k: _plain(v) for k, v in value.items()}
    return value


def _f(json_name: Optional[str] = None, default: Any = None) -> Any:
    if isinstance(default, (list, dict)):
        factory = list if isinstance(default, list) else dict
        return dataclasses.field(default_factory=factory, metadata={"json": json_name} if json_name else {})
    return dataclasses.field(default=default, metadata={"json": json_name} if json_name else {})


M = TypeVar("M")


@dataclasses.dataclass
class EnumerationResult(Generic[M]):
    """A page from an enumeration route (maxResults, skip, objects)."""

    success: bool = True
    max_results: int = 0
    skip: int = 0
    total_records: int = 0
    records_remaining: int = 0
    end_of_results: bool = True
    timestamp_utc: Optional[datetime] = None
    objects: List[Any] = dataclasses.field(default_factory=list)
    raw: Dict[str, Any] = dataclasses.field(default_factory=dict, repr=False)

    @classmethod
    def parse(cls, data: Optional[Dict[str, Any]], item: Optional[type]) -> "EnumerationResult[Any]":
        data = data or {}
        objects = data.get("objects") or []
        return cls(
            success=bool(data.get("success", True)),
            max_results=int(data.get("maxResults") or 0),
            skip=int(data.get("skip") or 0),
            total_records=int(data.get("totalRecords") or 0),
            records_remaining=int(data.get("recordsRemaining") or 0),
            end_of_results=bool(data.get("endOfResults", True)),
            timestamp_utc=parse_datetime(data.get("timestampUtc")),
            objects=[item.from_dict(o) if item and isinstance(o, dict) else o for o in objects],
            raw=data,
        )


@dataclasses.dataclass
class PagedResult(Generic[M]):
    """A page from the audit and request history routes (pageNumber, pageSize, totalCount, totalPages, objects)."""

    page_number: int = 1
    page_size: int = 0
    total_count: int = 0
    total_pages: int = 0
    objects: List[Any] = dataclasses.field(default_factory=list)
    raw: Dict[str, Any] = dataclasses.field(default_factory=dict, repr=False)

    @classmethod
    def parse(cls, data: Optional[Dict[str, Any]], item: Optional[type]) -> "PagedResult[Any]":
        data = data or {}
        return cls(
            page_number=int(data.get("pageNumber") or 1),
            page_size=int(data.get("pageSize") or 0),
            total_count=int(data.get("totalCount") or 0),
            total_pages=int(data.get("totalPages") or 0),
            objects=[item.from_dict(o) if item and isinstance(o, dict) else o for o in (data.get("objects") or [])],
            raw=data,
        )


# ---------------------------------------------------------------------------------------------------------------------
# Health, info, tokens
# ---------------------------------------------------------------------------------------------------------------------


@dataclasses.dataclass
class HealthResponse(Model):
    status: Optional[str] = None
    node: Optional[str] = None
    version: Optional[str] = None
    database: Optional[str] = None
    schema_version: Optional[int] = None
    expected_schema_version: Optional[int] = None
    background: Optional[str] = None
    utc: Optional[datetime] = None


@dataclasses.dataclass
class ServerInfo(Model):
    product: Optional[str] = None
    version: Optional[str] = None
    node_id: Optional[str] = None
    rest_url: Optional[str] = None
    mcp_url: Optional[str] = None
    a2a_url: Optional[str] = None
    open_api_url: Optional[str] = None
    database_type: Optional[str] = None
    telemetry_enabled: Optional[bool] = None
    request_history_enabled: Optional[bool] = None
    route_count: Optional[int] = None
    repository_url: Optional[str] = None
    started_utc: Optional[datetime] = None
    features: List[str] = _f(default=[])
    observability: Optional[Dict[str, Any]] = None


@dataclasses.dataclass
class TokenResponse(Model):
    token: Optional[str] = None
    expires_utc: Optional[datetime] = None
    tenant_id: Optional[str] = None
    session_id: Optional[str] = None
    principal_type: Optional[str] = None
    must_change_password: bool = False


@dataclasses.dataclass
class TokenValidResponse(Model):
    valid: bool = False


@dataclasses.dataclass
class TokenDetails(Model):
    principal_type: Optional[str] = None
    principal_id: Optional[str] = None
    principal_name: Optional[str] = None
    tenant_id: Optional[str] = None
    user_id: Optional[str] = None
    credential_id: Optional[str] = None
    must_change_password: bool = False
    reauthenticated_utc: Optional[datetime] = None
    session_id: Optional[str] = None
    auth_scheme: Optional[str] = None
    is_admin: bool = False
    is_tenant_admin: bool = False
    email: Optional[str] = None
    first_name: Optional[str] = None
    last_name: Optional[str] = None
    expires_utc: Optional[datetime] = None


@dataclasses.dataclass
class ReauthenticateResponse(Model):
    reauthenticated_utc: Optional[datetime] = None
    reveal_allowed_until_utc: Optional[datetime] = None


@dataclasses.dataclass
class TenantLookupEntry(Model):
    tenant_id: Optional[str] = None
    name: Optional[str] = None


# ---------------------------------------------------------------------------------------------------------------------
# Identity and RBAC
# ---------------------------------------------------------------------------------------------------------------------


@dataclasses.dataclass
class Tenant(Model):
    id: Optional[str] = None
    name: Optional[str] = None
    description: Optional[str] = None
    active: bool = True
    is_protected: bool = False
    parent_tenant_id: Optional[str] = None
    region: Optional[str] = None
    default_agent_profile_id: Optional[str] = None
    lockdown: bool = False
    created_utc: Optional[datetime] = None
    last_update_utc: Optional[datetime] = None


@dataclasses.dataclass
class User(Model):
    id: Optional[str] = None
    tenant_id: Optional[str] = None
    first_name: Optional[str] = None
    last_name: Optional[str] = None
    email: Optional[str] = None
    is_admin: bool = False
    is_tenant_admin: bool = False
    must_change_password: bool = False
    default_agent_profile_id: Optional[str] = None
    preferred_locale: Optional[str] = None
    active: bool = True
    is_protected: bool = False
    created_utc: Optional[datetime] = None
    last_update_utc: Optional[datetime] = None


@dataclasses.dataclass
class Credential(Model):
    id: Optional[str] = None
    tenant_id: Optional[str] = None
    user_id: Optional[str] = None
    name: Optional[str] = None
    access_key: Optional[str] = None
    secret_key_last4: Optional[str] = None
    auth_mode: Optional[str] = None
    agent_profile_id: Optional[str] = None
    lock_to_profile: bool = False
    inherit_owner_bypass: bool = False
    """When true the credential carries its owner's administrator or tenant administrator bypass. Off by default:
    a credential gets access only from its role assignments. Only signed-in tenant or system administrators can
    change it (403 BypassRequiresAdministrator otherwise); changes are audited as CredentialBypassChanged."""
    last_used_utc: Optional[datetime] = None
    expires_utc: Optional[datetime] = None
    active: bool = True
    is_protected: bool = False
    created_utc: Optional[datetime] = None
    last_update_utc: Optional[datetime] = None


@dataclasses.dataclass
class CredentialCreateResponse(Model):
    """A created or rotated credential. ``secret_key`` is returned only once."""

    credential: Optional[Credential] = None
    access_key: Optional[str] = None
    secret_key: Optional[str] = None


@dataclasses.dataclass
class PermissionGrant(Model):
    source: Optional[str] = None
    permission_type: Optional[str] = None
    resource_types: List[Any] = _f(default=[])
    operations: List[Any] = _f(default=[])
    scope: Optional[str] = None
    resource_id: Optional[str] = None
    inherits_to_children: bool = False


@dataclasses.dataclass
class PermissionCheck(Model):
    resource_type: Optional[str] = None
    operation: Optional[str] = None
    permitted: bool = False


@dataclasses.dataclass
class EffectivePermissions(Model):
    principal_type: Optional[str] = None
    principal_id: Optional[str] = None
    tenant_id: Optional[str] = None
    is_admin: bool = False
    is_tenant_admin: bool = False
    grants: List[PermissionGrant] = _f(default=[])
    has_permission: List[PermissionCheck] = _f(default=[])


@dataclasses.dataclass
class Role(Model):
    id: Optional[str] = None
    tenant_id: Optional[str] = None
    name: Optional[str] = None
    description: Optional[str] = None
    is_built_in: bool = False
    active: bool = True
    is_protected: bool = False
    created_utc: Optional[datetime] = None
    last_update_utc: Optional[datetime] = None


@dataclasses.dataclass
class Permission(Model):
    id: Optional[str] = None
    tenant_id: Optional[str] = None
    name: Optional[str] = None
    permission_type: Optional[str] = None
    is_built_in: bool = False
    active: bool = True
    is_protected: bool = False
    created_utc: Optional[datetime] = None
    last_update_utc: Optional[datetime] = None
    resource_types: List[str] = _f(default=[])
    operation_types: List[str] = _f(default=[])


@dataclasses.dataclass
class RolePermissionMap(Model):
    id: Optional[str] = None
    tenant_id: Optional[str] = None
    role_id: Optional[str] = None
    permission_id: Optional[str] = None
    active: bool = True
    is_protected: bool = False
    created_utc: Optional[datetime] = None
    last_update_utc: Optional[datetime] = None


@dataclasses.dataclass
class UserRoleAssignment(Model):
    id: Optional[str] = None
    tenant_id: Optional[str] = None
    user_id: Optional[str] = None
    role_id: Optional[str] = None
    role_name: Optional[str] = None
    resource_scope: Optional[str] = None
    resource_id: Optional[str] = None
    inherits_to_children: bool = False
    active: bool = True
    is_protected: bool = False
    created_utc: Optional[datetime] = None
    last_update_utc: Optional[datetime] = None


@dataclasses.dataclass
class CredentialScopeAssignment(Model):
    id: Optional[str] = None
    tenant_id: Optional[str] = None
    credential_id: Optional[str] = None
    role_id: Optional[str] = None
    role_name: Optional[str] = None
    resource_scope: Optional[str] = None
    resource_id: Optional[str] = None
    inherits_to_children: bool = False
    active: bool = True
    is_protected: bool = False
    created_utc: Optional[datetime] = None
    last_update_utc: Optional[datetime] = None


@dataclasses.dataclass
class AuthSession(Model):
    id: Optional[str] = None
    tenant_id: Optional[str] = None
    user_id: Optional[str] = None
    credential_id: Optional[str] = None
    principal_type: Optional[str] = None
    principal_name: Optional[str] = None
    auth_scheme: Optional[str] = None
    source_ip: Optional[str] = None
    user_agent: Optional[str] = None
    expires_utc: Optional[datetime] = None
    last_used_utc: Optional[datetime] = None
    revoked_utc: Optional[datetime] = None
    revocation_reason: Optional[str] = None
    reauthenticated_utc: Optional[datetime] = None
    active: bool = True
    is_protected: bool = False
    created_utc: Optional[datetime] = None
    last_update_utc: Optional[datetime] = None


@dataclasses.dataclass
class AuditRecord(Model):
    id: Optional[str] = None
    tenant_id: Optional[str] = None
    event_type: Optional[str] = None
    request_id: Optional[str] = None
    correlation_id: Optional[str] = None
    trace_id: Optional[str] = None
    auth_scheme: Optional[str] = None
    principal_type: Optional[str] = None
    principal_id: Optional[str] = None
    principal_name: Optional[str] = None
    session_id: Optional[str] = None
    resource_type: Optional[str] = None
    resource_id: Optional[str] = None
    operation: Optional[str] = None
    request_type: Optional[str] = None
    method: Optional[str] = None
    path: Optional[str] = None
    source_ip: Optional[str] = None
    auth_result: Optional[str] = None
    authorization_result: Optional[str] = None
    reason: Optional[str] = None
    bypass_reason: Optional[str] = None
    status_code: Optional[int] = None
    description: Optional[str] = None
    active: bool = True
    is_protected: bool = False
    created_utc: Optional[datetime] = None
    last_update_utc: Optional[datetime] = None


@dataclasses.dataclass
class DeleteCountResponse(Model):
    deleted_count: int = 0


# ---------------------------------------------------------------------------------------------------------------------
# Request history
# ---------------------------------------------------------------------------------------------------------------------


@dataclasses.dataclass
class RequestHistoryHeader(Model):
    id: Optional[str] = None
    request_history_entry_id: Optional[str] = None
    direction: Optional[str] = None
    name: Optional[str] = None
    value: Optional[str] = None


@dataclasses.dataclass
class RequestHistoryEntry(Model):
    id: Optional[str] = None
    tenant_id: Optional[str] = None
    user_id: Optional[str] = None
    principal_name: Optional[str] = None
    method: Optional[str] = None
    path: Optional[str] = None
    url: Optional[str] = None
    status_code: Optional[int] = None
    duration_ms: Optional[float] = None
    source_ip: Optional[str] = None
    trace_id: Optional[str] = None
    correlation_id: Optional[str] = None
    request_body: Optional[str] = None
    request_body_bytes: Optional[int] = None
    request_body_truncated: bool = False
    response_body: Optional[str] = None
    response_body_bytes: Optional[int] = None
    response_body_truncated: bool = False
    created_utc: Optional[datetime] = None
    completed_utc: Optional[datetime] = None
    headers: List[RequestHistoryHeader] = _f(default=[])


@dataclasses.dataclass
class RequestHistoryBucket(Model):
    bucket_start_utc: Optional[datetime] = None
    bucket_end_utc: Optional[datetime] = None
    success_count: int = 0
    failure_count: int = 0
    average_duration_ms: Optional[float] = None


@dataclasses.dataclass
class RequestHistorySummary(Model):
    total_count: int = 0
    total_success: int = 0
    total_failure: int = 0
    average_duration_ms: Optional[float] = None
    buckets: List[RequestHistoryBucket] = _f(default=[])


# ---------------------------------------------------------------------------------------------------------------------
# Secrets
# ---------------------------------------------------------------------------------------------------------------------


@dataclasses.dataclass
class SecretReferrer(Model):
    referrer_type: Optional[str] = None
    referrer_id: Optional[str] = None
    referrer_name: Optional[str] = None
    detail: Optional[str] = None


SECRET_KINDS = ("Password", "PrivateKey", "ApiToken", "AccessKeyPair", "ServiceAccountJson", "Kubeconfig",
                "ConnectionString", "Certificate", "Generic", "BearerToken")
"""Secret kinds the server accepts. ``BearerToken`` (field ``token``) is for tokens sent exactly as
``Authorization: Bearer``; ``ApiToken`` (field ``token``) is for tokens whose header or format depends on the service."""


@dataclasses.dataclass
class Secret(Model):
    """Secret metadata. Values are never returned except by ``secrets.reveal``."""

    id: Optional[str] = None
    tenant_id: Optional[str] = None
    owner_user_id: Optional[str] = None
    name: Optional[str] = None
    kind: Optional[str] = None
    description: Optional[str] = None
    key_version: Optional[int] = None
    fingerprint: Optional[str] = None
    last_rotated_utc: Optional[datetime] = None
    last_revealed_utc: Optional[datetime] = None
    expires_utc: Optional[datetime] = None
    active: bool = True
    is_protected: bool = False
    created_utc: Optional[datetime] = None
    last_update_utc: Optional[datetime] = None
    fields: List[str] = _f(default=[])
    referrers: List[SecretReferrer] = _f(default=[])


@dataclasses.dataclass
class SecretRevealResponse(Model):
    secret_id: Optional[str] = None
    name: Optional[str] = None
    kind: Optional[str] = None
    values: Dict[str, str] = _f(default={})
    revealed_utc: Optional[datetime] = None


@dataclasses.dataclass
class KubeconfigContext(Model):
    name: Optional[str] = None
    cluster: Optional[str] = None
    server: Optional[str] = None
    user: Optional[str] = None
    namespace: Optional[str] = None


@dataclasses.dataclass
class SecretImportResult(Model):
    kind: Optional[str] = None
    format: Optional[str] = None
    suggested_field: Optional[str] = None
    fields: List[str] = _f(default=[])
    fingerprint: Optional[str] = None
    key_type: Optional[str] = None
    key_size_bits: Optional[int] = None
    encrypted: bool = False
    comment: Optional[str] = None
    project_id: Optional[str] = None
    client_email: Optional[str] = None
    private_key_id: Optional[str] = None
    contexts: List[KubeconfigContext] = _f(default=[])
    current_context: Optional[str] = None
    subject: Optional[str] = None
    issuer: Optional[str] = None
    not_before_utc: Optional[datetime] = None
    not_after_utc: Optional[datetime] = None
    certificate_count: Optional[int] = None
    has_private_key: bool = False
    warnings: List[str] = _f(default=[])


# ---------------------------------------------------------------------------------------------------------------------
# Prompt templates
# ---------------------------------------------------------------------------------------------------------------------


@dataclasses.dataclass
class PromptTemplateVariable(Model):
    id: Optional[str] = None
    prompt_template_id: Optional[str] = None
    name: Optional[str] = None
    description: Optional[str] = None
    required: bool = False
    default_value: Optional[str] = None


@dataclasses.dataclass
class PromptTemplate(Model):
    id: Optional[str] = None
    tenant_id: Optional[str] = None
    name: Optional[str] = None
    description: Optional[str] = None
    kind: Optional[str] = None
    content: Optional[str] = None
    current_version: Optional[int] = None
    is_built_in: bool = False
    built_in_key: Optional[str] = None
    active: bool = True
    is_protected: bool = False
    created_utc: Optional[datetime] = None
    last_update_utc: Optional[datetime] = None
    variables: List[PromptTemplateVariable] = _f(default=[])


@dataclasses.dataclass
class PromptTemplateVersion(Model):
    id: Optional[str] = None
    tenant_id: Optional[str] = None
    template_id: Optional[str] = None
    version: Optional[int] = None
    content: Optional[str] = None
    change_note: Optional[str] = None
    created_by: Optional[str] = None
    active: bool = True
    is_protected: bool = False
    created_utc: Optional[datetime] = None
    last_update_utc: Optional[datetime] = None


@dataclasses.dataclass
class PromptRenderResponse(Model):
    rendered: Optional[str] = None
    success: bool = False
    variables: List[str] = _f(default=[])
    includes: List[str] = _f(default=[])
    errors: List[str] = _f(default=[])


@dataclasses.dataclass
class PromptLayerState(Model):
    """One layer (Global or Tenant) of a layered prompt template key."""

    layer: Optional[str] = None
    exists: bool = False
    template_id: Optional[str] = None
    content: Optional[str] = None
    version: Optional[int] = None
    customized: bool = False
    default_updated_since_customized: bool = False
    last_update_utc: Optional[datetime] = None


@dataclasses.dataclass
class PromptTemplateView(Model):
    """A layered template key (/v1.0/api/prompt-templates): Built-in, Global, and Tenant layers and the one in effect."""

    key: Optional[str] = None
    description: Optional[str] = None
    source: Optional[str] = None
    active_layer: Optional[str] = None
    effective_content: Optional[str] = None
    built_in_content: Optional[str] = None
    built_in_hash: Optional[str] = None
    global_: Optional[PromptLayerState] = _f("global", None)
    tenant: Optional[PromptLayerState] = None


@dataclasses.dataclass
class BuiltInTemplate(Model):
    key: Optional[str] = None
    content: Optional[str] = None
    hash: Optional[str] = None
    source: Optional[str] = None
    description: Optional[str] = None


@dataclasses.dataclass
class PromptDiffLine(Model):
    """One diff line; ``op`` is add, remove, or same."""

    op: Optional[str] = None
    text: Optional[str] = None


# ---------------------------------------------------------------------------------------------------------------------
# Settings and system
# ---------------------------------------------------------------------------------------------------------------------


@dataclasses.dataclass
class SettingField(Model):
    path: Optional[str] = None
    group: Optional[str] = None
    description: Optional[str] = None
    type: Optional[str] = None
    value: Optional[str] = None
    is_secret: bool = False
    is_set: bool = False
    source: Optional[str] = None
    apply_mode: Optional[str] = None
    editable: bool = False
    minimum: Optional[int] = None
    maximum: Optional[int] = None
    allowed_values: List[Any] = _f(default=[])


@dataclasses.dataclass
class SettingsView(Model):
    settings_file: Optional[str] = None
    settings_file_writable: bool = False
    restart_pending: bool = False
    fields: List[SettingField] = _f(default=[])


@dataclasses.dataclass
class SettingsSaveResponse(Model):
    saved: bool = False
    restart_required: bool = False
    applied_live: List[str] = _f(default=[])
    pending_restart: List[str] = _f(default=[])
    settings: Optional[SettingsView] = None


@dataclasses.dataclass
class RestartResponse(Model):
    restarting: bool = False
    node: Optional[str] = None


@dataclasses.dataclass
class DatabaseTestResponse(Model):
    ok: bool = False
    provider: Optional[str] = None
    message: Optional[str] = None


@dataclasses.dataclass
class MasterKeyRotationStatus(Model):
    running: bool = False
    target_key_version: Optional[int] = None
    total: int = 0
    completed: int = 0
    failed: int = 0
    remaining: int = 0
    started_utc: Optional[datetime] = None
    completed_utc: Optional[datetime] = None
    last_error: Optional[str] = None


@dataclasses.dataclass
class ObservabilityServiceStatus(Model):
    service: Optional[str] = None
    configured: bool = False
    reachable: bool = False
    checked_utc: Optional[datetime] = None


# ---------------------------------------------------------------------------------------------------------------------
# Domain
# ---------------------------------------------------------------------------------------------------------------------


@dataclasses.dataclass
class SettingDescriptor(Model):
    key: Optional[str] = None
    label: Optional[str] = None
    help: Optional[str] = None
    kind: Optional[str] = None
    required: bool = False
    default_value: Optional[str] = None
    minimum: Optional[int] = None
    maximum: Optional[int] = None
    allowed_values: List[str] = _f(default=[])
    pattern: Optional[str] = None
    group: Optional[str] = None
    show_in_summary: bool = False


@dataclasses.dataclass
class SecretSlotDescriptor(Model):
    name: Optional[str] = None
    label: Optional[str] = None
    help: Optional[str] = None
    accepted_kinds: List[str] = _f(default=[])
    required: bool = False


@dataclasses.dataclass
class ConnectorToolView(Model):
    name: Optional[str] = None
    title: Optional[str] = None
    description: Optional[str] = None
    input_schema_json: Optional[str] = None
    base_effect: Optional[str] = None
    classified_per_call: bool = False
    updates_knowledge: bool = False
    requires_model: bool = False
    output_description: Optional[str] = None


@dataclasses.dataclass
class ConnectorView(Model):
    type: Optional[str] = None
    family: Optional[str] = None
    display_name: Optional[str] = None
    description: Optional[str] = None
    category: Optional[str] = None
    icon_key: Optional[str] = None
    version: Optional[str] = None
    documentation_url: Optional[str] = None
    default_instructions_key: Optional[str] = None
    plugin: bool = False
    settings: List[SettingDescriptor] = _f(default=[])
    secret_slots: List[SecretSlotDescriptor] = _f(default=[])
    tools: List[ConnectorToolView] = _f(default=[])


@dataclasses.dataclass
class ResourceSetting(Model):
    id: Optional[str] = None
    resource_id: Optional[str] = None
    tenant_id: Optional[str] = None
    key: Optional[str] = None
    value_kind: Optional[str] = None
    value: Optional[str] = None


@dataclasses.dataclass
class ResourceSecretBinding(Model):
    id: Optional[str] = None
    resource_id: Optional[str] = None
    tenant_id: Optional[str] = None
    slot_name: Optional[str] = None
    secret_id: Optional[str] = None
    secret_field_name: Optional[str] = None
    secret_name: Optional[str] = None


@dataclasses.dataclass
class Resource(Model):
    id: Optional[str] = None
    tenant_id: Optional[str] = None
    owner_user_id: Optional[str] = None
    visibility: Optional[str] = None
    name: Optional[str] = None
    connector_type: Optional[str] = None
    display_name: Optional[str] = None
    description: Optional[str] = None
    endpoint: Optional[str] = None
    environment: Optional[str] = None
    notes: Optional[str] = None
    default_access_mode_ceiling: Optional[str] = None
    last_test_utc: Optional[datetime] = None
    last_test_result: Optional[bool] = None
    last_test_message: Optional[str] = None
    active: bool = True
    created_utc: Optional[datetime] = None
    last_update_utc: Optional[datetime] = None
    settings: List[ResourceSetting] = _f(default=[])
    secret_bindings: List[ResourceSecretBinding] = _f(default=[])


@dataclasses.dataclass
class ConnectorValidationError(Model):
    field: Optional[str] = None
    code: Optional[str] = None
    message: Optional[str] = None


@dataclasses.dataclass
class ResourceTestResponse(Model):
    """Result of a resource connection test. ``learned_settings`` holds values the connector discovered."""

    success: bool = False
    message: Optional[str] = None
    error_code: Optional[str] = None
    latency_ms: Optional[float] = None
    details: Dict[str, Any] = _f(default={})
    validation_errors: List[ConnectorValidationError] = _f(default=[])
    learned_settings: Dict[str, Any] = _f(default={})


ResourceTestResult = ResourceTestResponse


@dataclasses.dataclass
class ResourceReferrer(Model):
    agent_profile_id: Optional[str] = None
    agent_profile_name: Optional[str] = None
    binding_id: Optional[str] = None
    alias: Optional[str] = None
    access_mode: Optional[str] = None
    enabled: bool = True


@dataclasses.dataclass
class ResourceKnowledge(Model):
    id: Optional[str] = None
    tenant_id: Optional[str] = None
    resource_id: Optional[str] = None
    scope: Optional[str] = None
    key: Optional[str] = None
    content: Optional[str] = None
    source: Optional[str] = None
    updated_by: Optional[str] = None
    version: Optional[int] = None
    created_utc: Optional[datetime] = None
    last_update_utc: Optional[datetime] = None


@dataclasses.dataclass
class ResourceKnowledgeVersion(Model):
    id: Optional[str] = None
    tenant_id: Optional[str] = None
    knowledge_id: Optional[str] = None
    resource_id: Optional[str] = None
    scope: Optional[str] = None
    key: Optional[str] = None
    version: Optional[int] = None
    content: Optional[str] = None
    source: Optional[str] = None
    updated_by: Optional[str] = None
    deleted: bool = False
    created_utc: Optional[datetime] = None


@dataclasses.dataclass
class AgentProfile(Model):
    id: Optional[str] = None
    tenant_id: Optional[str] = None
    owner_user_id: Optional[str] = None
    visibility: Optional[str] = None
    name: Optional[str] = None
    display_name: Optional[str] = None
    persona_name: Optional[str] = None
    user_address: Optional[str] = None
    description: Optional[str] = None
    persona_template_key: Optional[str] = None
    inference_endpoint_id: Optional[str] = None
    fallback_inference_endpoint_id: Optional[str] = None
    embedding_endpoint_id: Optional[str] = None
    max_iterations: Optional[int] = None
    max_tool_calls_per_turn: Optional[int] = None
    tool_timeout_seconds: Optional[int] = None
    temperature: Optional[float] = None
    expose_a2a: bool = _f("exposeA2A", False)
    expose_mcp: bool = False
    expose_openai: bool = _f("exposeOpenAI", False)
    expose_management_tools: bool = False
    instructions_append: Optional[str] = None
    instruction_detail: Optional[str] = None
    is_default: bool = False
    binding_count: int = 0
    active: bool = True
    created_utc: Optional[datetime] = None
    last_update_utc: Optional[datetime] = None


@dataclasses.dataclass
class BindingToolRule(Model):
    id: Optional[str] = None
    binding_id: Optional[str] = None
    tenant_id: Optional[str] = None
    tool_name: Optional[str] = None
    rule: Optional[str] = None


@dataclasses.dataclass
class Binding(Model):
    id: Optional[str] = None
    tenant_id: Optional[str] = None
    agent_profile_id: Optional[str] = None
    resource_id: Optional[str] = None
    alias: Optional[str] = None
    access_mode: Optional[str] = None
    require_approval_for_destructive: bool = True
    instruction_mode: Optional[str] = None
    instruction_text: Optional[str] = None
    max_output_bytes: Optional[int] = None
    max_rows: Optional[int] = None
    enabled: bool = True
    allow_knowledge_updates: bool = False
    tool_rules: List[BindingToolRule] = _f(default=[])
    deny_patterns: List[str] = _f(default=[])
    resource_name: Optional[str] = None
    connector_type: Optional[str] = None
    created_utc: Optional[datetime] = None
    last_update_utc: Optional[datetime] = None


@dataclasses.dataclass
class InstructionSegment(Model):
    key: Optional[str] = None
    layer: Optional[str] = None
    version: Optional[int] = None
    binding_alias: Optional[str] = None
    start: int = 0
    length: int = 0
    depth: int = 0


@dataclasses.dataclass
class CompiledInstructions(Model):
    profile_id: Optional[str] = None
    profile_name: Optional[str] = None
    surface: Optional[str] = None
    text: Optional[str] = None
    token_estimate: Optional[int] = None
    binding_count: Optional[int] = None
    segments: List[InstructionSegment] = _f(default=[])
    warnings: List[str] = _f(default=[])
    from_cache: bool = False
    compiled_utc: Optional[datetime] = None


@dataclasses.dataclass
class CallerToolResource(Model):
    alias: Optional[str] = None
    connector_type: Optional[str] = None
    access_mode: Optional[str] = None
    rule: Optional[str] = None


@dataclasses.dataclass
class CallerTool(Model):
    name: Optional[str] = None
    family: Optional[str] = None
    title: Optional[str] = None
    description: Optional[str] = None
    input_schema_json: Optional[str] = None
    base_effect: Optional[str] = None
    classified_per_call: bool = False
    updates_knowledge: bool = False
    requires_model: bool = False
    known_effect: Optional[str] = None
    is_meta_tool: bool = False
    resources: List[CallerToolResource] = _f(default=[])


@dataclasses.dataclass
class CallerToolCatalog(Model):
    profile_id: Optional[str] = None
    profile_name: Optional[str] = None
    has_model: bool = False
    resource_count: int = 0
    tools: List[CallerTool] = _f(default=[])
    meta_tools: List[CallerTool] = _f(default=[])


@dataclasses.dataclass
class AgentSkill(Model):
    id: Optional[str] = None
    name: Optional[str] = None
    description: Optional[str] = None
    tags: List[str] = _f(default=[])
    examples: List[str] = _f(default=[])


@dataclasses.dataclass
class AgentCardPreview(Model):
    name: Optional[str] = None
    description: Optional[str] = None
    url: Optional[str] = None
    version: Optional[str] = None
    skills: List[AgentSkill] = _f(default=[])


@dataclasses.dataclass
class ModelEndpoint(Model):
    id: Optional[str] = None
    tenant_id: Optional[str] = None
    name: Optional[str] = None
    kind: Optional[str] = None
    provider: Optional[str] = None
    base_url: Optional[str] = None
    model: Optional[str] = None
    api_key_secret_id: Optional[str] = None
    api_key_secret_field: Optional[str] = None
    region: Optional[str] = None
    deployment_name: Optional[str] = None
    api_version: Optional[str] = None
    context_window_tokens: Optional[int] = None
    max_output_tokens: Optional[int] = None
    supports_tools: bool = False
    supports_streaming: bool = False
    embedding_dimensions: Optional[int] = None
    max_concurrency: Optional[int] = None
    timeout_seconds: Optional[int] = None
    max_retries: Optional[int] = None
    health_status: Optional[str] = None
    last_health_utc: Optional[datetime] = None
    last_health_message: Optional[str] = None
    active: bool = True
    created_utc: Optional[datetime] = None
    last_update_utc: Optional[datetime] = None


@dataclasses.dataclass
class ModelValidationResult(Model):
    success: bool = False
    kind: Optional[str] = None
    latency_ms: Optional[int] = None
    dimensions: Optional[int] = None
    model: Optional[str] = None
    response_preview: Optional[str] = None
    status_code: Optional[int] = None
    error: Optional[str] = None
    problems: List[str] = _f(default=[])


@dataclasses.dataclass
class ToolResult(Model):
    is_error: bool = False
    error_code: Optional[str] = None
    retry_after_seconds: Optional[float] = None
    text: Optional[str] = None
    structured_json: Optional[str] = None
    mime_type: Optional[str] = None
    truncated: bool = False
    original_bytes: Optional[int] = None
    metrics: Dict[str, float] = _f(default={})


@dataclasses.dataclass
class ToolInvocationOutcome(Model):
    invocation_id: Optional[str] = None
    dry_run: bool = False
    tool_name: Optional[str] = None
    profile_id: Optional[str] = None
    binding_alias: Optional[str] = None
    resource_id: Optional[str] = None
    connector_type: Optional[str] = None
    decision: Optional[str] = None
    decision_code: Optional[str] = None
    reason: Optional[str] = None
    effect: Optional[str] = None
    effect_reason: Optional[str] = None
    access_mode: Optional[str] = None
    approval_id: Optional[str] = None
    approval_status: Optional[str] = None
    approval_summary: Optional[str] = None
    approval_expires_utc: Optional[datetime] = None
    status: Optional[str] = None
    result: Optional[ToolResult] = None
    duration_ms: Optional[int] = None
    approval_wait_ms: Optional[int] = None


@dataclasses.dataclass
class ToolInvocation(Model):
    id: Optional[str] = None
    tenant_id: Optional[str] = None
    conversation_id: Optional[str] = None
    message_id: Optional[str] = None
    surface: Optional[str] = None
    principal_type: Optional[str] = None
    principal_id: Optional[str] = None
    principal_name: Optional[str] = None
    agent_profile_id: Optional[str] = None
    binding_id: Optional[str] = None
    binding_alias: Optional[str] = None
    resource_id: Optional[str] = None
    connector_type: Optional[str] = None
    tool_name: Optional[str] = None
    arguments_redacted: Optional[str] = None
    redaction_applied: bool = False
    effect: Optional[str] = None
    effect_reason: Optional[str] = None
    access_mode: Optional[str] = None
    decision: Optional[str] = None
    decision_code: Optional[str] = None
    decision_reason: Optional[str] = None
    approval_id: Optional[str] = None
    status: Optional[str] = None
    error_code: Optional[str] = None
    error_message: Optional[str] = None
    output_bytes: Optional[int] = None
    output_truncated: bool = False
    output_preview: Optional[str] = None
    duration_ms: Optional[int] = None
    trace_id: Optional[str] = None
    correlation_id: Optional[str] = None
    created_utc: Optional[datetime] = None
    last_update_utc: Optional[datetime] = None


@dataclasses.dataclass
class StatusCount(Model):
    status: Optional[str] = None
    count: int = 0


@dataclasses.dataclass
class ToolInvocationSummary(Model):
    total: int = 0
    since_utc: Optional[datetime] = None
    by_decision: List[StatusCount] = _f(default=[])
    by_status: List[StatusCount] = _f(default=[])
    by_effect: List[StatusCount] = _f(default=[])
    by_tool: List[StatusCount] = _f(default=[])


APPROVAL_TERMINAL_STATUSES = ("Approved", "Rejected", "Expired", "Cancelled")


@dataclasses.dataclass
class Approval(Model):
    id: Optional[str] = None
    tenant_id: Optional[str] = None
    tool_invocation_id: Optional[str] = None
    agent_profile_id: Optional[str] = None
    binding_id: Optional[str] = None
    binding_alias: Optional[str] = None
    resource_id: Optional[str] = None
    tool_name: Optional[str] = None
    surface: Optional[str] = None
    requested_by_principal_type: Optional[str] = None
    requested_by_principal_id: Optional[str] = None
    requested_by_name: Optional[str] = None
    summary: Optional[str] = None
    effect: Optional[str] = None
    effect_reason: Optional[str] = None
    arguments_redacted: Optional[str] = None
    status: Optional[str] = None
    decided_by_user_id: Optional[str] = None
    decided_by_name: Optional[str] = None
    decision_note: Optional[str] = None
    decided_utc: Optional[datetime] = None
    expires_utc: Optional[datetime] = None
    invocation: Optional[ToolInvocation] = None
    created_utc: Optional[datetime] = None
    last_update_utc: Optional[datetime] = None

    @property
    def is_decided(self) -> bool:
        """True once the approval left Pending."""
        return self.status in APPROVAL_TERMINAL_STATUSES


@dataclasses.dataclass
class LockdownState(Model):
    tenant_id: Optional[str] = None
    lockdown: bool = False
    approvals_cancelled: int = 0


@dataclasses.dataclass
class Conversation(Model):
    id: Optional[str] = None
    tenant_id: Optional[str] = None
    user_id: Optional[str] = None
    credential_id: Optional[str] = None
    agent_profile_id: Optional[str] = None
    title: Optional[str] = None
    source: Optional[str] = None
    a2a_context_id: Optional[str] = _f("a2AContextId")
    external_key: Optional[str] = None
    message_count: int = 0
    last_message_utc: Optional[datetime] = None
    total_input_tokens: int = 0
    total_output_tokens: int = 0
    active: bool = True
    created_utc: Optional[datetime] = None
    last_update_utc: Optional[datetime] = None


@dataclasses.dataclass
class Message(Model):
    """A stored conversation message (GET /v1.0/api/conversations/{id}/messages). Role is User, Assistant, Tool,
    Summary, or System; ``tool_calls`` is the assistant's tool call list as sent by the server."""

    id: Optional[str] = None
    tenant_id: Optional[str] = None
    conversation_id: Optional[str] = None
    turn_id: Optional[str] = None
    sequence: Optional[int] = None
    role: Optional[str] = None
    content: Optional[str] = None
    thinking_content: Optional[str] = None
    tool_calls: List[Any] = _f(default=[])
    tool_call_id: Optional[str] = None
    tool_name: Optional[str] = None
    model_endpoint_id: Optional[str] = None
    input_tokens: Optional[int] = None
    output_tokens: Optional[int] = None
    time_to_first_token_ms: Optional[int] = None
    duration_ms: Optional[int] = None
    finish_reason: Optional[str] = None
    compacted: bool = False
    created_utc: Optional[datetime] = None


ConversationMessage = Message


@dataclasses.dataclass
class ConversationCancelResponse(Model):
    conversation_id: Optional[str] = None
    cancelled: bool = False


@dataclasses.dataclass
class AgentTurnMessage(Model):
    """A message produced during a turn (in AgentTurnResult.messages)."""

    id: Optional[str] = None
    conversation_id: Optional[str] = None
    turn_id: Optional[str] = None
    role: Optional[str] = None
    content: Optional[str] = None
    thinking: Optional[str] = None
    tool_calls: List[Any] = _f(default=[])
    tool_call_id: Optional[str] = None
    tool_name: Optional[str] = None
    tokens_in: int = 0
    tokens_out: int = 0
    created_utc: Optional[datetime] = None


@dataclasses.dataclass
class AgentToolCallRecord(Model):
    """One tool call made during a turn. ``decision`` is Allowed, Denied, ApprovalRequired, Approved, Rejected, or
    Expired."""

    call_id: Optional[str] = None
    tool_name: Optional[str] = None
    resource: Optional[str] = None
    arguments_json: Optional[str] = None
    effect: Optional[str] = None
    decision: Optional[str] = None
    approval_id: Optional[str] = None
    duration_ms: int = 0
    approval_wait_ms: int = 0
    is_error: bool = False
    error_code: Optional[str] = None
    truncated: bool = False
    output_preview: Optional[str] = None
    iteration: int = 0
    parallel: bool = False


@dataclasses.dataclass
class AgentTurnResult(Model):
    """Result of POST /v1.0/api/conversations/{id}/messages without an event stream. ``finish_reason`` is Completed,
    MaxIterations, ToolCallLimit, ApprovalPending, Cancelled, or Error."""

    turn_id: Optional[str] = None
    conversation_id: Optional[str] = None
    final_text: Optional[str] = None
    thinking: Optional[str] = None
    messages: List[AgentTurnMessage] = _f(default=[])
    tool_calls: List[AgentToolCallRecord] = _f(default=[])
    tokens_in: int = 0
    tokens_out: int = 0
    tokens_estimated: bool = False
    time_to_first_token_ms: Optional[int] = None
    duration_ms: int = 0
    iterations: int = 0
    finish_reason: Optional[str] = None
    error_code: Optional[str] = None
    error_message: Optional[str] = None
    compacted: bool = False
    tools_available: int = 0
    tools_offered: int = 0
    tool_retrieval_used: bool = False
    used_fallback: bool = False
    model: Optional[str] = None
    pending_approval_id: Optional[str] = None


@dataclasses.dataclass
class AgentDelegationResult(Model):
    """Result of POST /v1.0/api/agent-profiles/{id}/ask. ``status`` is Completed, ApprovalPending, or Failed."""

    final_text: Optional[str] = None
    conversation_id: Optional[str] = None
    status: Optional[str] = None
    approval_id: Optional[str] = None
    tool_trace: List[str] = _f(default=[])


# ---------------------------------------------------------------------------------------------------------------------
# Analytics and model endpoint health history
# ---------------------------------------------------------------------------------------------------------------------


@dataclasses.dataclass
class AnalyticsBucket(Model):
    start_utc: Optional[datetime] = None
    turns: int = 0
    input_tokens: int = 0
    output_tokens: int = 0
    tool_calls: int = 0
    tool_errors: int = 0
    approvals: int = 0
    avg_time_to_first_token_ms: float = 0.0
    avg_turn_duration_ms: float = 0.0


@dataclasses.dataclass
class AnalyticsToolRow(Model):
    tool_name: Optional[str] = None
    connector_type: Optional[str] = None
    calls: int = 0
    errors: int = 0
    denied: int = 0
    approvals: int = 0
    avg_duration_ms: float = 0.0


@dataclasses.dataclass
class AnalyticsModelRow(Model):
    model_endpoint_id: Optional[str] = None
    name: Optional[str] = None
    provider: Optional[str] = None
    model: Optional[str] = None
    turns: int = 0
    input_tokens: int = 0
    output_tokens: int = 0
    avg_time_to_first_token_ms: float = 0.0
    avg_turn_duration_ms: float = 0.0


@dataclasses.dataclass
class AnalyticsReport(Model):
    """GET /v1.0/api/analytics/{overview|tokens|tools|models}. ``interval`` is minute, hour, or day."""

    kind: Optional[str] = None
    from_utc: Optional[datetime] = None
    to_utc: Optional[datetime] = None
    interval: Optional[str] = None
    conversations: int = 0
    totals: Optional[AnalyticsBucket] = None
    approvals_requested: int = 0
    buckets: List[AnalyticsBucket] = _f(default=[])
    tools: List[AnalyticsToolRow] = _f(default=[])
    models: List[AnalyticsModelRow] = _f(default=[])


@dataclasses.dataclass
class ModelHealthCheck(Model):
    """One model endpoint health check. ``status`` is Unknown, Healthy, or Unhealthy; ``source`` is Sweep or Validate."""

    id: Optional[str] = None
    tenant_id: Optional[str] = None
    model_endpoint_id: Optional[str] = None
    status: Optional[str] = None
    source: Optional[str] = None
    latency_ms: Optional[int] = None
    message: Optional[str] = None
    checked_utc: Optional[datetime] = None


@dataclasses.dataclass
class AuditSummary(Model):
    """Counts of all matching audit records, independent of pagination."""

    total_count: int = 0
    event_counts: Dict[str, int] = dataclasses.field(default_factory=dict)
