"""Typed errors raised by the Hugo SDK."""

from __future__ import annotations

import json
from typing import Any, Dict, Optional


class HugoError(Exception):
    """Raised when the Hugo API answers with a non-success status.

    Attributes:
        status: HTTP status code.
        error: Error class name from the body, for example ``NotFound`` or ``Forbidden``.
        code: Stable machine code, for example ``RouteNotFound``, ``PasswordChangeRequired``, or ``ReauthenticationRequired``.
        message: Human readable message.
        request_id: Request identifier from the body, or from the ``x-request-id`` response header.
        fields: Field level messages for validation failures (``fields`` or ``errors`` in the body).
        field_codes: Field level machine codes for validation failures.
        body: Raw response body text.
    """

    def __init__(self, status: int, body: str = "", request_id: Optional[str] = None):
        self.status = status
        self.body = body or ""
        parsed: Optional[Dict[str, Any]] = None
        try:
            candidate = json.loads(self.body) if self.body else None
            if isinstance(candidate, dict):
                parsed = candidate
        except ValueError:
            parsed = None
        self.details: Dict[str, Any] = parsed or {}
        self.error: Optional[str] = self.details.get("error")
        self.code: Optional[str] = self.details.get("code")
        self.message: str = self.details.get("message") or (self.body[:300] if self.body else f"HTTP {status}")
        self.request_id: Optional[str] = self.details.get("requestId") or request_id
        self.fields: Dict[str, str] = self.details.get("fields") or self.details.get("errors") or {}
        self.field_codes: Dict[str, str] = self.details.get("fieldCodes") or {}
        self.required_permission: Optional[str] = self.details.get("requiredPermission")
        label = self.code or self.error or "Error"
        super().__init__(f"Hugo API returned {status} {label}: {self.message}" + (f" (request {self.request_id})" if self.request_id else ""))

    @property
    def status_code(self) -> int:
        """Alias of ``status``."""
        return self.status

    @property
    def is_not_found(self) -> bool:
        """True for any 404."""
        return self.status == 404

    @property
    def is_route_not_found(self) -> bool:
        """True when the server has no route for the request (404 RouteNotFound), for example a preview route the
        server does not implement yet."""
        return self.status == 404 and self.code == "RouteNotFound"
