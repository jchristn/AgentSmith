import { HugoError } from "./errors.js";
import { parseSseStream, SseEvent } from "./sse.js";
import { signRequest } from "./signing.js";
import { DEFAULT_BASE_URL, SDK_VERSION } from "./version.js";
import type { TokenResponse } from "./models.js";

/** How the transport authenticates each request. */
export type AuthMode = "none" | "token" | "accessKey" | "signed" | "adminApiKey";

/** Query parameter values; undefined, null, and empty strings are skipped. Dates are sent as ISO 8601. */
export type QueryValue = string | number | boolean | Date | null | undefined;
export type QueryParams = Record<string, QueryValue>;

/** Transport options. */
export interface TransportOptions {
  /** Per-request timeout in milliseconds for non-streaming requests. Default 300000 (5 minutes); 0 disables it. */
  timeoutMs?: number;
  /** Refresh a token obtained from login when it expires within refreshSkewSeconds. Default true. */
  autoRefresh?: boolean;
  /** Seconds before expiry at which auto refresh triggers. Default 60. */
  refreshSkewSeconds?: number;
  /** Custom fetch implementation (for example for proxies or tests). Default globalThis.fetch. */
  fetch?: typeof fetch;
  /** Extra headers sent on every request. */
  headers?: Record<string, string>;
}

/** Options for one request. */
export interface RequestOptions {
  query?: QueryParams;
  body?: unknown;
  headers?: Record<string, string>;
  accept?: string;
  signal?: AbortSignal;
  /** Skip authentication headers (anonymous routes such as token issuance). */
  anonymous?: boolean;
  /** Do not refresh the token before this request (used by refresh itself). */
  skipAutoRefresh?: boolean;
}

/** Percent-encode one path segment. */
export function seg(value: string): string {
  if (value === undefined || value === null || value === "") throw new Error("identifier is required");
  return encodeURIComponent(value);
}

/** Build a query string (without '?') from parameters. */
export function buildQuery(params?: QueryParams): string {
  if (!params) return "";
  const parts: string[] = [];
  for (const [key, value] of Object.entries(params)) {
    if (value === undefined || value === null || value === "") continue;
    const text = value instanceof Date ? value.toISOString() : typeof value === "boolean" ? (value ? "true" : "false") : String(value);
    parts.push(`${encodeURIComponent(key)}=${encodeURIComponent(text)}`);
  }
  return parts.join("&");
}

/**
 * HTTP transport shared by every client module: authentication headers (bearer token, access key headers, signed
 * requests, administrator API key), JSON bodies, typed errors, token auto refresh, and server-sent events.
 */
export class Transport {
  baseUrl: string;
  mode: AuthMode = "none";
  token?: string;
  tokenExpiresUtc?: Date;
  /** True when the token came from login or refresh, so it can be refreshed automatically. */
  refreshable = false;
  accessKey?: string;
  secretKey?: string;
  adminApiKey?: string;
  /** Tenant used for tenant-scoped paths and signed requests. */
  tenantId?: string;
  timeoutMs: number;
  autoRefresh: boolean;
  refreshSkewSeconds: number;
  headers: Record<string, string>;
  private readonly fetchImpl: typeof fetch;
  private refreshing?: Promise<TokenResponse>;

  constructor(baseUrl: string = DEFAULT_BASE_URL, options: TransportOptions = {}) {
    if (!baseUrl) throw new Error("baseUrl is required");
    this.baseUrl = baseUrl.replace(/\/+$/, "");
    this.timeoutMs = options.timeoutMs ?? 300000;
    this.autoRefresh = options.autoRefresh ?? true;
    this.refreshSkewSeconds = options.refreshSkewSeconds ?? 60;
    this.headers = { ...(options.headers ?? {}) };
    const f = options.fetch ?? (globalThis as { fetch?: typeof fetch }).fetch;
    if (!f) throw new Error("No fetch implementation is available; pass options.fetch.");
    this.fetchImpl = f;
  }

  /** Path under /v1.0/api/tenants/{tenantId}. */
  tenantPath(suffix: string, tenantId?: string): string {
    const tenant = tenantId ?? this.tenantId;
    if (!tenant) throw new Error("Set tenantId on the client (or log in) before calling tenant-scoped APIs.");
    return `/v1.0/api/tenants/${seg(tenant)}${suffix}`;
  }

  /** Store a bearer token. */
  setToken(token: string, tenantId?: string, expiresUtc?: string, refreshable = false): void {
    this.mode = "token";
    this.token = token;
    if (tenantId !== undefined) this.tenantId = tenantId;
    this.tokenExpiresUtc = expiresUtc ? new Date(expiresUtc) : undefined;
    this.refreshable = refreshable;
  }

  /** Remove all authentication state. */
  clearAuth(): void {
    this.mode = "none";
    this.token = undefined;
    this.tokenExpiresUtc = undefined;
    this.refreshable = false;
    this.accessKey = undefined;
    this.secretKey = undefined;
    this.adminApiKey = undefined;
  }

  /** Exchange the current token for a fresh one (POST /v1.0/token/refresh) and store it. */
  async refreshToken(): Promise<TokenResponse> {
    if (!this.refreshing) {
      this.refreshing = (async () => {
        const issued = await this.request<TokenResponse>("POST", "/v1.0/token/refresh", { skipAutoRefresh: true });
        this.setToken(issued.token, issued.tenantId ?? this.tenantId, issued.expiresUtc, true);
        return issued;
      })();
      try {
        return await this.refreshing;
      } finally {
        this.refreshing = undefined;
      }
    }
    return this.refreshing;
  }

  /** Send a request and parse the JSON response (undefined for empty bodies). */
  async request<T = unknown>(method: string, path: string, options: RequestOptions = {}): Promise<T> {
    const response = await this.send(method, path, options);
    const text = await response.text();
    return (text ? JSON.parse(text) : undefined) as T;
  }

  /** Send a request and return the response text. */
  async requestText(method: string, path: string, options: RequestOptions = {}): Promise<string> {
    const response = await this.send(method, path, options);
    return response.text();
  }

  /** Send a request and yield server-sent events until the server closes the stream or the signal aborts. */
  async *stream(method: string, path: string, options: RequestOptions = {}): AsyncGenerator<SseEvent> {
    const response = await this.send(method, path, { ...options, accept: options.accept ?? "text/event-stream" }, true);
    if (!response.body) return;
    yield* parseSseStream(response.body, options.signal);
  }

  /** Send a request and return the raw Response; throws HugoError on a non-success status. */
  async send(method: string, path: string, options: RequestOptions = {}, streaming = false): Promise<Response> {
    if (!options.anonymous && !options.skipAutoRefresh && this.shouldRefresh()) await this.refreshToken();

    const query = buildQuery(options.query);
    let body: string | undefined;
    const headers: Record<string, string> = { "User-Agent": `hugo-sdk-js/${SDK_VERSION}`, Accept: options.accept ?? "application/json", ...this.headers };
    if (options.body !== undefined && options.body !== null) {
      body = typeof options.body === "string" ? options.body : JSON.stringify(options.body);
      headers["Content-Type"] = "application/json";
    }
    if (options.headers) Object.assign(headers, options.headers);
    if (!options.anonymous) Object.assign(headers, await this.authHeaders(method, path, query, body));

    const controller = new AbortController();
    const onAbort = () => controller.abort(options.signal?.reason);
    if (options.signal) {
      if (options.signal.aborted) controller.abort(options.signal.reason);
      else options.signal.addEventListener("abort", onAbort, { once: true });
    }
    const timer = !streaming && this.timeoutMs > 0 ? setTimeout(() => controller.abort(new Error(`Request timed out after ${this.timeoutMs} ms`)), this.timeoutMs) : undefined;
    try {
      const response = await this.fetchImpl(this.baseUrl + path + (query ? `?${query}` : ""), { method, headers, body, signal: controller.signal });
      if (!response.ok) throw new HugoError(response.status, await response.text(), response.headers.get("x-request-id"));
      return response;
    } finally {
      if (timer) clearTimeout(timer);
      if (!streaming) options.signal?.removeEventListener("abort", onAbort);
    }
  }

  private shouldRefresh(): boolean {
    if (!this.autoRefresh || this.mode !== "token" || !this.refreshable || !this.tokenExpiresUtc) return false;
    return this.tokenExpiresUtc.getTime() - Date.now() <= this.refreshSkewSeconds * 1000;
  }

  private async authHeaders(method: string, path: string, query: string, body?: string): Promise<Record<string, string>> {
    switch (this.mode) {
      case "token":
        return this.token ? { Authorization: `Bearer ${this.token}` } : {};
      case "accessKey": {
        const h: Record<string, string> = { "x-access-key": this.accessKey ?? "", "x-secret-key": this.secretKey ?? "" };
        if (this.tenantId) h["x-tenant-id"] = this.tenantId;
        return h;
      }
      case "signed": {
        if (!this.tenantId) throw new Error("Signed requests need the credential's tenantId.");
        return { ...(await signRequest(this.accessKey ?? "", this.secretKey ?? "", this.tenantId, method, path, query, body)) };
      }
      case "adminApiKey": {
        const h: Record<string, string> = { "x-api-key": this.adminApiKey ?? "" };
        if (this.tenantId) h["x-tenant-id"] = this.tenantId;
        return h;
      }
      default:
        return {};
    }
  }
}
