import type {
  AuditFilter,
  AuditRecord,
  DeleteCountResponse,


  HealthResponse,
  MasterKeyRotationStatus,
  PagedResult,
  PromptRenderRequest,
  PromptRenderResponse,
  PromptTemplate,
  PromptTemplateRequest,
  PromptTemplateVersion,
  RequestHistoryEntry,
  RequestHistoryFilter,
  RequestHistorySummary,
  Secret,
  SecretImportRequest,
  SecretImportResult,
  SecretReferrer,
  SecretRequest,
  SecretRevealResponse,
  SecretValueRequest,
  ServerInfo,
  SettingsSaveResponse,
  SettingsView,
} from "../models.js";
import { parseEventData, SseEvent } from "../sse.js";
import { seg, Transport } from "../transport.js";
import { TenantCollection, toQuery } from "./base.js";

/** Audit records in the client's tenant. */
export class AuditClient {
  constructor(private readonly t: Transport) {}

  private base(): string {
    return this.t.tenantPath("/audit");
  }

  /** List audit records (eventType, principalId, result, fromUtc, toUtc, pageNumber, pageSize). */
  list(filter?: AuditFilter): Promise<PagedResult<AuditRecord>> {
    return this.t.request("GET", this.base(), { query: toQuery(filter) });
  }

  get(id: string): Promise<AuditRecord> {
    return this.t.request("GET", `${this.base()}/${seg(id)}`);
  }

  async delete(id: string): Promise<void> {
    await this.t.request("DELETE", `${this.base()}/${seg(id)}`);
  }

  /** Delete every record matching the filter. */
  deleteMany(filter: AuditFilter): Promise<DeleteCountResponse> {
    return this.t.request("DELETE", this.base(), { query: toQuery(filter) });
  }
}

/** Captured requests (tenant scoped for tenant administrators; all tenants for system administrators). */
export class RequestHistoryClient {
  constructor(private readonly t: Transport) {}

  private static readonly base = "/v1.0/api/request-history";

  list(filter?: RequestHistoryFilter): Promise<PagedResult<RequestHistoryEntry>> {
    return this.t.request("GET", RequestHistoryClient.base, { query: toQuery(filter) });
  }

  /** Time-bucketed counts. bucketMinutes defaults to 15 on the server. */
  summary(options?: { fromUtc?: Date | string; toUtc?: Date | string; bucketMinutes?: number }): Promise<RequestHistorySummary> {
    return this.t.request("GET", `${RequestHistoryClient.base}/summary`, { query: toQuery(options) });
  }

  /** Read one entry with headers and bodies. */
  get(id: string): Promise<RequestHistoryEntry> {
    return this.t.request("GET", `${RequestHistoryClient.base}/${seg(id)}`);
  }

  async delete(id: string): Promise<void> {
    await this.t.request("DELETE", `${RequestHistoryClient.base}/${seg(id)}`);
  }

  deleteMany(filter: RequestHistoryFilter): Promise<DeleteCountResponse> {
    return this.t.request("DELETE", RequestHistoryClient.base, { query: toQuery(filter) });
  }
}

/** Secrets vault. Values are write-only except through reveal, which needs a recent reauthenticate. */
export class SecretsClient extends TenantCollection<Secret, SecretRequest> {
  constructor(t: Transport) {
    super(t, "secrets");
  }

  /** Replace the supplied field values; omitted fields keep stored values; remove deletes fields. */
  setValues(id: string, body: SecretValueRequest): Promise<Secret> {
    return this.t.request("PUT", `${this.item(id)}/values`, { body });
  }

  /** Resources and model endpoints that use the secret. */
  referrers(id: string): Promise<SecretReferrer[]> {
    return this.t.request("GET", `${this.item(id)}/referrers`);
  }

  /** Describe an SSH key, service account JSON, kubeconfig, or PEM certificate without storing it. */
  parse(body: SecretImportRequest): Promise<SecretImportResult> {
    return this.t.request("POST", `${this.base()}/parse`, { body });
  }

  /**
   * Reveal plaintext values. Needs the Reveal permission, a reason, and (for user sessions) a call to
   * client.reauthenticate(password) within the server's Secrets.RevealReauthMinutes. Audited.
   */
  reveal(id: string, reason: string): Promise<SecretRevealResponse> {
    return this.t.request("POST", `${this.item(id)}/reveal`, { body: { reason } });
  }
}

/** Prompt templates (tenant and built-in) with versions. */
export class PromptTemplatesClient extends TenantCollection<PromptTemplate, PromptTemplateRequest> {
  constructor(t: Transport) {
    super(t, "prompt-templates");
  }

  versions(id: string): Promise<PromptTemplateVersion[]> {
    return this.t.request("GET", `${this.item(id)}/versions`);
  }

  version(id: string, version: number): Promise<PromptTemplateVersion> {
    return this.t.request("GET", `${this.item(id)}/versions/${version}`);
  }

  /** Restore a version as a new version. */
  restore(id: string, version: number): Promise<PromptTemplate> {
    return this.t.request("POST", `${this.item(id)}/restore/${version}`);
  }

  clone(id: string, name: string): Promise<PromptTemplate> {
    return this.t.request("POST", `${this.item(id)}/clone`, { body: { name } });
  }

  /** Reset a cloned built-in template to the shipped default. */
  reset(id: string): Promise<PromptTemplate> {
    return this.t.request("POST", `${this.item(id)}/reset`);
  }

  /** Render a stored template with sample data. */
  render(id: string, body: PromptRenderRequest = {}): Promise<PromptRenderResponse> {
    return this.t.request("POST", `${this.item(id)}/render`, { body });
  }

  /** Render ad hoc content with sample data. */
  renderAdHoc(body: PromptRenderRequest): Promise<PromptRenderResponse> {
    return this.t.request("POST", `${this.base()}/render`, { body });
  }
}

/** An event from GET /v1.0/api/events with a JSON helper. */
export interface HugoEvent extends SseEvent {
  /** Parse data as JSON (undefined when not JSON). */
  json<T = any>(): T | undefined;
}

/** Tenant event stream (Server-Sent Events). */
export class EventsClient {
  constructor(private readonly t: Transport) {}

  /**
   * Stream events until the server closes the connection or the signal aborts. The first event is stream.opened;
   * heartbeat events arrive while idle. Break out of the loop or abort the signal to close the connection.
   */
  async *stream(options: { signal?: AbortSignal } = {}): AsyncGenerator<HugoEvent> {
    for await (const event of this.t.stream("GET", "/v1.0/api/events", { signal: options.signal })) {
      yield { ...event, json: <T>() => parseEventData<T>(event) };
    }
  }
}

/** Server settings (system administrators). */
export class SettingsClient {
  constructor(private readonly t: Transport) {}

  get(): Promise<SettingsView> {
    return this.t.request("GET", "/v1.0/api/settings");
  }

  /** Update settings by path, for example [{ path: "RequestHistory.RetentionDays", value: "14" }]. */
  update(values: { path: string; value: string | null }[]): Promise<SettingsSaveResponse> {
    return this.t.request("PUT", "/v1.0/api/settings", { body: { values } });
  }

  testDatabase(): Promise<{ ok: boolean; provider?: string; message?: string }> {
    return this.t.request("POST", "/v1.0/api/settings/database/test");
  }
}

/** Health, server info, and system administration. */
export class SystemClient {
  constructor(private readonly t: Transport) {}

  /** Anonymous health. Throws HugoError(503) when the server is not healthy. */
  health(): Promise<HealthResponse> {
    return this.t.request("GET", "/v1.0/api/health", { anonymous: true });
  }

  /** Anonymous liveness. */
  live(): Promise<HealthResponse> {
    return this.t.request("GET", "/v1.0/api/health/live", { anonymous: true });
  }

  info(): Promise<ServerInfo> {
    return this.t.request("GET", "/v1.0/api/info");
  }

  serverInfo(): Promise<ServerInfo> {
    return this.t.request("GET", "/v1.0/api/server-info");
  }

  masterKeyStatus(): Promise<MasterKeyRotationStatus> {
    return this.t.request("GET", "/v1.0/api/system/master-key");
  }

  rotateMasterKey(): Promise<MasterKeyRotationStatus> {
    return this.t.request("POST", "/v1.0/api/system/master-key/rotate");
  }

  /** Restart the server process (the container runtime restarts it). */
  restart(): Promise<{ restarting: boolean; node?: string }> {
    return this.t.request("POST", "/v1.0/api/system/restart");
  }

  /** The OpenAPI document. */
  openApi(): Promise<Record<string, any>> {
    return this.t.request("GET", "/openapi.json", { anonymous: true });
  }
}
