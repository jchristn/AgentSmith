/**
 * Hugo domain routes (preview). Each test probes its list route first and skips with a clear reason when the
 * server answers 404, so the harness runs against servers built before Phase 3, 5, and 6 land.
 */
import { describe, expect, it } from "vitest";
import type { Connector, HugoClient } from "../src/index.js";
import { owner, routeMissing, unique } from "./helpers.js";

async function firstConnector(client: HugoClient): Promise<Connector | undefined> {
  const connectors = await client.connectors.list();
  return connectors.find((c) => c.type === "http") ?? connectors[0];
}

/** Create a resource of the first available connector with its default settings. */
async function createResource(client: HugoClient, connector: Connector) {
  const settings: Record<string, unknown> = {};
  for (const s of connector.settings ?? []) {
    if (s.defaultValue !== undefined && s.defaultValue !== null) settings[s.key] = s.defaultValue;
    else if (s.required) settings[s.key] = s.kind === "Integer" ? 1 : s.kind === "Boolean" ? false : s.key.toLowerCase().includes("url") ? "http://127.0.0.1:9" : "sdk";
  }
  return client.resources.create({ name: unique("res"), connectorType: connector.type, description: "js harness", environment: "test", settings });
}

describe("domain (preview)", () => {
  it("connectors list and read", async (t) => {
    const client = owner();
    const reason = await routeMissing(() => client.connectors.list());
    if (reason) return t.skip(reason);
    const connectors = await client.connectors.list();
    expect(connectors.length).toBeGreaterThan(0);
    const one = await client.connectors.get(connectors[0].type);
    expect(one.type).toBe(connectors[0].type);
  });

  it("resources CRUD, test, referrers, duplicate", async (t) => {
    const client = owner();
    const reason = (await routeMissing(() => client.resources.list())) ?? (await routeMissing(() => client.connectors.list()));
    if (reason) return t.skip(reason);
    const connector = await firstConnector(client);
    if (!connector) return t.skip("no connectors registered on this server");
    const resource = await createResource(client, connector);
    let copyId: string | undefined;
    try {
      expect(resource.connectorType).toBe(connector.type);
      expect((await client.resources.get(resource.id)).name).toBe(resource.name);
      expect((await client.resources.list({ connectorType: connector.type })).objects.map((r) => r.id)).toContain(resource.id);
      const updated = await client.resources.update(resource.id, { description: "changed" });
      expect(updated.description).toBe("changed");
      const tested = await client.resources.test(resource.id);
      expect(typeof tested.success).toBe("boolean");
      expect(Array.isArray(await client.resources.referrers(resource.id))).toBe(true);
      const copy = await client.resources.duplicate(resource.id, unique("res-copy"));
      copyId = copy.id;
      expect(copy.connectorType).toBe(connector.type);
    } finally {
      await client.resources.delete(resource.id);
      if (copyId) await client.resources.delete(copyId);
    }
  });

  it("resource knowledge put, read, versions, delete", async (t) => {
    const client = owner();
    const reason = (await routeMissing(() => client.resources.list())) ?? (await routeMissing(() => client.connectors.list()));
    if (reason) return t.skip(reason);
    const connector = await firstConnector(client);
    if (!connector) return t.skip("no connectors registered on this server");
    const resource = await createResource(client, connector);
    try {
      const knowledgeMissing = await routeMissing(() => client.resources.knowledge.list(resource.id));
      if (knowledgeMissing) return t.skip(knowledgeMissing);
      const entry = await client.resources.knowledge.put(resource.id, "notes", { content: "checkout runs on web-01" }, "overview");
      expect(entry.content).toContain("web-01");
      expect((await client.resources.knowledge.get(resource.id, "notes", "overview")).key).toBe("overview");
      expect((await client.resources.knowledge.listScope(resource.id, "notes")).length).toBeGreaterThan(0);
      expect((await client.resources.knowledge.versions(resource.id, "notes", "overview")).length).toBeGreaterThan(0);
      await client.resources.knowledge.delete(resource.id, "notes", "overview");
    } finally {
      await client.resources.delete(resource.id);
    }
  });

  it("agent profiles CRUD, duplicate, compiled instructions, tools, card", async (t) => {
    const client = owner();
    const reason = await routeMissing(() => client.agentProfiles.list());
    if (reason) return t.skip(reason);
    const profile = await client.agentProfiles.create({ name: unique("profile"), displayName: "JS Harness", description: "js harness" });
    let copyId: string | undefined;
    try {
      expect(profile.personaName).toBeTruthy();
      expect((await client.agentProfiles.get(profile.id)).name).toBe(profile.name);
      const updated = await client.agentProfiles.update(profile.id, { description: "changed" });
      expect(updated.description).toBe("changed");
      const compiled = await client.agentProfiles.compiledInstructions(profile.id, "Mcp");
      expect(typeof compiled.text).toBe("string");
      const tools = await client.agentProfiles.tools(profile.id);
      expect(tools.profileId).toBe(profile.id);
      await client.agentProfiles.card(profile.id);
      const copy = await client.agentProfiles.duplicate(profile.id, unique("profile-copy"));
      copyId = copy.id;
    } finally {
      await client.agentProfiles.delete(profile.id);
      if (copyId) await client.agentProfiles.delete(copyId);
    }
  });

  it("bindings CRUD and tool rules, then dry run", async (t) => {
    const client = owner();
    const reason =
      (await routeMissing(() => client.agentProfiles.list())) ??
      (await routeMissing(() => client.resources.list())) ??
      (await routeMissing(() => client.connectors.list()));
    if (reason) return t.skip(reason);
    const connector = await firstConnector(client);
    if (!connector) return t.skip("no connectors registered on this server");
    const resource = await createResource(client, connector);
    const profile = await client.agentProfiles.create({ name: unique("bprofile") });
    try {
      const binding = await client.agentProfiles.bindings.create(profile.id, { resourceId: resource.id, alias: "target", accessMode: "ReadOnly" });
      expect(binding.accessMode).toBe("ReadOnly");
      expect((await client.agentProfiles.bindings.list(profile.id)).map((b) => b.id)).toContain(binding.id);
      const updated = await client.agentProfiles.bindings.update(profile.id, binding.id, { accessMode: "ReadWriteWithApproval" });
      expect(updated.accessMode).toBe("ReadWriteWithApproval");
      const toolName = connector.tools?.[0]?.name;
      if (toolName) {
        await client.agentProfiles.bindings.setToolRules(profile.id, binding.id, [{ toolName, rule: "RequireApproval" }]);
        expect((await client.agentProfiles.bindings.toolRules(profile.id, binding.id)).map((r) => r.toolName)).toContain(toolName);
        const dryRunMissing = await routeMissing(() => client.toolInvocations.list());
        if (!dryRunMissing) {
          const outcome = await client.toolInvocations.dryRun({ profileId: profile.id, resource: "target", toolName, arguments: {} });
          expect(outcome.dryRun).toBe(true);
          expect(outcome.decision).toBeTruthy();
        }
      }
      await client.agentProfiles.bindings.delete(profile.id, binding.id);
    } finally {
      await client.agentProfiles.delete(profile.id);
      await client.resources.delete(resource.id);
    }
  });

  it("model endpoints CRUD", async (t) => {
    const client = owner();
    const reason = await routeMissing(() => client.modelEndpoints.list());
    if (reason) return t.skip(reason);
    const endpoint = await client.modelEndpoints.create({ name: unique("mep"), kind: "Inference", provider: "Ollama", baseUrl: "http://127.0.0.1:11434", model: "llama3.2" });
    try {
      expect(endpoint.provider).toBe("Ollama");
      expect((await client.modelEndpoints.get(endpoint.id)).model).toBe("llama3.2");
      const updated = await client.modelEndpoints.update(endpoint.id, { model: "qwen2.5" });
      expect(updated.model).toBe("qwen2.5");
    } finally {
      await client.modelEndpoints.delete(endpoint.id);
    }
  });

  it("tool invocations list and summary", async (t) => {
    const client = owner();
    const reason = await routeMissing(() => client.toolInvocations.list());
    if (reason) return t.skip(reason);
    expect((await client.toolInvocations.list({ maxResults: 5 })).success).toBe(true);
    expect(typeof (await client.toolInvocations.summary()).total).toBe("number");
  });

  it("approvals list", async (t) => {
    const client = owner();
    const reason = await routeMissing(() => client.approvals.list());
    if (reason) return t.skip(reason);
    const page = await client.approvals.list({ status: "Pending" });
    expect(Array.isArray(page.objects)).toBe(true);
  });

  it("tenant lockdown on and off", async (t) => {
    const client = owner();
    const reason = await routeMissing(() => client.lockdown.get());
    if (reason) return t.skip(reason);
    const on = await client.lockdown.enable("js harness");
    try {
      expect(on.lockdown).toBe(true);
      expect((await client.lockdown.get()).lockdown).toBe(true);
    } finally {
      const off = await client.lockdown.disable();
      expect(off.lockdown).toBe(false);
    }
  });

  it("conversations CRUD (streaming needs a model endpoint: HUGO_SDK_CHAT=1)", async (t) => {
    const client = owner();
    const reason = await routeMissing(() => client.conversations.list());
    if (reason) return t.skip(reason);
    const conversation = await client.conversations.create({ title: unique("conv") });
    try {
      expect((await client.conversations.get(conversation.id)).id).toBe(conversation.id);
      await client.conversations.messages(conversation.id);
      if (process.env.HUGO_SDK_CHAT === "1") {
        const types: string[] = [];
        for await (const event of client.conversations.streamMessage(conversation.id, "Say hello in five words.")) {
          types.push(event.type);
          if (event.type === "turn.completed" || event.type === "error") break;
        }
        expect(types).toContain("turn.started");
      }
    } finally {
      await client.conversations.delete(conversation.id);
    }
  });
});
