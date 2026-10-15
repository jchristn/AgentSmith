/** Error body returned by the Hugo server for every non-success status. */
export interface ErrorBody {
  /** HTTP-level name, for example NotFound or Forbidden. */
  error?: string;
  /** Stable machine code, for example RouteNotFound, ValidationFailed, RateLimited, ReauthenticationRequired. */
  code?: string;
  message?: string;
  requestId?: string;
  requiredPermission?: string;
  resourceType?: string;
  tenantId?: string;
  fields?: Record<string, string>;
  errors?: Record<string, string>;
  fieldCodes?: Record<string, string>;
  [key: string]: unknown;
}

/**
 * Raised for every non-success HTTP status. Carries the HTTP status, the server's error name and code, the message,
 * the request ID (from the body, else the x-request-id response header), and field-level details when present.
 */
export class HugoError extends Error {
  /** HTTP status code. */
  readonly status: number;
  /** HTTP-level error name from the body, for example NotFound. */
  readonly error?: string;
  /** Stable error code, for example RouteNotFound. */
  readonly code?: string;
  /** Request ID for correlation with server logs, audit, and request history. */
  readonly requestId?: string;
  /** Raw response body text. */
  readonly body: string;
  /** Parsed error body, when the body was JSON. */
  readonly details?: ErrorBody;
  /** Retry-After response header (seconds), when present (429 RateLimited, 503 DatabaseBusy). */
  retryAfter?: string;

  constructor(status: number, body: string, requestIdHeader?: string | null) {
    let parsed: ErrorBody | undefined;
    try {
      const value = body ? JSON.parse(body) : undefined;
      parsed = value && typeof value === "object" && !Array.isArray(value) ? (value as ErrorBody) : undefined;
    } catch {
      parsed = undefined;
    }
    const message = parsed?.message ?? (body ? body.slice(0, 300) : `HTTP ${status}`);
    super(`Hugo API returned ${status}${parsed?.code ? " " + parsed.code : ""}: ${message}`);
    this.name = "HugoError";
    this.status = status;
    this.error = parsed?.error;
    this.code = parsed?.code;
    this.requestId = parsed?.requestId ?? (requestIdHeader || undefined);
    this.body = body;
    this.details = parsed;
  }

  /** The server's message, without the status prefix added to Error.message. */
  get serverMessage(): string {
    return this.details?.message ?? this.body;
  }

  /** Field-level validation messages keyed by field name. */
  get fields(): Record<string, string> | undefined {
    return this.details?.fields;
  }

  /** Per-setting validation errors keyed by setting path. */
  get errors(): Record<string, string> | undefined {
    return this.details?.errors;
  }

  /** Field-level validation codes keyed by field name. */
  get fieldCodes(): Record<string, string> | undefined {
    return this.details?.fieldCodes;
  }

  /** True for HTTP 404. */
  get isNotFound(): boolean {
    return this.status === 404;
  }

  /** True when the route itself does not exist on the server (404 RouteNotFound), for example a preview route. */
  get isRouteNotFound(): boolean {
    return this.status === 404 && this.code === "RouteNotFound";
  }
}

/** Type guard for HugoError. */
export function isHugoError(value: unknown): value is HugoError {
  return value instanceof HugoError;
}
