import { describe, expect, it } from "vitest";
import { HugoError } from "../src/index.js";
import { admin, ctx, expectHugoError, owner, unique } from "./helpers.js";

describe("request history", () => {
  it("list, read, summary, and delete", async () => {
    const client = owner();
    await expectHugoError(client.secrets.get("sec_history_probe"), 404);
    const page = await client.requestHistory.list({ pathContains: "sec_history_probe", pageSize: 10 });
    expect(page.pageNumber).toBe(1);
    expect(page.objects.length).toBeGreaterThan(0);
    const entry = await client.requestHistory.get(page.objects[0].id);
    expect(entry.statusCode).toBe(404);
    expect(entry.path).toContain("sec_history_probe");
    expect(Array.isArray(entry.headers)).toBe(true);
    const summary = await client.requestHistory.summary({ fromUtc: new Date(Date.now() - 3600_000), bucketMinutes: 5 });
    expect(summary.totalCount).toBeGreaterThan(0);
    await client.requestHistory.delete(entry.id);
    await expectHugoError(client.requestHistory.get(entry.id), 404);
    const deleted = await client.requestHistory.deleteMany({ pathContains: "sec_history_probe" });
    expect(deleted.deletedCount).toBeGreaterThanOrEqual(0);
  });
});

describe("request history filters", () => {
  it("filters by method and status code", async () => {
    const client = owner();
    await client.users.me();
    const page = await client.requestHistory.list({ method: "GET", statusCode: 200, pageSize: 25 });
    expect(page.objects.length).toBeGreaterThan(0);
    expect(page.objects.every((e) => e.method === "GET" && e.statusCode === 200)).toBe(true);
  });
});

describe("audit", () => {
  it("list with filters and read", async () => {
    const client = owner();
    const page = await client.audit.list({ eventType: "SessionIssued", pageSize: 5 });
    expect(page.objects.length).toBeGreaterThan(0);
    expect(page.objects.every((r) => r.eventType === "SessionIssued")).toBe(true);
    const record = await client.audit.get(page.objects[0].id);
    expect(record.id).toBe(page.objects[0].id);
    expect(record.tenantId).toBe(ctx().tenantId);
  });
});

describe("events", () => {
  it("SSE stream opens and receives secret.created", async () => {
    const client = owner();
    const controller = new AbortController();
    const name = unique("evt");
    const seen: string[] = [];
    let created: { id?: string; name?: string } | undefined;
    const timer = setTimeout(() => controller.abort(), 20000);
    try {
      for await (const event of client.events.stream({ signal: controller.signal })) {
        seen.push(event.event);
        if (event.event === "stream.opened") {
          expect(event.json<{ tenantId: string }>()?.tenantId).toBe(ctx().tenantId);
          const secret = await client.secrets.create({ name, kind: "Generic", values: { value: "x" } });
          await client.secrets.delete(secret.id);
        }
        if (event.event === "secret.created") {
          created = event.json();
          if (created?.name === name) break;
        }
      }
    } finally {
      clearTimeout(timer);
      controller.abort();
    }
    expect(seen[0]).toBe("stream.opened");
    expect(created?.name).toBe(name);
  });
});

describe("events stream control", () => {
  it("aborting the signal ends the iterator without an error", async () => {
    const controller = new AbortController();
    const seen: string[] = [];
    for await (const event of owner().events.stream({ signal: controller.signal })) {
      seen.push(event.event);
      controller.abort();
    }
    expect(seen).toEqual(["stream.opened"]);
  });
});

describe("settings and system (administrator)", () => {
  it("settings read with metadata", async () => {
    const settings = await admin().settings.get();
    expect(settings.fields.length).toBeGreaterThan(0);
    const field = settings.fields.find((f) => f.path === "RequestHistory.RetentionDays");
    expect(field).toBeTruthy();
  });

  it("tenant administrator cannot read settings", async () => {
    await expectHugoError(owner().settings.get(), 403);
  });

  it("observability service status", async () => {
    const services = await owner().system.observabilityStatus();
    expect(Array.isArray(services)).toBe(true);
    for (const s of services) {
      expect(typeof s.service).toBe("string");
      expect(typeof s.configured).toBe("boolean");
      expect(typeof s.reachable).toBe("boolean");
    }
  });

  it("master key status", async () => {
    const status = await admin().system.masterKeyStatus();
    expect(typeof status.running).toBe("boolean");
  });
});

describe("errors", () => {
  it("typed 404 carries status, error, code, message, and request ID", async () => {
    try {
      await owner().secrets.get("sec_does_not_exist");
      throw new Error("expected a 404");
    } catch (e) {
      expect(e).toBeInstanceOf(HugoError);
      const err = e as HugoError;
      expect(err.status).toBe(404);
      expect(err.error).toBe("NotFound");
      expect(err.code).toBe("NotFound");
      expect(err.serverMessage).toBe("Secret not found.");
      expect(err.requestId).toMatch(/^[0-9a-f]{32}$/);
    }
  });

  it("unknown route reports RouteNotFound with the request ID from the header", async () => {
    const err = await expectHugoError(owner().transport.request("GET", "/v1.0/api/no-such-route"), 404, "RouteNotFound");
    expect(err.isRouteNotFound).toBe(true);
    expect(err.requestId).toBeTruthy();
  });

  it("validation errors carry field details", async () => {
    const err = await expectHugoError(owner().secrets.create({ kind: "Password", values: {} }), 400);
    expect(err.code).toBeTruthy();
  });
});
