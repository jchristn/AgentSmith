/**
 * Everything that needs a model, against the OpenAI-compatible stub in stub-llm.ts: model endpoint draft validation,
 * validate and health history, list filters, conversations with real turns (JSON and streaming), ask, and analytics.
 */
import { afterAll, beforeAll, describe, expect, it } from "vitest";
import type { AgentProfile, ModelEndpoint, Secret } from "../src/index.js";
import { expectHugoError, owner, unique } from "./helpers.js";
import { StubLlm } from "./stub-llm.js";

const stub = new StubLlm();
let key: Secret;
let endpoint: ModelEndpoint;
let profile: AgentProfile;
const startedUtc = new Date(Date.now() - 60_000);
// Conversations stay until the analytics test has counted their turns; the last test deletes them.
const conversations: string[] = [];

beforeAll(async () => {
  await stub.start();
  const client = owner();
  // Model endpoint API keys accept the BearerToken kind (D-049).
  key = await client.secrets.create({ name: unique("stub-key"), kind: "BearerToken", values: { token: "stub-api-key" } });
  endpoint = await client.modelEndpoints.create({
    name: unique("stub:model/js @lab"),
    kind: "Inference",
    provider: "OpenAICompatible",
    baseUrl: stub.baseUrl,
    model: stub.model,
    apiKeySecretId: key.id,
    maxRetries: 0,
    timeoutSeconds: 30,
  });
  profile = await client.agentProfiles.create({ name: unique("stubbed"), description: "harness profile backed by the stub model", inferenceEndpointId: endpoint.id });
});

afterAll(async () => {
  const client = owner();
  for (const id of conversations) await client.conversations.delete(id).catch(() => undefined);
  if (profile) await client.agentProfiles.delete(profile.id).catch(() => undefined);
  if (endpoint) await client.modelEndpoints.delete(endpoint.id).catch(() => undefined);
  if (key) await client.secrets.delete(key.id).catch(() => undefined);
  await stub.stop();
});

describe("model endpoints with a model", () => {
  it("names accept letters, digits, spaces, and . _ - : / @", () => {
    expect(endpoint.name).toContain("stub:model/js @lab");
    expect(endpoint.apiKeySecretId).toBe(key.id);
  });

  it("draft validation checks unsaved settings and stores nothing", async () => {
    const client = owner();
    const before = (await client.modelEndpoints.list({ maxResults: 1000 })).totalRecords;
    const draft = await client.modelEndpoints.validateDraft({ name: "draft:check", provider: "OpenAICompatible", baseUrl: stub.baseUrl, model: stub.model, maxRetries: 0, timeoutSeconds: 30 });
    expect(draft.success).toBe(true);
    expect(draft.kind).toBe("Inference");
    expect(draft.latencyMs).toBeGreaterThanOrEqual(0);
    expect((await client.modelEndpoints.list({ maxResults: 1000 })).totalRecords).toBe(before);

    const unreachable = await client.modelEndpoints.validateDraft({ provider: "OpenAICompatible", baseUrl: "http://127.0.0.1:9", model: "m", maxRetries: 0, timeoutSeconds: 5 });
    expect(unreachable.success).toBe(false);
    expect(unreachable.error).toBeTruthy();
    await expectHugoError(client.modelEndpoints.validateDraft({ model: "m" }), 400);
  });

  it("validate records health, health history lists it, and models lists the provider's models", async () => {
    const client = owner();
    const result = await client.modelEndpoints.validate(endpoint.id);
    expect(result.success).toBe(true);
    expect((await client.modelEndpoints.get(endpoint.id)).healthStatus).toBe("Healthy");
    const history = await client.modelEndpoints.healthHistory(endpoint.id);
    expect(history.length).toBeGreaterThanOrEqual(1);
    expect(history[0].modelEndpointId).toBe(endpoint.id);
    expect(history[0].status).toBe("Healthy");
    expect(history[0].source).toBe("Validate");
    expect((await client.modelEndpoints.healthHistory(endpoint.id, 1)).length).toBe(1);
    // maxResults is clamped to 1 through 1000.
    expect((await client.modelEndpoints.healthHistory(endpoint.id, 0)).length).toBe(1);
    expect(await client.modelEndpoints.models(endpoint.id)).toContain(stub.model);
  });

  it("kind, provider, and health filters", async () => {
    const client = owner();
    const ids = async (q: Parameters<typeof client.modelEndpoints.list>[0]) => (await client.modelEndpoints.list({ maxResults: 1000, ...q })).objects.map((m) => m.id);
    expect(await ids({ kind: "Inference" })).toContain(endpoint.id);
    expect(await ids({ kind: "Embedding" })).not.toContain(endpoint.id);
    expect(await ids({ provider: "OpenAICompatible" })).toContain(endpoint.id);
    expect(await ids({ provider: "Ollama" })).not.toContain(endpoint.id);
    expect(await ids({ health: "Healthy" })).toContain(endpoint.id);
    expect(await ids({ health: "Unhealthy" })).not.toContain(endpoint.id);
    await expectHugoError(client.modelEndpoints.list({ provider: "Skynet" as any }), 400, "ValidationFailed");
  });

  it("models answers 502 when the provider cannot be reached", async () => {
    const client = owner();
    const dead = await client.modelEndpoints.create({ name: unique("dead"), provider: "OpenAICompatible", baseUrl: "http://127.0.0.1:9", model: "m", maxRetries: 0, timeoutSeconds: 5 });
    try {
      const err = await expectHugoError(client.modelEndpoints.models(dead.id), 502);
      expect(["ProviderUnreachable", "ProviderTimeout"]).toContain(err.code);
    } finally {
      await client.modelEndpoints.delete(dead.id);
    }
  });
});

describe("conversations", () => {
  it("CRUD, filters, a JSON turn, paged messages, cancel, and export", async () => {
    const client = owner();
    const conversation = await client.conversations.create({ agentProfileId: profile.id, title: unique("chat") });
    try {
      expect(conversation.agentProfileId).toBe(profile.id);
      const renamed = await client.conversations.update(conversation.id, { title: "Renamed chat" });
      expect(renamed.title).toBe("Renamed chat");
      expect((await client.conversations.list({ agentProfileId: profile.id })).objects.map((x) => x.id)).toContain(conversation.id);
      expect((await client.conversations.list({ source: "Dashboard", maxResults: 1000 })).objects.map((x) => x.id)).toContain(conversation.id);

      stub.reply = "The answer is forty-two, Mr. Anderson.";
      const turn = await client.conversations.sendMessage(conversation.id, "What is the answer?");
      expect(turn.conversationId).toBe(conversation.id);
      expect(turn.turnId).toBeTruthy();
      expect(turn.finishReason).toBe("Completed");
      expect(turn.finalText).toBe(stub.reply);
      expect(turn.tokensOut).toBeGreaterThan(0);

      const page = await client.conversations.messages(conversation.id);
      expect(page.success).toBe(true);
      expect(page.objects.map((m) => m.role)).toEqual(["User", "Assistant"]);
      expect(page.objects[0].content).toBe("What is the answer?");
      expect(page.objects[1].content).toBe(stub.reply);
      expect(page.objects[1].turnId).toBe(turn.turnId);
      const first = await client.conversations.messages(conversation.id, { maxResults: 1 });
      expect(first.objects.length).toBe(1);
      expect(first.endOfResults).toBe(false);

      const reread = await client.conversations.get(conversation.id);
      expect(reread.messageCount).toBe(2);

      const cancel = await client.conversations.cancel(conversation.id);
      expect(cancel.conversationId).toBe(conversation.id);
      expect(cancel.cancelled).toBe(false);

      expect(await client.conversations.export(conversation.id, "md")).toContain(stub.reply);
      expect(JSON.parse(await client.conversations.export(conversation.id, "json"))).toBeTypeOf("object");
    } finally {
      conversations.push(conversation.id);
    }
  });

  it("chat streaming emits typed events ending with stream.closed", async () => {
    const client = owner();
    const conversation = await client.conversations.create({ agentProfileId: profile.id });
    try {
      stub.reply = "Streaming hello from the stub model.";
      const types: string[] = [];
      let text = "";
      let closed: Record<string, any> | undefined;
      for await (const event of client.conversations.streamMessage(conversation.id, "Say hello.")) {
        types.push(event.type);
        if (event.type === "assistant.delta") text += String(event.payload?.text ?? "");
        if (event.type === "stream.closed") closed = event.payload;
      }
      expect(types[0]).toBe("turn.started");
      expect(types).toContain("assistant.delta");
      expect(types).toContain("turn.completed");
      expect(types).not.toContain("error");
      expect(types[types.length - 1]).toBe("stream.closed");
      expect(text).toBe(stub.reply);
      expect(closed?.conversationId).toBe(conversation.id);
      expect(closed?.finishReason).toBeTruthy();
    } finally {
      conversations.push(conversation.id);
    }
  });
});

describe("ask", () => {
  it("delegates a task to the profile's agent loop and continues a conversation", async () => {
    const client = owner();
    stub.reply = "Delegated answer from Agent Smith.";
    const result = await client.agentProfiles.ask(profile.id, "Check the status page.");
    expect(result.status).toBe("Completed");
    expect(result.finalText).toBe(stub.reply);
    expect(result.conversationId).toBeTruthy();
    expect(Array.isArray(result.toolTrace)).toBe(true);
    try {
      const again = await client.agentProfiles.ask(profile.id, "And again.", result.conversationId);
      expect(again.conversationId).toBe(result.conversationId);
      const page = await client.conversations.messages(result.conversationId!);
      expect(page.objects.filter((m) => m.role === "User").length).toBe(2);
    } finally {
      conversations.push(result.conversationId!);
    }
  });
});

describe("analytics", () => {
  it("overview, tokens, tools, and models reflect the turns", async () => {
    const client = owner();
    const window = { from: startedUtc, to: new Date(Date.now() + 60_000) };
    const overview = await client.analytics.overview({ ...window, interval: "minute" });
    expect(overview.kind).toBe("overview");
    expect(overview.interval).toBe("minute");
    expect(overview.totals.turns).toBeGreaterThanOrEqual(3);
    expect(overview.conversations).toBeGreaterThanOrEqual(1);
    expect(overview.buckets.length).toBeGreaterThan(0);
    const tokens = await client.analytics.tokens(window);
    expect(tokens.totals.outputTokens).toBeGreaterThan(0);
    const tools = await client.analytics.tools(window);
    expect(Array.isArray(tools.tools)).toBe(true);
    const models = await client.analytics.models(window);
    const row = models.models.find((m) => m.modelEndpointId === endpoint.id);
    expect(row?.turns).toBeGreaterThanOrEqual(3);
    // The default window is the last seven days, bucketed by day.
    expect((await client.analytics.overview()).interval).toBe("day");
    await expectHugoError(client.analytics.overview({ interval: "fortnight" as any }), 400);
    await expectHugoError(client.analytics.overview({ from: new Date(), to: new Date(Date.now() - 3600_000) }), 400);
  });
});

describe("conversation delete", () => {
  it("removes the conversation and its messages", async () => {
    const client = owner();
    expect(conversations.length).toBeGreaterThanOrEqual(3);
    for (const id of conversations.splice(0)) {
      await client.conversations.delete(id);
      await expectHugoError(client.conversations.get(id), 404);
      await expectHugoError(client.conversations.messages(id), 404);
    }
  });
});
