import { createHash, createHmac } from "node:crypto";
import { describe, expect, it } from "vitest";
import { buildQuery, canonicalize, HugoClient, HugoError, parseSseStream, SDK_VERSION, sign, signingDate, SseParser } from "../src/index.js";

describe("unit: SSE parser", () => {
  it("parses events split across arbitrary chunks, comments, multi-line data, and CRLF", () => {
    const text = ": comment\r\nevent: stream.opened\r\ndata: {\"tenantId\":\"t\"}\r\n\r\nid: 7\nevent: secret.created\ndata: line1\ndata: line2\n\ndata: plain\n\n";
    for (const size of [1, 2, 3, 7, 1000]) {
      const parser = new SseParser();
      const events = [];
      for (let i = 0; i < text.length; i += size) events.push(...parser.push(text.slice(i, i + size)));
      events.push(...parser.end());
      expect(events).toEqual([
        { event: "stream.opened", data: "{\"tenantId\":\"t\"}" },
        { event: "secret.created", id: "7", data: "line1\nline2" },
        { event: "message", data: "plain" },
      ]);
    }
  });

  it("flushes a trailing event without a blank line at end of stream", async () => {
    const bytes = new TextEncoder().encode("event: turn.completed\ndata: {\"ok\":true}");
    const stream = new ReadableStream<Uint8Array>({
      start(controller) {
        controller.enqueue(bytes);
        controller.close();
      },
    });
    const events = [];
    for await (const e of parseSseStream(stream)) events.push(e);
    expect(events).toEqual([{ event: "turn.completed", data: "{\"ok\":true}" }]);
  });
});

describe("unit: request signing", () => {
  it("matches the server's canonical form and HMAC-SHA256", async () => {
    const body = "{\"a\":1}";
    const canonical = await canonicalize({ method: "post", path: "/v1.0/api/x", query: "a=1&b=2", tenantId: "ten_1", date: "2026-10-14T00:00:00Z", nonce: "abc", body });
    const bodyHash = createHash("sha256").update(body).digest("hex");
    expect(canonical).toBe(`POST\n/v1.0/api/x\na=1&b=2\nten_1\n2026-10-14T00:00:00Z\nabc\n${bodyHash}`);
    expect(await sign("secret", canonical)).toBe(createHmac("sha256", "secret").update(canonical).digest("hex"));
    const empty = await canonicalize({ method: "GET", path: "/p", query: "", tenantId: "", date: "d", nonce: "n" });
    expect(empty.endsWith(createHash("sha256").update("").digest("hex"))).toBe(true);
    expect(signingDate(new Date("2026-01-02T03:04:05.678Z"))).toBe("2026-01-02T03:04:05Z");
  });
});

describe("unit: errors and query", () => {
  it("HugoError carries status, error, code, message, and request ID (body or header)", () => {
    const e = new HugoError(404, JSON.stringify({ error: "NotFound", code: "RouteNotFound", message: "No route matches the request." }), "req-header");
    expect(e.status).toBe(404);
    expect(e.error).toBe("NotFound");
    expect(e.code).toBe("RouteNotFound");
    expect(e.serverMessage).toBe("No route matches the request.");
    expect(e.requestId).toBe("req-header");
    expect(e.isRouteNotFound).toBe(true);
    const v = new HugoError(400, JSON.stringify({ code: "ValidationFailed", message: "bad", requestId: "body-id", fields: { name: "required" } }), "h");
    expect(v.requestId).toBe("body-id");
    expect(v.fields).toEqual({ name: "required" });
    const raw = new HugoError(502, "<html>bad gateway</html>");
    expect(raw.code).toBeUndefined();
    expect(raw.body).toContain("bad gateway");
  });

  it("buildQuery skips empty values and formats dates and booleans", () => {
    expect(buildQuery({ a: 1, b: undefined, c: null, d: "", e: true, f: new Date("2026-01-01T00:00:00Z"), g: "x y" })).toBe("a=1&e=true&f=2026-01-01T00%3A00%3A00.000Z&g=x%20y");
    expect(SDK_VERSION).toBe("0.1.0-alpha");
  });
});

describe("unit: rate limit retries", () => {
  function fakeFetch(statuses: number[], seen: number[]): typeof fetch {
    return (async () => {
      const status = statuses[Math.min(seen.length, statuses.length - 1)];
      seen.push(status);
      const body = status === 429 ? JSON.stringify({ error: "TooManyRequests", code: "RateLimited", message: "slow down" }) : JSON.stringify({ ok: true });
      return new Response(body, { status, headers: status === 429 ? { "Retry-After": "0.01" } : {} });
    }) as unknown as typeof fetch;
  }

  it("retries a 429 after Retry-After when rateLimitRetries is set", async () => {
    const seen: number[] = [];
    const client = new HugoClient("http://127.0.0.1:1", { fetch: fakeFetch([429, 429, 200], seen), rateLimitRetries: 3 });
    expect(await client.transport.request("GET", "/x", { anonymous: true })).toEqual({ ok: true });
    expect(seen).toEqual([429, 429, 200]);
  });

  it("throws the 429 with retryAfter when retries are off (the default) or exhausted", async () => {
    const seen: number[] = [];
    const off = new HugoClient("http://127.0.0.1:1", { fetch: fakeFetch([429], seen) });
    const err = await off.transport.request("GET", "/x", { anonymous: true }).catch((e: HugoError) => e) as HugoError;
    expect(err).toBeInstanceOf(HugoError);
    expect(err.code).toBe("RateLimited");
    expect(err.retryAfter).toBe("0.01");
    expect(seen).toEqual([429]);
    const exhausted: number[] = [];
    const few = new HugoClient("http://127.0.0.1:1", { fetch: fakeFetch([429], exhausted), rateLimitRetries: 2 });
    await expect(few.transport.request("GET", "/x", { anonymous: true })).rejects.toBeInstanceOf(HugoError);
    expect(exhausted).toEqual([429, 429, 429]);
  });
});
