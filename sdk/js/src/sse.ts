/** One server-sent event. */
export interface SseEvent {
  /** Event type; "message" when the server sent no event field. */
  event: string;
  /** Event ID, when present. */
  id?: string;
  /** Data lines joined with a newline. */
  data: string;
}

/**
 * Incremental server-sent events parser shared by the events stream and chat streaming. Feed it text chunks in
 * any split; it returns the events completed by each chunk. Follows the WHATWG rules used by Hugo: a blank line
 * dispatches, lines beginning with a colon are comments, data lines are joined with a newline, retry is ignored.
 */
export class SseParser {
  private buffer = "";
  private eventName: string | undefined;
  private eventId: string | undefined;
  private data: string[] = [];
  private hasData = false;

  /** Feed a chunk of text and return the events it completed. */
  push(chunk: string): SseEvent[] {
    this.buffer += chunk;
    const events: SseEvent[] = [];
    let index: number;
    while ((index = this.nextLineBreak()) >= 0) {
      let line = this.buffer.slice(0, index);
      const crlf = this.buffer[index] === "\r" && this.buffer[index + 1] === "\n";
      this.buffer = this.buffer.slice(index + (crlf ? 2 : 1));
      if (line.endsWith("\r")) line = line.slice(0, -1);
      const event = this.line(line);
      if (event) events.push(event);
    }
    return events;
  }

  /** Flush at end of stream: dispatch a trailing event that had data but no terminating blank line. */
  end(): SseEvent[] {
    const events: SseEvent[] = [];
    if (this.buffer.length > 0) {
      const event = this.line(this.buffer);
      this.buffer = "";
      if (event) events.push(event);
    }
    if (this.hasData) {
      events.push(this.dispatch());
    }
    return events;
  }

  private nextLineBreak(): number {
    const n = this.buffer.indexOf("\n");
    const r = this.buffer.indexOf("\r");
    if (r >= 0 && (n < 0 || r < n)) {
      // A lone carriage return at the end of the buffer may be the first half of CRLF; wait for more input.
      if (r === this.buffer.length - 1) return -1;
      return r;
    }
    return n;
  }

  private line(line: string): SseEvent | undefined {
    if (line === "") {
      if (this.hasData || this.eventName !== undefined) return this.dispatch();
      return undefined;
    }
    if (line.startsWith(":")) return undefined;
    const colon = line.indexOf(":");
    const field = colon < 0 ? line : line.slice(0, colon);
    let value = colon < 0 ? "" : line.slice(colon + 1);
    if (value.startsWith(" ")) value = value.slice(1);
    if (field === "event") this.eventName = value;
    else if (field === "id") this.eventId = value;
    else if (field === "data") {
      this.data.push(value);
      this.hasData = true;
    }
    return undefined;
  }

  private dispatch(): SseEvent {
    const event: SseEvent = { event: this.eventName ?? "message", data: this.data.join("\n") };
    if (this.eventId !== undefined) event.id = this.eventId;
    this.eventName = undefined;
    this.eventId = undefined;
    this.data = [];
    this.hasData = false;
    return event;
  }
}

/** Parse a byte stream (for example a fetch Response body) into server-sent events. */
export async function* parseSseStream(body: ReadableStream<Uint8Array>, signal?: AbortSignal): AsyncGenerator<SseEvent> {
  const reader = body.getReader();
  const decoder = new TextDecoder();
  const parser = new SseParser();
  try {
    while (true) {
      if (signal?.aborted) return;
      let chunk: ReadableStreamReadResult<Uint8Array>;
      try {
        chunk = await reader.read();
      } catch (e) {
        if (signal?.aborted) return;
        throw e;
      }
      if (chunk.done) break;
      for (const event of parser.push(decoder.decode(chunk.value, { stream: true }))) yield event;
    }
    for (const event of parser.push(decoder.decode())) yield event;
    for (const event of parser.end()) yield event;
  } finally {
    try {
      await reader.cancel();
    } catch {
      // The stream is already closed or errored.
    }
  }
}

/** Parse the data of an event as JSON, returning undefined when it is empty or not JSON. */
export function parseEventData<T = unknown>(event: SseEvent): T | undefined {
  if (!event.data) return undefined;
  try {
    return JSON.parse(event.data) as T;
  } catch {
    return undefined;
  }
}
