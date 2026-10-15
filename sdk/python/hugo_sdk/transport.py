"""HTTP transport shared by the sync and async clients: authentication headers, request signing, JSON, typed errors,
automatic session refresh, and server-sent event streams."""

from __future__ import annotations

import hashlib
import hmac
import json
import secrets as _secrets
from dataclasses import dataclass, field
from datetime import datetime, timedelta, timezone
from typing import Any, AsyncIterator, Callable, Dict, Iterator, Optional, Tuple
from urllib.parse import quote, urlencode

import httpx

from .errors import HugoError
from .models import Model, format_datetime
from .sse import SseEvent, SseParser
from .version import __version__

DEFAULT_BASE_URL = "http://127.0.0.1:8800"
USER_AGENT = f"hugo-sdk-python/{__version__}"


def canonicalize(method: str, path: str, query: str, tenant_id: str, date: str, nonce: str, body: Optional[bytes]) -> str:
    """Build the canonical string for a signed request. Matches the server's RequestSigner:
    METHOD\\nPATH\\nQUERY\\nTENANT\\nDATE\\nNONCE\\nhex(SHA256(BODY))."""
    return "\n".join([
        (method or "").upper(), path or "", query or "", tenant_id or "", date or "", nonce or "",
        hashlib.sha256(body or b"").hexdigest(),
    ])


def sign(secret_key: str, canonical: str) -> str:
    """Lowercase hex HMAC-SHA256 of the canonical string with the secret key."""
    return hmac.new(secret_key.encode("utf-8"), canonical.encode("utf-8"), hashlib.sha256).hexdigest()


def seg(value: str) -> str:
    """URL-encode one path segment."""
    if value is None or str(value) == "":
        raise ValueError("an identifier is required")
    return quote(str(value), safe="")


def clean_query(params: Optional[Dict[str, Any]]) -> str:
    """Encode query parameters, skipping None and empty values. Booleans become true/false, datetimes ISO 8601 UTC."""
    if not params:
        return ""
    out = []
    for key, value in params.items():
        if value is None or value == "":
            continue
        if isinstance(value, bool):
            text = "true" if value else "false"
        elif isinstance(value, datetime):
            text = format_datetime(value)
        else:
            text = str(value)
        out.append((key, text))
    return urlencode(out, quote_via=quote)


def to_body(body: Any) -> Any:
    """Convert a model or a dictionary containing models into JSON-ready data."""
    if body is None:
        return None
    if isinstance(body, Model):
        return body.to_dict()
    if isinstance(body, dict):
        return {k: to_body(v) for k, v in body.items() if v is not None}
    if isinstance(body, list):
        return [to_body(v) for v in body]
    if isinstance(body, datetime):
        return format_datetime(body)
    return body


@dataclass
class Call:
    """One HTTP request description. ``parse`` turns decoded JSON into the result; ``after`` observes the result."""

    method: str
    path: str
    params: Optional[Dict[str, Any]] = None
    body: Any = None
    parse: Optional[Callable[[Any], Any]] = None
    headers: Optional[Dict[str, str]] = None
    after: Optional[Callable[[Any], None]] = None
    raw_text: bool = False
    accept: Optional[str] = None


@dataclass
class AuthState:
    """Authentication state. ``mode`` is one of none, token, keys, signed, admin."""

    mode: str = "none"
    token: Optional[str] = None
    token_expires_utc: Optional[datetime] = None
    refreshable: bool = False
    access_key: Optional[str] = None
    secret_key: Optional[str] = None
    admin_api_key: Optional[str] = None
    tenant_id: Optional[str] = None
    auto_refresh: bool = True
    refresh_skew_seconds: int = 60
    extra_headers: Dict[str, str] = field(default_factory=dict)


class _TransportBase:
    def __init__(self, base_url: str, timeout: float, auth: Optional[AuthState] = None):
        if not base_url:
            raise ValueError("base_url is required")
        self.base_url = base_url.rstrip("/")
        self.timeout = timeout
        self.auth = auth or AuthState()

    def tenant_path(self, suffix: str = "", tenant_id: Optional[str] = None) -> str:
        tenant = tenant_id or self.auth.tenant_id
        if not tenant:
            raise ValueError("set tenant_id on the client (or log in) before calling tenant-scoped APIs")
        return f"/v1.0/api/tenants/{seg(tenant)}{suffix}"

    def _auth_headers(self, method: str, path: str, query: str, body: Optional[bytes]) -> Dict[str, str]:
        a = self.auth
        headers: Dict[str, str] = {}
        if a.mode == "token" and a.token:
            headers["Authorization"] = "Bearer " + a.token
        elif a.mode == "keys":
            headers["x-access-key"] = a.access_key or ""
            headers["x-secret-key"] = a.secret_key or ""
            if a.tenant_id:
                headers["x-tenant-id"] = a.tenant_id
        elif a.mode == "signed":
            date = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
            nonce = _secrets.token_hex(16)
            canonical = canonicalize(method, path, query, a.tenant_id or "", date, nonce, body)
            headers["x-access-key"] = a.access_key or ""
            headers["x-date"] = date
            headers["x-nonce"] = nonce
            headers["x-signature"] = sign(a.secret_key or "", canonical)
            if a.tenant_id:
                headers["x-tenant-id"] = a.tenant_id
        elif a.mode == "admin":
            headers["x-api-key"] = a.admin_api_key or ""
            if a.tenant_id:
                headers["x-tenant-id"] = a.tenant_id
        return headers

    def _prepare(self, call: Call) -> Tuple[str, Optional[bytes], Dict[str, str]]:
        query = clean_query(call.params)
        data: Optional[bytes] = None
        headers: Dict[str, str] = {"User-Agent": USER_AGENT, "Accept": call.accept or "application/json"}
        if call.body is not None:
            if isinstance(call.body, bytes):
                data = call.body
            elif isinstance(call.body, str):
                data = call.body.encode("utf-8")
            else:
                data = json.dumps(to_body(call.body)).encode("utf-8")
            headers["Content-Type"] = "application/json"
        headers.update(self.auth.extra_headers)
        if call.headers:
            headers.update({k: v for k, v in call.headers.items() if v is not None})
        headers.update(self._auth_headers(call.method, call.path, query, data))
        url = self.base_url + call.path + ("?" + query if query else "")
        return url, data, headers

    def _needs_refresh(self, call: Call) -> bool:
        a = self.auth
        if not (a.auto_refresh and a.mode == "token" and a.refreshable and a.token_expires_utc):
            return False
        if call.path.startswith("/v1.0/token"):
            return False
        return a.token_expires_utc - datetime.now(timezone.utc) <= timedelta(seconds=a.refresh_skew_seconds)

    @staticmethod
    def _finish(call: Call, status: int, text: str, request_id: Optional[str]) -> Any:
        if status >= 400:
            raise HugoError(status, text, request_id)
        if call.raw_text:
            result: Any = text
        else:
            decoded = json.loads(text) if text and text.strip() else None
            result = call.parse(decoded) if call.parse else decoded
        if call.after:
            call.after(result)
        return result

    def _refresh_call(self) -> Call:
        from .models import TokenResponse

        return Call("POST", "/v1.0/token/refresh", parse=TokenResponse.from_dict, after=self.store_token)

    def store_token(self, issued: Any) -> None:
        """Store a TokenResponse as the active bearer token."""
        if issued is None or not getattr(issued, "token", None):
            return
        self.auth.mode = "token"
        self.auth.token = issued.token
        self.auth.token_expires_utc = issued.expires_utc
        self.auth.refreshable = True
        if issued.tenant_id:
            self.auth.tenant_id = issued.tenant_id


class SyncTransport(_TransportBase):
    """Blocking transport on httpx.Client."""

    def __init__(self, base_url: str = DEFAULT_BASE_URL, timeout: float = 300.0, http: Optional[httpx.Client] = None,
                 auth: Optional[AuthState] = None):
        super().__init__(base_url, timeout, auth)
        self._owns = http is None
        self.http = http or httpx.Client(timeout=timeout)

    def run(self, call: Call) -> Any:
        if self._needs_refresh(call):
            self.run(self._refresh_call())
        url, data, headers = self._prepare(call)
        response = self.http.request(call.method, url, content=data, headers=headers, timeout=self.timeout)
        return self._finish(call, response.status_code, response.text, response.headers.get("x-request-id"))

    def stream(self, call: Call) -> Iterator[SseEvent]:
        if self._needs_refresh(call):
            self.run(self._refresh_call())
        call.accept = "text/event-stream"
        url, data, headers = self._prepare(call)
        with self.http.stream(call.method, url, content=data, headers=headers,
                              timeout=httpx.Timeout(self.timeout, read=None)) as response:
            if response.status_code >= 400:
                response.read()
                raise HugoError(response.status_code, response.text, response.headers.get("x-request-id"))
            parser = SseParser()
            for line in response.iter_lines():
                event = parser.feed(line)
                if event is not None:
                    yield event
            tail = parser.flush()
            if tail is not None:
                yield tail

    def close(self) -> None:
        if self._owns:
            self.http.close()


class AsyncTransport(_TransportBase):
    """Asynchronous transport on httpx.AsyncClient."""

    def __init__(self, base_url: str = DEFAULT_BASE_URL, timeout: float = 300.0, http: Optional[httpx.AsyncClient] = None,
                 auth: Optional[AuthState] = None):
        super().__init__(base_url, timeout, auth)
        self._owns = http is None
        self.http = http or httpx.AsyncClient(timeout=timeout)

    async def run(self, call: Call) -> Any:
        if self._needs_refresh(call):
            await self.run(self._refresh_call())
        url, data, headers = self._prepare(call)
        response = await self.http.request(call.method, url, content=data, headers=headers, timeout=self.timeout)
        return self._finish(call, response.status_code, response.text, response.headers.get("x-request-id"))

    async def stream(self, call: Call) -> AsyncIterator[SseEvent]:
        if self._needs_refresh(call):
            await self.run(self._refresh_call())
        call.accept = "text/event-stream"
        url, data, headers = self._prepare(call)
        async with self.http.stream(call.method, url, content=data, headers=headers,
                                    timeout=httpx.Timeout(self.timeout, read=None)) as response:
            if response.status_code >= 400:
                await response.aread()
                raise HugoError(response.status_code, response.text, response.headers.get("x-request-id"))
            parser = SseParser()
            async for line in response.aiter_lines():
                event = parser.feed(line)
                if event is not None:
                    yield event
            tail = parser.flush()
            if tail is not None:
                yield tail

    async def aclose(self) -> None:
        if self._owns:
            await self.http.aclose()
