import {
  AuditClient,
  EventsClient,
  PromptTemplatesClient,
  RequestHistoryClient,
  SecretsClient,
  SettingsClient,
  SystemClient,
} from "./clients/admin.js";
import {
  AgentProfilesClient,
  PromptDocumentsClient,
  AgentPromptDocumentsClient,
  AnalyticsClient,
  ApprovalsClient,
  ConnectorsClient,
  ConversationsClient,
  LockdownClient,
  ModelEndpointsClient,
  PromptLayersClient,
  ResourcesClient,
  ToolInvocationsClient,
} from "./clients/domain.js";
import {
  CredentialScopeAssignmentsClient,
  CredentialsClient,
  PermissionsClient,
  RolePermissionsClient,
  RolesClient,
  SessionsClient,
  TenantsClient,
  UserRoleAssignmentsClient,
  UsersClient,
} from "./clients/identity.js";
import type {
  HealthResponse,
  ReauthenticateResponse,
  ServerInfo,
  TenantLookupEntry,
  TokenDetails,
  TokenResponse,
  TokenValidResponse,
  User,
} from "./models.js";
import { Transport, TransportOptions } from "./transport.js";
import { DEFAULT_BASE_URL, SDK_VERSION } from "./version.js";

/** Client options. */
export type HugoClientOptions = TransportOptions;

/**
 * Entry point of the Hugo SDK. Choose an authentication method (login, loginWithAccessKey, useToken, useAccessKey,
 * useSignedRequests, or useAdminApiKey), then use the module properties. Safe for concurrent requests; change
 * credentials only between requests.
 */
export class HugoClient {
  /** SDK version. */
  static readonly version = SDK_VERSION;

  readonly transport: Transport;

  // Identity and administration (tenant scoped under /v1.0/api/tenants/{tenantId}).
  readonly tenants: TenantsClient;
  readonly users: UsersClient;
  readonly credentials: CredentialsClient;
  readonly roles: RolesClient;
  readonly permissions: PermissionsClient;
  readonly rolePermissions: RolePermissionsClient;
  readonly userRoleAssignments: UserRoleAssignmentsClient;
  readonly credentialScopeAssignments: CredentialScopeAssignmentsClient;
  readonly sessions: SessionsClient;
  readonly audit: AuditClient;
  readonly secrets: SecretsClient;
  readonly promptTemplates: PromptTemplatesClient;
  /** Layered prompt templates by key (Built-in, Global, Tenant) at /v1.0/api/prompt-templates. */
  readonly promptLayers: PromptLayersClient;
  readonly requestHistory: RequestHistoryClient;
  readonly events: EventsClient;
  readonly settings: SettingsClient;
  readonly system: SystemClient;

  // Hugo domain (flat routes with the tenant from the token).
  readonly connectors: ConnectorsClient;
  readonly resources: ResourcesClient;
  readonly agentProfiles: AgentProfilesClient;
  readonly promptDocuments: PromptDocumentsClient;
  readonly agentPromptDocuments: AgentPromptDocumentsClient;
  readonly modelEndpoints: ModelEndpointsClient;
  readonly toolInvocations: ToolInvocationsClient;
  readonly approvals: ApprovalsClient;
  readonly lockdown: LockdownClient;
  readonly conversations: ConversationsClient;
  /** Tenant analytics reports (overview, tokens, tools, models). */
  readonly analytics: AnalyticsClient;

  constructor(baseUrl: string = DEFAULT_BASE_URL, options: HugoClientOptions = {}) {
    const t = (this.transport = new Transport(baseUrl, options));
    this.tenants = new TenantsClient(t);
    this.users = new UsersClient(t);
    this.credentials = new CredentialsClient(t);
    this.roles = new RolesClient(t);
    this.permissions = new PermissionsClient(t);
    this.rolePermissions = new RolePermissionsClient(t);
    this.userRoleAssignments = new UserRoleAssignmentsClient(t);
    this.credentialScopeAssignments = new CredentialScopeAssignmentsClient(t);
    this.sessions = new SessionsClient(t);
    this.audit = new AuditClient(t);
    this.secrets = new SecretsClient(t);
    this.promptTemplates = new PromptTemplatesClient(t);
    this.promptLayers = new PromptLayersClient(t);
    this.requestHistory = new RequestHistoryClient(t);
    this.events = new EventsClient(t);
    this.settings = new SettingsClient(t);
    this.system = new SystemClient(t);
    this.connectors = new ConnectorsClient(t);
    this.resources = new ResourcesClient(t);
    this.agentProfiles = new AgentProfilesClient(t);
    this.promptDocuments = new PromptDocumentsClient(t);
    this.agentPromptDocuments = new AgentPromptDocumentsClient(t);
    this.modelEndpoints = new ModelEndpointsClient(t);
    this.toolInvocations = new ToolInvocationsClient(t);
    this.approvals = new ApprovalsClient(t);
    this.lockdown = new LockdownClient(t);
    this.conversations = new ConversationsClient(t);
    this.analytics = new AnalyticsClient(t);
  }

  /** Base URL without a trailing slash. */
  get baseUrl(): string {
    return this.transport.baseUrl;
  }

  /** Tenant used for tenant-scoped routes. Set by login; may be changed (for example by a system administrator). */
  get tenantId(): string | undefined {
    return this.transport.tenantId;
  }

  set tenantId(value: string | undefined) {
    this.transport.tenantId = value;
  }

  /**
   * System administrators only: the tenant that flat /v1.0/api/... routes (resources, agent profiles, approvals,
   * prompt layers, ...) act on, sent as ?tenantId=. Undefined uses the caller's own tenant.
   */
  get tenantOverride(): string | undefined {
    return this.transport.tenantOverride;
  }

  set tenantOverride(value: string | undefined) {
    this.transport.tenantOverride = value;
  }

  /** The current bearer token, when token authentication is in use. */
  get token(): string | undefined {
    return this.transport.mode === "token" ? this.transport.token : undefined;
  }

  // -------------------------------------------------------------------------------------------------------------
  // Authentication
  // -------------------------------------------------------------------------------------------------------------

  /**
   * Sign in with email and password (x-email, x-password, x-tenant-id). Stores the token and tenant.
   * mustChangePassword in the response is advisory: the account still has a seeded or reset password, every route
   * works, and changePassword clears the flag.
   */
  async login(email: string, password: string, tenantId?: string): Promise<TokenResponse> {
    const headers: Record<string, string> = { "x-email": email, "x-password": password };
    if (tenantId) headers["x-tenant-id"] = tenantId;
    const issued = await this.transport.request<TokenResponse>("POST", "/v1.0/token", { headers, anonymous: true });
    this.transport.setToken(issued.token, issued.tenantId ?? tenantId, issued.expiresUtc, true);
    return issued;
  }

  /**
   * Exchange credential keys for a session token (credentials in DirectHeader, SessionExchange, or Hybrid mode).
   * SessionExchange credentials work only here; use the returned token for other routes.
   */
  async loginWithAccessKey(accessKey: string, secretKey: string): Promise<TokenResponse> {
    const issued = await this.transport.request<TokenResponse>("POST", "/v1.0/token", {
      headers: { "x-access-key": accessKey, "x-secret-key": secretKey },
      anonymous: true,
    });
    this.transport.setToken(issued.token, issued.tenantId, issued.expiresUtc, true);
    return issued;
  }

  /** Use an existing bearer token. */
  useToken(token: string, tenantId?: string): void {
    this.transport.setToken(token, tenantId);
  }

  /** Send x-access-key and x-secret-key on every request (credentials in DirectHeader or Hybrid mode). */
  useAccessKey(accessKey: string, secretKey: string, tenantId?: string): void {
    this.transport.clearAuth();
    Object.assign(this.transport, { mode: "accessKey", accessKey, secretKey });
    if (tenantId !== undefined) this.transport.tenantId = tenantId;
  }

  /** Sign every request with HMAC-SHA256 (credentials in SignedRequest or Hybrid mode). tenantId is the credential's tenant. */
  useSignedRequests(accessKey: string, secretKey: string, tenantId: string): void {
    if (!tenantId) throw new Error("tenantId is required for signed requests");
    this.transport.clearAuth();
    Object.assign(this.transport, { mode: "signed", accessKey, secretKey, tenantId });
  }

  /** Use the system administrator API key (x-api-key), optionally targeting a tenant with x-tenant-id. */
  useAdminApiKey(apiKey: string, tenantId?: string): void {
    this.transport.clearAuth();
    Object.assign(this.transport, { mode: "adminApiKey", adminApiKey: apiKey });
    if (tenantId !== undefined) this.transport.tenantId = tenantId;
  }

  /** Drop all credentials. */
  clearAuth(): void {
    this.transport.clearAuth();
  }

  /** Issue a fresh token for the current principal and store it. */
  refresh(): Promise<TokenResponse> {
    return this.transport.refreshToken();
  }

  /** Revoke the current token and clear it. */
  async logout(): Promise<TokenValidResponse> {
    const result = await this.transport.request<TokenValidResponse>("DELETE", "/v1.0/token", { skipAutoRefresh: true });
    this.transport.clearAuth();
    return result;
  }

  /** Validate the presented credentials. */
  validateToken(): Promise<TokenValidResponse> {
    return this.transport.request("GET", "/v1.0/token");
  }

  /** Describe the authenticated principal. */
  tokenDetails(): Promise<TokenDetails> {
    return this.transport.request("GET", "/v1.0/token/details");
  }

  /** Re-enter the password on the current session (required shortly before revealing secrets). */
  reauthenticate(password: string): Promise<ReauthenticateResponse> {
    return this.transport.request("POST", "/v1.0/token/reauthenticate", { body: { password } });
  }

  /** Change the signed-in user's password (clears the advisory mustChangePassword flag; revokes the user's other sessions). */
  changePassword(currentPassword: string, newPassword: string): Promise<User> {
    return this.transport.request("POST", "/v1.0/api/users/me/password", { body: { currentPassword, newPassword } });
  }

  /** Tenants an email can sign in to (anonymous). */
  lookupTenants(email: string): Promise<TenantLookupEntry[]> {
    return this.transport.request("POST", "/v1.0/tenants/lookup", { body: { email }, anonymous: true });
  }

  /** Anonymous health check. */
  health(): Promise<HealthResponse> {
    return this.system.health();
  }

  /** Server version, endpoints, and features. */
  info(): Promise<ServerInfo> {
    return this.system.info();
  }
}

export default HugoClient;
