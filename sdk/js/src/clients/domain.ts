/**
 * Hugo domain clients. Routes are flat (/v1.0/api/...) with the tenant taken from the caller's token (system
 * administrators can set client.tenantOverride).
 */
import type {
  PromptDocument, PromptDocumentRevision, PromptDocumentRequest, PromptDocumentReferrers,
  AgentPromptDocument, AgentPromptDocumentRequest, AgentPromptDocumentList,
  AgentPromptDocumentOrderRequest, PromptDocumentPreview, PromptDocumentPreviewRequest,
  AgentCardPreview,
  AgentDelegationResult,
  AgentProfile,
  AgentTurnResult,
  AnalyticsQuery,
  AnalyticsReport,
  AgentProfileRequest,
  BuiltInTemplate,
  Approval,
  ApprovalFilter,
  Binding,
  BindingRequest,
  BindingToolRule,
  CallerToolCatalog,
  ChatEvent,
  CompiledInstructions,
  Connector,
  Conversation,
  ConversationCancelResponse,
  ConversationFilter,
  ConversationMessage,
  ConversationRequest,
  EnumerationQuery,
  EnumerationResult,
  InvocationSurface,
  Json,
  KnowledgeRequest,
  LockdownState,
  ModelEndpoint,
  ModelEndpointFilter,
  ModelEndpointRequest,
  ModelEndpointHealthRecord,
  ModelValidationResult,
  PromptDiffLine,
  PromptLayer,
  PromptRenderResponse,
  PromptTemplateVersion,
  PromptTemplateView,
  Resource,
  ResourceKnowledge,
  ResourceKnowledgeVersion,
  ResourceFilter,
  ResourceReferrer,
  ResourceRequest,
  ResourceTestResponse,
  SendMessageRequest,
  ToolCallRequest,
  ToolInvocation,
  ToolInvocationFilter,
  ToolInvocationOutcome,
  ToolInvocationSummary,
} from "../models.js";
import { parseEventData } from "../sse.js";
import { seg, Transport } from "../transport.js";
import { FlatCollection, toQuery } from "./base.js";

/** Connector catalog: descriptors with settings schema, secret slots, and tools. */
export class ConnectorsClient {
  constructor(private readonly t: Transport) {}

  list(): Promise<Connector[]> {
    return this.t.request("GET", "/v1.0/api/connectors");
  }

  get(type: string): Promise<Connector> {
    return this.t.request("GET", `/v1.0/api/connectors/${seg(type)}`);
  }
}

/** Knowledge entries of one resource (scopes and keys; key _root is the scope root). */
export class ResourceKnowledgeClient {
  constructor(private readonly t: Transport) {}

  private base(resourceId: string): string {
    return `/v1.0/api/resources/${seg(resourceId)}/knowledge`;
  }

  list(resourceId: string): Promise<ResourceKnowledge[]> {
    return this.t.request("GET", this.base(resourceId));
  }

  listScope(resourceId: string, scope: string): Promise<ResourceKnowledge[]> {
    return this.t.request("GET", `${this.base(resourceId)}/${seg(scope)}`);
  }

  get(resourceId: string, scope: string, key = "_root"): Promise<ResourceKnowledge> {
    return this.t.request("GET", `${this.base(resourceId)}/${seg(scope)}/${seg(key)}`);
  }

  /** Write an entry (key omitted writes the scope root). */
  put(resourceId: string, scope: string, body: KnowledgeRequest, key?: string): Promise<ResourceKnowledge> {
    const path = key ? `${this.base(resourceId)}/${seg(scope)}/${seg(key)}` : `${this.base(resourceId)}/${seg(scope)}`;
    return this.t.request("PUT", path, { body });
  }

  async delete(resourceId: string, scope: string, key?: string): Promise<void> {
    const path = key ? `${this.base(resourceId)}/${seg(scope)}/${seg(key)}` : `${this.base(resourceId)}/${seg(scope)}`;
    await this.t.request("DELETE", path);
  }

  versions(resourceId: string, scope: string, key = "_root"): Promise<ResourceKnowledgeVersion[]> {
    return this.t.request("GET", `${this.base(resourceId)}/${seg(scope)}/${seg(key)}/versions`);
  }
}

/** Resources (configured connections to services). */
export class ResourcesClient extends FlatCollection<Resource, ResourceRequest> {
  readonly knowledge: ResourceKnowledgeClient;

  constructor(t: Transport) {
    super(t, "/v1.0/api/resources");
    this.knowledge = new ResourceKnowledgeClient(t);
  }

  /** List resources, optionally filtered by connectorType. */
  override list(query?: ResourceFilter): Promise<EnumerationResult<Resource>> {
    return super.list(query);
  }

  /** Test a saved resource's connection and record the result. */
  test(id: string): Promise<ResourceTestResponse> {
    return this.t.request("POST", `${this.item(id)}/test`);
  }

  /** Validate and test unsaved settings (nothing is stored). */
  testUnsaved(body: ResourceRequest): Promise<ResourceTestResponse> {
    return this.t.request("POST", `${this.base()}/test`, { body });
  }

  duplicate(id: string, name: string): Promise<Resource> {
    return this.t.request("POST", `${this.item(id)}/duplicate`, { body: { name } });
  }

  referrers(id: string): Promise<ResourceReferrer[]> {
    return this.t.request("GET", `${this.item(id)}/referrers`);
  }

  /** Invoke a tool on a resource directly, without a profile. Status 202 while approval is pending. */
  invoke(id: string, toolName: string, args: Json = {}): Promise<ToolInvocationOutcome> {
    return this.t.request("POST", `${this.item(id)}/invoke`, { body: { toolName, arguments: args } });
  }
}

/** Bindings of one agent profile. */
export class BindingsClient {
  constructor(private readonly t: Transport) {}

  private base(profileId: string): string {
    return `/v1.0/api/agent-profiles/${seg(profileId)}/bindings`;
  }

  list(profileId: string): Promise<Binding[]> {
    return this.t.request("GET", this.base(profileId));
  }

  get(profileId: string, bindingId: string): Promise<Binding> {
    return this.t.request("GET", `${this.base(profileId)}/${seg(bindingId)}`);
  }

  create(profileId: string, body: BindingRequest): Promise<Binding> {
    return this.t.request("POST", this.base(profileId), { body });
  }

  update(profileId: string, bindingId: string, body: BindingRequest): Promise<Binding> {
    return this.t.request("PUT", `${this.base(profileId)}/${seg(bindingId)}`, { body });
  }

  async delete(profileId: string, bindingId: string): Promise<void> {
    await this.t.request("DELETE", `${this.base(profileId)}/${seg(bindingId)}`);
  }

  toolRules(profileId: string, bindingId: string): Promise<BindingToolRule[]> {
    return this.t.request("GET", `${this.base(profileId)}/${seg(bindingId)}/tool-rules`);
  }

  setToolRules(profileId: string, bindingId: string, rules: BindingToolRule[]): Promise<Binding> {
    return this.t.request("PUT", `${this.base(profileId)}/${seg(bindingId)}/tool-rules`, { body: rules });
  }
}

/** Agent profiles. */
export class AgentProfilesClient extends FlatCollection<AgentProfile, AgentProfileRequest> {
  readonly bindings: BindingsClient;

  constructor(t: Transport) {
    super(t, "/v1.0/api/agent-profiles");
    this.bindings = new BindingsClient(t);
  }

  duplicate(id: string, name: string): Promise<AgentProfile> {
    return this.t.request("POST", `${this.item(id)}/duplicate`, { body: { name } });
  }

  /** Compiled instructions as the caller would receive them on a surface. */
  compiledInstructions(id: string, surface?: InvocationSurface): Promise<CompiledInstructions> {
    return this.t.request("GET", `${this.item(id)}/compiled-instructions`, { query: { surface } });
  }

  /** Compiled instructions as plain text (text/plain). */
  compiledInstructionsText(id: string, surface?: InvocationSurface): Promise<string> {
    return this.t.requestText("GET", `${this.item(id)}/compiled-instructions`, { query: { surface, text: true }, accept: "text/plain, */*" });
  }

  /** Tools as the caller would see them on a surface (the server defaults to rest). */
  tools(id: string, surface?: string): Promise<CallerToolCatalog> {
    return this.t.request("GET", `${this.item(id)}/tools`, { query: { surface } });
  }

  /** A2A agent card preview. */
  card(id: string): Promise<AgentCardPreview> {
    return this.t.request("GET", `${this.item(id)}/card`);
  }

  /**
   * Delegate a task to the profile's own agent loop and wait for the answer (REST twin of the MCP ask_agent_smith
   * tool). Needs Execute on the profile and an inference endpoint on it. Approvals wait up to the server's
   * Approvals.McpWaitSeconds, after which status is ApprovalPending with approvalId. Pass conversationId to continue
   * a conversation. Throws HugoError 409 TurnInProgress when that conversation already has a running turn.
   */
  ask(id: string, message: string, conversationId?: string): Promise<AgentDelegationResult> {
    return this.t.request("POST", `${this.item(id)}/ask`, { body: { message, conversationId } });
  }
}

/** Model endpoints (inference and embedding). */
export class ModelEndpointsClient extends FlatCollection<ModelEndpoint, ModelEndpointRequest> {
  constructor(t: Transport) {
    super(t, "/v1.0/api/model-endpoints");
  }

  /** List model endpoints, optionally filtered by kind, provider, and health (unknown values answer 400 ValidationFailed). */
  override list(query?: ModelEndpointFilter): Promise<EnumerationResult<ModelEndpoint>> {
    return super.list(query);
  }

  /** Validate a saved endpoint with a tiny prompt (or embedding) and record its health. */
  validate(id: string): Promise<ModelValidationResult> {
    return this.t.request("POST", `${this.item(id)}/validate`);
  }

  /**
   * Validate unsaved settings (the body is the same as create) with a tiny prompt or embedding. Nothing is stored;
   * an unreachable provider is reported as success false with an error, not as an HTTP error.
   */
  validateDraft(body: ModelEndpointRequest): Promise<ModelValidationResult> {
    return this.t.request("POST", `${this.base()}/validate`, { body });
  }

  /** Models offered by the provider. Throws HugoError 502 ProviderUnreachable or ProviderTimeout when it cannot be reached. */
  models(id: string): Promise<string[]> {
    return this.t.request("GET", `${this.item(id)}/models`);
  }

  /** Recent health checks (validate and the server's health sweep), newest first. maxResults 1 to 1000, default 50. */
  healthHistory(id: string, maxResults?: number): Promise<ModelEndpointHealthRecord[]> {
    return this.t.request("GET", `${this.item(id)}/health-history`, { query: maxResults === undefined ? undefined : { maxResults: String(maxResults) } });
  }
}

/**
 * Tenant analytics from conversations, turns, tool invocations, and approvals. Every report takes an optional
 * window (from, to; default the last seven days) and bucket interval (minute, hour, or day; chosen from the window
 * length when omitted). An invalid window or too many buckets answers 400 ValidationFailed.
 */
export class AnalyticsClient {
  constructor(private readonly t: Transport) {}

  private static readonly base = "/v1.0/api/analytics";

  /** Conversations, turns, tokens, tool calls, approvals, and latency per bucket. */
  overview(query?: AnalyticsQuery): Promise<AnalyticsReport> {
    return this.t.request("GET", `${AnalyticsClient.base}/overview`, { query: toQuery(query) });
  }

  /** Token usage per bucket (input and output) with window totals. */
  tokens(query?: AnalyticsQuery): Promise<AnalyticsReport> {
    return this.t.request("GET", `${AnalyticsClient.base}/tokens`, { query: toQuery(query) });
  }

  /** Tool calls, errors, and approvals per bucket, with per-tool rows. */
  tools(query?: AnalyticsQuery): Promise<AnalyticsReport> {
    return this.t.request("GET", `${AnalyticsClient.base}/tools`, { query: toQuery(query) });
  }

  /** Turns, tokens, and latency per model endpoint, with per-bucket totals. */
  models(query?: AnalyticsQuery): Promise<AnalyticsReport> {
    return this.t.request("GET", `${AnalyticsClient.base}/models`, { query: toQuery(query) });
  }
}

/** Tool invocations: list, read, summary, dry run, and direct invoke. */
export class ToolInvocationsClient {
  constructor(private readonly t: Transport) {}

  private static readonly base = "/v1.0/api/tool-invocations";

  list(filter?: ToolInvocationFilter): Promise<EnumerationResult<ToolInvocation>> {
    return this.t.request("GET", ToolInvocationsClient.base, { query: toQuery(filter) });
  }

  get(id: string): Promise<ToolInvocation> {
    return this.t.request("GET", `${ToolInvocationsClient.base}/${seg(id)}`);
  }

  summary(since?: string): Promise<ToolInvocationSummary> {
    return this.t.request("GET", `${ToolInvocationsClient.base}/summary`, { query: { since } });
  }

  /** Resolve, authorize, validate, classify, and apply rules only; nothing is executed or recorded. */
  dryRun(body: ToolCallRequest): Promise<ToolInvocationOutcome> {
    return this.t.request("POST", `${ToolInvocationsClient.base}/dry-run`, { body });
  }

  /** Invoke through the full pipeline. Returns the outcome; decision ApprovalRequired carries approvalId. */
  invoke(body: ToolCallRequest): Promise<ToolInvocationOutcome> {
    return this.t.request("POST", ToolInvocationsClient.base, { body });
  }
}

/** Approvals of pending tool calls. */
export class ApprovalsClient {
  constructor(private readonly t: Transport) {}

  private static readonly base = "/v1.0/api/approvals";

  list(filter?: ApprovalFilter): Promise<EnumerationResult<Approval>> {
    return this.t.request("GET", ApprovalsClient.base, { query: toQuery(filter) });
  }

  get(id: string): Promise<Approval> {
    return this.t.request("GET", `${ApprovalsClient.base}/${seg(id)}`);
  }

  approve(id: string, note?: string): Promise<Approval> {
    return this.t.request("POST", `${ApprovalsClient.base}/${seg(id)}/approve`, { body: { note } });
  }

  reject(id: string, note?: string): Promise<Approval> {
    return this.t.request("POST", `${ApprovalsClient.base}/${seg(id)}/reject`, { body: { note } });
  }

  cancel(id: string, note?: string): Promise<Approval> {
    return this.t.request("POST", `${ApprovalsClient.base}/${seg(id)}/cancel`, { body: { note } });
  }

  /**
   * Poll until the approval leaves Pending or the timeout elapses; returns the last state read.
   * Defaults: timeoutMs 900000 (the server's default expiry), pollMs 2000.
   */
  async awaitDecision(id: string, options: { timeoutMs?: number; pollMs?: number; signal?: AbortSignal } = {}): Promise<Approval> {
    const deadline = Date.now() + (options.timeoutMs ?? 900000);
    const pollMs = Math.max(100, options.pollMs ?? 2000);
    while (true) {
      const approval = await this.get(id);
      if (approval.status !== "Pending" || Date.now() >= deadline || options.signal?.aborted) return approval;
      await new Promise((resolve) => setTimeout(resolve, Math.min(pollMs, Math.max(0, deadline - Date.now()))));
    }
  }
}

/** Tenant lockdown: while on, every non-Read call in the tenant is denied and pending approvals are cancelled. */
export class LockdownClient {
  constructor(private readonly t: Transport) {}

  private static readonly path = "/v1.0/api/tenant/lockdown";

  get(): Promise<LockdownState> {
    return this.t.request("GET", LockdownClient.path);
  }

  enable(reason?: string): Promise<LockdownState> {
    return this.t.request("POST", LockdownClient.path, { body: { reason } });
  }

  disable(): Promise<LockdownState> {
    return this.t.request("DELETE", LockdownClient.path);
  }
}

/**
 * Conversations and chat. Callers see their own conversations; tenant administrators see all. Sending a message runs
 * an agent turn on the conversation's profile (which needs an inference endpoint).
 */
export class ConversationsClient extends FlatCollection<Conversation, ConversationRequest> {
  constructor(t: Transport) {
    super(t, "/v1.0/api/conversations");
  }

  /** List conversations, optionally filtered by agentProfileId and source. */
  override list(query?: ConversationFilter): Promise<EnumerationResult<Conversation>> {
    return super.list(query);
  }

  /** A page of the conversation's messages in order (maxResults defaults to 1000 on this route). */
  messages(id: string, query?: EnumerationQuery): Promise<EnumerationResult<ConversationMessage>> {
    return this.t.request("GET", `${this.item(id)}/messages`, { query: toQuery(query) });
  }

  /**
   * Send a message and wait for the completed turn (JSON). Throws HugoError 409 TurnInProgress when a turn is
   * already running in the conversation.
   */
  sendMessage(id: string, message: string | SendMessageRequest): Promise<AgentTurnResult> {
    const body = typeof message === "string" ? { message } : message;
    return this.t.request("POST", `${this.item(id)}/messages`, { body });
  }

  /**
   * Send a message and stream the turn as typed events (turn.started, assistant.delta, assistant.thinking,
   * tool.proposed, tool.started, tool.completed, approval.required, approval.resolved, turn.completed, error), ending
   * with stream.closed (conversationId, turnId, finishReason, title).
   */
  async *streamMessage(id: string, message: string | SendMessageRequest, options: { signal?: AbortSignal } = {}): AsyncGenerator<ChatEvent> {
    const body = typeof message === "string" ? { message } : message;
    for await (const event of this.t.stream("POST", `${this.item(id)}/messages`, { body, signal: options.signal })) {
      const chatEvent: ChatEvent = { type: event.event, data: event.data, payload: parseEventData<Json>(event) };
      if (event.id !== undefined) chatEvent.id = event.id;
      yield chatEvent;
    }
  }

  /** Cancel the running turn; cancelled is false when no turn was running. */
  cancel(id: string): Promise<ConversationCancelResponse> {
    return this.t.request("POST", `${this.item(id)}/cancel`);
  }

  /** Export as Markdown (format md) or JSON (format json). */
  export(id: string, format: "md" | "json" = "md"): Promise<string> {
    return this.t.requestText("GET", `${this.item(id)}/export`, { query: { format }, accept: format === "md" ? "text/markdown, */*" : "application/json" });
  }
}

/**
 * Layered prompt templates by key (/v1.0/api/prompt-templates): Built-in, Global (system administrators), and
 * Tenant layers, with versions, diff, reset, and render. The tenant-scoped promptTemplates client covers the
 * /v1.0/api/tenants/{tenantId}/prompt-templates records.
 */
export class PromptLayersClient {
  constructor(private readonly t: Transport) {}

  private static readonly base = "/v1.0/api/prompt-templates";

  private key(key: string): string {
    return `${PromptLayersClient.base}/${seg(key)}`;
  }

  /** Every key with its layers and the layer in effect. */
  list(): Promise<PromptTemplateView[]> {
    return this.t.request("GET", PromptLayersClient.base);
  }

  get(key: string): Promise<PromptTemplateView> {
    return this.t.request("GET", this.key(key));
  }

  /** Save content at a layer (default Tenant; Global needs a system administrator). Creates a version. */
  save(key: string, content: string, changeNote?: string, layer: PromptLayer = "Tenant"): Promise<PromptTemplateView> {
    return this.t.request("PUT", this.key(key), { query: { layer }, body: { content, changeNote } });
  }

  /** Reset Tenant to the Global default, or Global to the Built-in. */
  reset(key: string, layer: PromptLayer = "Tenant"): Promise<PromptTemplateView> {
    return this.t.request("POST", `${this.key(key)}/reset`, { query: { layer } });
  }

  /** Version history of a layer, newest first. */
  versions(key: string, layer: PromptLayer = "Tenant"): Promise<PromptTemplateVersion[]> {
    return this.t.request("GET", `${this.key(key)}/versions`, { query: { layer } });
  }

  /** The shipped Built-in content. */
  builtin(key: string): Promise<BuiltInTemplate> {
    return this.t.request("GET", `${this.key(key)}/builtin`);
  }

  /** Line diff of a layer against the layer below it. */
  diff(key: string, layer: PromptLayer = "Tenant"): Promise<PromptDiffLine[]> {
    return this.t.request("GET", `${this.key(key)}/diff`, { query: { layer } });
  }

  /** Render content with sample data; includes resolve through the layers. */
  render(content: string, context: Json = {}): Promise<PromptRenderResponse> {
    return this.t.request("POST", `${PromptLayersClient.base}/render`, { body: { content, context } });
  }
}

/** Reusable, versioned Markdown library. All paths identify managed documents. */
export class PromptDocumentsClient {
  constructor(private readonly t: Transport) {}
  private item(id: string): string { return `/v1.0/api/prompt-documents/${seg(id)}`; }
  list(query?: EnumerationQuery & { active?: boolean }, signal?: AbortSignal): Promise<EnumerationResult<PromptDocument>> {
    return this.t.request("GET", "/v1.0/api/prompt-documents", { query: toQuery(query), signal });
  }
  get(id: string, signal?: AbortSignal): Promise<PromptDocument> { return this.t.request("GET", this.item(id), { signal }); }
  create(body: PromptDocumentRequest, signal?: AbortSignal): Promise<PromptDocument> { return this.t.request("POST", "/v1.0/api/prompt-documents", { body, signal }); }
  update(id: string, body: PromptDocumentRequest, signal?: AbortSignal): Promise<PromptDocument> { return this.t.request("PUT", this.item(id), { body, signal }); }
  delete(id: string, expectedVersion: number, signal?: AbortSignal): Promise<void> { return this.t.request<void>("DELETE", this.item(id), { query: { expectedVersion }, signal }); }
  revisions(id: string, query?: EnumerationQuery, signal?: AbortSignal): Promise<EnumerationResult<PromptDocumentRevision>> { return this.t.request("GET", `${this.item(id)}/revisions`, { query: toQuery(query), signal }); }
  revision(id: string, revision: number, signal?: AbortSignal): Promise<PromptDocumentRevision> { return this.t.request("GET", `${this.item(id)}/revisions/${revision}`, { signal }); }
  referrers(id: string, signal?: AbortSignal): Promise<PromptDocumentReferrers> { return this.t.request("GET", `${this.item(id)}/referrers`, { signal }); }
}
/** Profile-scoped document attachments and caller/public composition previews. */
export class AgentPromptDocumentsClient {
  constructor(private readonly t: Transport) {}
  private base(profileId: string): string { return `/v1.0/api/agent-profiles/${seg(profileId)}/documents`; }
  list(profileId: string, signal?: AbortSignal): Promise<AgentPromptDocumentList> { return this.t.request("GET", this.base(profileId), { signal }); }
  create(profileId: string, body: AgentPromptDocumentRequest, signal?: AbortSignal): Promise<AgentPromptDocument> { return this.t.request("POST", this.base(profileId), { body, signal }); }
  update(profileId: string, attachmentId: string, body: AgentPromptDocumentRequest, signal?: AbortSignal): Promise<AgentPromptDocument> { return this.t.request("PUT", `${this.base(profileId)}/${seg(attachmentId)}`, { body, signal }); }
  delete(profileId: string, attachmentId: string, expectedVersion: number, expectedDocumentSetVersion: number, signal?: AbortSignal): Promise<void> { return this.t.request<void>("DELETE", `${this.base(profileId)}/${seg(attachmentId)}`, { query: { expectedVersion, expectedDocumentSetVersion }, signal }); }
  reorder(profileId: string, body: AgentPromptDocumentOrderRequest, signal?: AbortSignal): Promise<AgentPromptDocumentList> { return this.t.request("PUT", `${this.base(profileId)}/order`, { body, signal }); }
  preview(profileId: string, body: PromptDocumentPreviewRequest = { audience: "Authenticated" }, signal?: AbortSignal): Promise<PromptDocumentPreview> { return this.t.request("POST", `${this.base(profileId)}/preview`, { body, signal }); }
}
