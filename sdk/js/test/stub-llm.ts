/**
 * A minimal OpenAI-compatible model server for the harness, modeled on src/Test.Shared/Agent/StubLlm: chat completions
 * (streaming and not), model listing, and deterministic feature-hash embeddings. It never proposes tool calls, so a
 * turn against it completes with the configured reply.
 */
import { createServer, IncomingMessage, Server, ServerResponse } from "node:http";
import type { AddressInfo } from "node:net";

/** One request the stub received. */
export interface StubRequest {
  method: string;
  path: string;
  body: any;
}

export class StubLlm {
  /** Text every chat completion answers with. */
  reply = "Good evening, Mr. Anderson.";
  /** Model name reported by /models and in completions. */
  readonly model = "stub-model";
  /** Embedding dimensions. */
  readonly dimensions = 64;
  readonly requests: StubRequest[] = [];
  private server?: Server;
  private port = 0;

  /** Base URL, for example http://127.0.0.1:54321. */
  get baseUrl(): string {
    return `http://127.0.0.1:${this.port}`;
  }

  /** Chat completion requests received so far. */
  get chatRequests(): StubRequest[] {
    return this.requests.filter((r) => r.path.endsWith("/chat/completions"));
  }

  async start(): Promise<void> {
    this.server = createServer((req, res) => {
      this.handle(req, res).catch(() => res.destroy());
    });
    await new Promise<void>((resolve) => this.server!.listen(0, "127.0.0.1", resolve));
    this.port = (this.server.address() as AddressInfo).port;
  }

  async stop(): Promise<void> {
    if (!this.server) return;
    this.server.closeAllConnections?.();
    await new Promise<void>((resolve) => this.server!.close(() => resolve()));
    this.server = undefined;
  }

  private async handle(req: IncomingMessage, res: ServerResponse): Promise<void> {
    const chunks: Buffer[] = [];
    for await (const chunk of req) chunks.push(chunk as Buffer);
    const text = Buffer.concat(chunks).toString("utf8");
    let body: any = undefined;
    try {
      body = text ? JSON.parse(text) : undefined;
    } catch {
      body = text;
    }
    const path = new URL(req.url ?? "/", "http://127.0.0.1").pathname;
    this.requests.push({ method: req.method ?? "GET", path, body });

    if (req.method === "POST" && path.endsWith("/chat/completions")) return this.chat(body ?? {}, res);
    if (req.method === "POST" && path.endsWith("/embeddings")) return this.embeddings(body ?? {}, res);
    if (req.method === "GET" && path.endsWith("/models")) return json(res, 200, { object: "list", data: [{ id: this.model, object: "model", owned_by: "stub" }] });
    json(res, 404, { error: { message: "not found", type: "not_found" } });
  }

  private chat(body: any, res: ServerResponse): void {
    const messages: any[] = Array.isArray(body.messages) ? body.messages : [];
    const promptTokens = Math.max(1, Math.floor(messages.reduce((n, m) => n + String(m?.content ?? "").length, 0) / 4));
    const completionTokens = Math.max(1, Math.floor(this.reply.length / 4));
    const usage = { prompt_tokens: promptTokens, completion_tokens: completionTokens, total_tokens: promptTokens + completionTokens };
    const id = `chatcmpl-${Math.random().toString(16).slice(2, 14)}`;
    const created = Math.floor(Date.now() / 1000);

    if (!body.stream) {
      json(res, 200, {
        id,
        object: "chat.completion",
        created,
        model: this.model,
        choices: [{ index: 0, message: { role: "assistant", content: this.reply }, finish_reason: "stop" }],
        usage,
      });
      return;
    }

    res.writeHead(200, { "Content-Type": "text/event-stream", "Cache-Control": "no-cache" });
    const send = (payload: unknown) => res.write(`data: ${typeof payload === "string" ? payload : JSON.stringify(payload)}\n\n`);
    const chunk = (delta: Record<string, unknown>, finish: string | null) =>
      send({ id, object: "chat.completion.chunk", created, model: this.model, choices: [{ index: 0, delta, finish_reason: finish }] });
    const size = 8;
    for (let i = 0; i < this.reply.length; i += size) {
      const delta: Record<string, unknown> = { content: this.reply.slice(i, i + size) };
      if (i === 0) delta.role = "assistant";
      chunk(delta, null);
    }
    chunk({}, "stop");
    send({ id, object: "chat.completion.chunk", created, model: this.model, choices: [], usage });
    send("[DONE]");
    res.end();
  }

  private embeddings(body: any, res: ServerResponse): void {
    const inputs: string[] = Array.isArray(body.input) ? body.input.map(String) : [String(body.input ?? "")];
    const data = inputs.map((input, index) => ({ object: "embedding", index, embedding: this.embed(input) }));
    const tokens = Math.max(1, Math.floor(inputs.reduce((n, s) => n + s.length, 0) / 4));
    json(res, 200, { object: "list", data, model: body.model ?? "stub-embed", usage: { prompt_tokens: tokens, total_tokens: tokens } });
  }

  private embed(input: string): number[] {
    const vector = new Array<number>(this.dimensions).fill(0);
    for (const word of input.toLowerCase().split(/\W+/).filter(Boolean)) {
      let hash = 2166136261;
      for (let i = 0; i < word.length; i++) hash = Math.imul(hash ^ word.charCodeAt(i), 16777619) >>> 0;
      vector[hash % this.dimensions] += 1;
    }
    const norm = Math.sqrt(vector.reduce((n, v) => n + v * v, 0)) || 1;
    return vector.map((v) => v / norm);
  }
}

function json(res: ServerResponse, status: number, body: unknown): void {
  const text = JSON.stringify(body);
  res.writeHead(status, { "Content-Type": "application/json", "Content-Length": Buffer.byteLength(text) });
  res.end(text);
}
