"""Server-sent event parsing shared by the event stream and chat streaming."""

from __future__ import annotations

import json
from dataclasses import dataclass, field
from typing import Any, AsyncIterator, Iterable, Iterator, List, Optional

CHAT_EVENT_TYPES = (
    "turn.started",
    "assistant.delta",
    "assistant.thinking",
    "tool.proposed",
    "tool.started",
    "tool.completed",
    "approval.required",
    "approval.resolved",
    "turn.completed",
    "error",
)


@dataclass
class SseEvent:
    """One server-sent event. ``event`` defaults to ``message`` when the server sends no event field."""

    event: str = "message"
    data: str = ""
    id: Optional[str] = None

    def json(self) -> Any:
        """Parse ``data`` as JSON; returns None for empty data and the raw string when it is not JSON."""
        if not self.data:
            return None
        try:
            return json.loads(self.data)
        except ValueError:
            return self.data

    @property
    def type(self) -> str:
        """Alias of ``event``."""
        return self.event


@dataclass
class ChatEvent(SseEvent):
    """A chat streaming event (turn.started, assistant.delta, assistant.thinking, tool.proposed, tool.started,
    tool.completed, approval.required, approval.resolved, turn.completed, error)."""

    @property
    def is_terminal(self) -> bool:
        """True for turn.completed and error."""
        return self.event in ("turn.completed", "error")

    @classmethod
    def from_sse(cls, event: SseEvent) -> "ChatEvent":
        return cls(event=event.event, data=event.data, id=event.id)


class SseParser:
    """Incremental parser. Feed it lines (without the trailing newline); it returns a complete event on each blank line.
    Comment lines (starting with ':') and the ``retry`` field are ignored. Multiple data lines are joined with newlines."""

    def __init__(self) -> None:
        self._event: Optional[str] = None
        self._id: Optional[str] = None
        self._data: List[str] = []

    def feed(self, line: str) -> Optional[SseEvent]:
        if line.endswith("\r"):
            line = line[:-1]
        if line == "":
            return self._dispatch()
        if line.startswith(":"):
            return None
        name, sep, value = line.partition(":")
        if sep and value.startswith(" "):
            value = value[1:]
        if name == "event":
            self._event = value
        elif name == "id":
            self._id = value
        elif name == "data":
            self._data.append(value)
        return None

    def flush(self) -> Optional[SseEvent]:
        """Dispatch a pending event at end of stream."""
        return self._dispatch()

    def _dispatch(self) -> Optional[SseEvent]:
        if not self._data and self._event is None:
            self._id = None
            return None
        event = SseEvent(event=self._event or "message", data="\n".join(self._data), id=self._id)
        self._event = None
        self._id = None
        self._data = []
        return event


def parse_lines(lines: Iterable[str]) -> Iterator[SseEvent]:
    """Parse an iterable of lines into events."""
    parser = SseParser()
    for line in lines:
        event = parser.feed(line)
        if event is not None:
            yield event
    tail = parser.flush()
    if tail is not None:
        yield tail


async def aparse_lines(lines: AsyncIterator[str]) -> AsyncIterator[SseEvent]:
    """Parse an async iterable of lines into events."""
    parser = SseParser()
    async for line in lines:
        event = parser.feed(line)
        if event is not None:
            yield event
    tail = parser.flush()
    if tail is not None:
        yield tail


def parse_text(text: str) -> List[SseEvent]:
    """Parse a complete SSE payload."""
    return list(parse_lines(text.splitlines()))
