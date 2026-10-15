/**
 * Hugo domain routes: connectors, resources, knowledge, agent profiles, bindings, tool invocations, approvals,
 * lockdown, prompt layers, model endpoints, and the system administrator tenant override. Conversations, chat
 * streaming, ask, and analytics run against a stub model in model.test.ts.
 */
import { describe, expect, it } from "vitest";
import type { HugoClient, ResourceRequest, ToolCallRequest } from "../src/index.js";
import { admin, ctx, expectHugoError, owner, unique } from "./helpers.js";

function httpResource(name: string): ResourceRequest {
  return { name, connectorType: "http", displayName: name, description: "Hugo JS SDK harness resource", environment: "test", settings: { baseUrl: "http://127.0.0.1:9" } };
}

function call(profileId: string, method: string, path: string): ToolCallRequest {
  return { profileId, resource: "api", toolName: "http_request", arguments: { method, path } };
}

async function withProfile<T>(client: HugoClient, accessMode: "ReadOnly" | "ReadWriteWithApproval" | "ReadWrite", fn: (profileId: string, resourceId: string) => Promise<T>): Promise<T> {
  const resource = await client.resources.create(httpResource(unique("res")));
  const profile = await client.agentProfiles.create({ name: unique("profile"), description: "harness profile" });
  try {
    await client.agentProfiles.bindings.create(profile.id, { resourceId: resource.id, alias: "api", accessMode });
    return await fn(profile.id, resource.id);
  } finally {
    await client.agentProfiles.delete(profile.id);
    await client.resources.delete(resource.id);
  }
}

describe("connectors", () => {
  it("list and read the http connector", async () => {
    const client = owner();
    const connectors = await client.connectors.list();
    expect(connectors.some((c) => c.type === "http")).toBe(true);
    expect(connectors.some((c) => c.type === "ssh")).toBe(true);
    const http = await client.connectors.get("http");
    expect(http.settings?.some((s) => s.key === "baseUrl" && s.required)).toBe(true);
    expect(http.tools?.some((t) => t.name === "http_request")).toBe(true);
    await expectHugoError(client.connectors.get("no-such-connector"), 404);
  });
});

describe("resources", () => {
  it("CRUD, connectorType filter, test, testUnsaved, duplicate, referrers, invalid create", async () => {
    const client = owner();
    const resource = await client.resources.create(httpResource(unique("res")));
    let copyId: string | undefined;
    try {
      expect(resource.id.startsWith("res_")).toBe(true);
      expect(resource.settings?.some((s) => s.key === "baseUrl" && s.value === "http://127.0.0.1:9")).toBe(true);
      const updated = await client.resources.update(resource.id, { description: "updated", defaultAccessModeCeiling: "ReadWriteWithApproval" });
      expect(updated.description).toBe("updated");
      expect(updated.defaultAccessModeCeiling).toBe("ReadWriteWithApproval");
      expect((await client.resources.list({ connectorType: "http" })).objects.map((r) => r.id)).toContain(resource.id);
      expect((await client.resources.list({ connectorType: "ssh" })).objects.map((r) => r.id)).not.toContain(resource.id);
      const test = await client.resources.test(resource.id);
      expect(test.success).toBe(false);
      expect(test.errorCode).toBeTruthy();
      expect(test.details).toBeTypeOf("object");
      expect(Array.isArray(test.validationErrors)).toBe(true);
      const reread = await client.resources.get(resource.id);
      expect(reread.lastTestUtc).toBeTruthy();
      expect(reread.lastTestResult).toBe(false);
      expect((await client.resources.testUnsaved(httpResource(unique("unsaved")))).success).toBe(false);
      const copy = await client.resources.duplicate(resource.id, unique("res-copy"));
      copyId = copy.id;
      expect(copy.connectorType).toBe("http");
      expect(await client.resources.referrers(resource.id)).toEqual([]);
      const invalid = await expectHugoError(client.resources.create({ name: unique("invalid"), connectorType: "http", settings: {} }), 400);
      expect(invalid.code).toBeTruthy();
    } finally {
      await client.resources.delete(resource.id);
      if (copyId) await client.resources.delete(copyId);
    }
  });

  it("knowledge entries, scope root, and versions", async () => {
    const client = owner();
    const resource = await client.resources.create(httpResource(unique("know")));
    try {
      const k = client.resources.knowledge;
      const first = await k.put(resource.id, "service", { content: "Checkout runs here." }, "notes");
      expect(first.version).toBe(1);
      const second = await k.put(resource.id, "service", { content: "Checkout runs on web-01." }, "notes");
      expect(second.version).toBe(2);
      const root = await k.put(resource.id, "service", { content: "Root summary." });
      expect(root.content).toBe("Root summary.");
      expect((await k.get(resource.id, "service", "_root")).id).toBe(root.id);
      expect((await k.list(resource.id)).length).toBe(2);
      expect((await k.listScope(resource.id, "service")).length).toBe(2);
      const versions = await k.versions(resource.id, "service", "notes");
      expect(versions.length).toBeGreaterThanOrEqual(2);
      expect(versions[0].version).toBe(2);
      await k.delete(resource.id, "service", "notes");
      await k.delete(resource.id, "service");
      expect((await k.list(resource.id)).length).toBe(0);
    } finally {
      await client.resources.delete(resource.id);
    }
  });
});

describe("agent profiles and bindings", () => {
  it("profiles CRUD, duplicate, card, compiled instructions, tools", async () => {
    const client = owner();
    const profile = await client.agentProfiles.create({ name: unique("profile"), description: "harness profile" });
    let copyId: string | undefined;
    try {
      expect(profile.personaName).toBe("Agent Smith");
      expect(profile.userAddress).toBe("Mr. Anderson");
      const updated = await client.agentProfiles.update(profile.id, { description: "updated profile", maxIterations: 6 });
      expect(updated.maxIterations).toBe(6);
      const copy = await client.agentProfiles.duplicate(profile.id, unique("profile-copy"));
      copyId = copy.id;
      const card = await client.agentProfiles.card(profile.id);
      expect(card.name).toBe(profile.name);
      expect(card.url).toContain("/v1.0/a2a/");
      expect(Array.isArray(card.skills)).toBe(true);
      const compiled = await client.agentProfiles.compiledInstructions(profile.id, "Mcp");
      expect(compiled.surface).toBe("Mcp");
      expect(compiled.text).toContain("Agent Smith");
      expect(compiled.tokenEstimate).toBeGreaterThan(0);
      expect(compiled.segments?.length).toBeGreaterThan(0);
      const text = await client.agentProfiles.compiledInstructionsText(profile.id, "Mcp");
      expect(text).toContain("Mr. Anderson");
      expect((await client.agentProfiles.tools(profile.id, "mcp")).profileId).toBe(profile.id);
      expect((await client.agentProfiles.list()).objects.map((p) => p.id)).toContain(profile.id);
    } finally {
      await client.agentProfiles.delete(profile.id);
      if (copyId) await client.agentProfiles.delete(copyId);
    }
  });

  it("bindings CRUD, tool rules, and resource referrers", async () => {
    const client = owner();
    const resource = await client.resources.create(httpResource(unique("bound")));
    const profile = await client.agentProfiles.create({ name: unique("bprofile") });
    try {
      const b = client.agentProfiles.bindings;
      const binding = await b.create(profile.id, { resourceId: resource.id, alias: "api", accessMode: "ReadOnly" });
      expect(binding.accessMode).toBe("ReadOnly");
      expect((await b.get(profile.id, binding.id)).alias).toBe("api");
      const updated = await b.update(profile.id, binding.id, { accessMode: "ReadWriteWithApproval", instructionMode: "Append", instructionText: "Only touch /status." });
      expect(updated.accessMode).toBe("ReadWriteWithApproval");
      const withRules = await b.setToolRules(profile.id, binding.id, [{ toolName: "http_health_check", rule: "Deny" }]);
      expect(withRules.toolRules?.some((r) => r.toolName === "http_health_check" && r.rule === "Deny")).toBe(true);
      expect((await b.toolRules(profile.id, binding.id)).length).toBe(1);
      expect((await b.list(profile.id)).map((x) => x.id)).toContain(binding.id);
      const referrers = await client.resources.referrers(resource.id);
      expect(referrers.some((r) => r.bindingId === binding.id && r.agentProfileId === profile.id)).toBe(true);
      await b.delete(profile.id, binding.id);
      expect((await b.list(profile.id)).length).toBe(0);
    } finally {
      await client.agentProfiles.delete(profile.id);
      await client.resources.delete(resource.id);
    }
  });
});

describe("tool invocations and approvals", () => {
  it("dry run decisions follow the access mode", async () => {
    const client = owner();
    await withProfile(client, "ReadOnly", async (profileId) => {
      const read = await client.toolInvocations.dryRun(call(profileId, "GET", "/status"));
      expect(read.dryRun).toBe(true);
      expect(read.decision).toBe("Allowed");
      expect(read.effect).toBe("Read");
      const del = await client.toolInvocations.dryRun(call(profileId, "DELETE", "/x"));
      expect(del.decision).toBe("Denied");
      expect(del.effect).toBe("Destructive");
      expect(del.decisionCode).toBeTruthy();
    });
  });

  it("invoke with approval: pending, await, reject, approve, history", async () => {
    const client = owner();
    await withProfile(client, "ReadWriteWithApproval", async (profileId) => {
      const pending = await client.toolInvocations.invoke(call(profileId, "POST", "/orders"));
      expect(pending.decision).toBe("ApprovalRequired");
      expect(pending.approvalId).toBeTruthy();
      expect(pending.approvalStatus).toBe("Pending");
      const listed = await client.approvals.list({ status: "Pending" });
      expect(listed.objects.map((a) => a.id)).toContain(pending.approvalId);
      const waited = await client.approvals.awaitDecision(pending.approvalId!, { timeoutMs: 300, pollMs: 100 });
      expect(waited.status).toBe("Pending");
      const rejected = await client.approvals.reject(pending.approvalId!, "not now");
      expect(rejected.status).toBe("Rejected");
      expect(rejected.decisionNote).toBe("not now");
      expect((await client.approvals.awaitDecision(pending.approvalId!, { timeoutMs: 1000 })).status).toBe("Rejected");

      const second = await client.toolInvocations.invoke(call(profileId, "PUT", "/orders/1"));
      const approved = await client.approvals.approve(second.approvalId!, "go ahead");
      expect(approved.status).toBe("Approved");
      const executed = await client.approvals.get(second.approvalId!);
      expect(executed.invocation).toBeTruthy();
      expect(executed.summary).toBe("PUT /orders/1 on api");
      const invocation = await client.toolInvocations.get(second.invocationId!);
      expect(invocation.status).not.toBe("Pending");

      const history = await client.toolInvocations.list({ agentProfileId: profileId });
      expect(history.objects.length).toBeGreaterThanOrEqual(2);
      const summary = await client.toolInvocations.summary();
      expect(summary.total).toBeGreaterThanOrEqual(2);
      expect(summary.byTool?.some((t) => t.status === "http_request")).toBe(true);
    });
  });

  it("direct resource invoke and approval cancel", async () => {
    const client = owner();
    const resource = await client.resources.create(httpResource(unique("direct")));
    try {
      const outcome = await client.resources.invoke(resource.id, "http_request", { method: "POST", path: "/x" });
      expect(outcome.decision).toBe("ApprovalRequired");
      const cancelled = await client.approvals.cancel(outcome.approvalId!, "changed my mind");
      expect(cancelled.status).toBe("Cancelled");
      await expectHugoError(client.approvals.approve(outcome.approvalId!), 409);
    } finally {
      await client.resources.delete(resource.id);
    }
  });

  it("tenant lockdown cancels pending approvals and denies writes", async () => {
    const client = owner();
    await withProfile(client, "ReadWrite", async (profileId, resourceId) => {
      const pending = await client.resources.invoke(resourceId, "http_request", { method: "POST", path: "/x" });
      expect((await client.lockdown.get()).lockdown).toBe(false);
      const on = await client.lockdown.enable("js harness check");
      try {
        expect(on.lockdown).toBe(true);
        expect(on.approvalsCancelled).toBeGreaterThanOrEqual(1);
        expect((await client.approvals.get(pending.approvalId!)).status).toBe("Cancelled");
        expect((await client.toolInvocations.dryRun(call(profileId, "POST", "/x"))).decision).toBe("Denied");
        expect((await client.toolInvocations.dryRun(call(profileId, "GET", "/x"))).decision).toBe("Allowed");
        expect((await admin().tenants.get(ctx().tenantId)).lockdown).toBe(true);
      } finally {
        const off = await client.lockdown.disable();
        expect(off.lockdown).toBe(false);
      }
    });
  });
});

describe("prompt layers", () => {
  it("save, versions, diff, built-in, Global denied, reset, render", async () => {
    const client = owner();
    const key = "persona.agent-smith";
    const views = await client.promptLayers.list();
    expect(views.some((v) => v.key === key)).toBe(true);
    const before = await client.promptLayers.get(key);
    expect(before.activeLayer).not.toBe("Tenant");
    try {
      const saved = await client.promptLayers.save(key, "You are {{profile.personaName}}. Harness override.", "harness");
      expect(saved.activeLayer).toBe("Tenant");
      expect(saved.tenant?.exists).toBe(true);
      const versions = await client.promptLayers.versions(key);
      expect(versions[0].changeNote).toBe("harness");
      const diff = await client.promptLayers.diff(key);
      expect(diff.some((d) => d.op === "add")).toBe(true);
      expect((await client.promptLayers.builtin(key)).content).toBe(before.builtInContent);
      await expectHugoError(client.promptLayers.save(key, "x", undefined, "Global"), 403);
    } finally {
      const reset = await client.promptLayers.reset(key);
      expect(reset.activeLayer).not.toBe("Tenant");
    }
    const rendered = await client.promptLayers.render("Good evening, {{input.who}}.", { input: { who: "Mr. Anderson" } });
    expect(rendered.rendered).toBe("Good evening, Mr. Anderson.");
  });
});

describe("model endpoints", () => {
  it("CRUD and validate", async () => {
    const client = owner();
    const endpoint = await client.modelEndpoints.create({ name: unique("model"), kind: "Inference", provider: "Ollama", baseUrl: "http://127.0.0.1:9", model: "qwen3:8b" });
    try {
      expect(endpoint.healthStatus).toBe("Unknown");
      const updated = await client.modelEndpoints.update(endpoint.id, { maxConcurrency: 2 });
      expect(updated.maxConcurrency).toBe(2);
      const validation = await client.modelEndpoints.validate(endpoint.id);
      expect(validation.success).toBe(false);
      expect((await client.modelEndpoints.get(endpoint.id)).healthStatus).toBe("Unhealthy");
      expect((await client.modelEndpoints.list()).objects.map((m) => m.id)).toContain(endpoint.id);
    } finally {
      await client.modelEndpoints.delete(endpoint.id);
    }
  });
});

describe("tenant override", () => {
  it("system administrator addresses the harness tenant with tenantOverride", async () => {
    const client = owner();
    const resource = await client.resources.create(httpResource(unique("override")));
    try {
      const sysadmin = admin();
      expect((await sysadmin.resources.list({ maxResults: 1000 })).objects.map((r) => r.id)).not.toContain(resource.id);
      sysadmin.tenantOverride = ctx().tenantId;
      expect((await sysadmin.resources.list({ maxResults: 1000 })).objects.map((r) => r.id)).toContain(resource.id);
    } finally {
      await client.resources.delete(resource.id);
    }
  });
});
