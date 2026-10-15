"""Hugo SDK for Python: sync and async clients for the Hugo REST API."""

from . import models
from .client import AsyncHugoClient, HugoClient
from .errors import HugoError
from .models import EnumerationResult, PagedResult
from .sse import CHAT_EVENT_TYPES, ChatEvent, SseEvent, SseParser, parse_lines, parse_text
from .transport import DEFAULT_BASE_URL, canonicalize, sign
from .version import __version__

__all__ = [
    "AsyncHugoClient",
    "CHAT_EVENT_TYPES",
    "ChatEvent",
    "DEFAULT_BASE_URL",
    "EnumerationResult",
    "HugoClient",
    "HugoError",
    "PagedResult",
    "SseEvent",
    "SseParser",
    "__version__",
    "canonicalize",
    "models",
    "parse_lines",
    "parse_text",
    "sign",
]
