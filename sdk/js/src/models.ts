/**
 * Response and request models. Every interface is permissive: the server may return additional fields, and they are
 * preserved on the plain objects returned by the SDK. JSON on the wire is camelCase; enums are strings.
 */

/** Any JSON object. */
export type Json = Record<string, any>;

// ---------------------------------------------------------------------------------------------------------------
// Enumeration
// ---------------------------------------------------------------------------------------------------------------

export type EnumerationOrder = "CreatedDescending" | "CreatedAscending" | "NameAscending" | "NameDescending";

/** Paging, ordering, and filtering for list routes. */
export interface EnumerationQuery {
  /** 1 to 1000, default 25. */
  maxResults?: number;
  /** Records to skip, default 0. */
  skip?: number;
  ordering?: EnumerationOrder;
  /** Case-insensitive name substring. */
  search?: string;
  activeOnly?: boolean;
  includeBuiltIn?: boolean;
  /** Route-specific filters (for example roleId, userId, credentialId, connectorType). */
  [filter: string]: string | number | boolean | Date | undefined | null;
}

/** One page of a list route. */
export interface EnumerationResult<T> {
  success: boolean;
  maxResults: number;
  skip: number;
  totalRecords: number;
  recordsRemaining: number;
  endOfResults: boolean;
  timestampUtc: string;
  objects: T[];
}

/** Page shape used by audit and request history. */
export interface PagedResult<T> {
  pageNumber: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  objects: T[];
}

export interface DeleteCountResponse extends Json {
  deletedCount: number;
}

// ---------------------------------------------------------------------------------------------------------------
// Health, info, tokens
// ---------------------------------------------------------------------------------------------------------------

export interface HealthResponse extends Json {
  status: string;
  node?: string;
  version?: string;
  database?: string;
  schemaVersion?: number;
  expectedSchemaVersion?: number;
  background?: string;
  utc?: string;
}

export interface ServerInfo extends Json {
  product: string;
  version: string;
  nodeId?: string;
  restUrl?: string;
  mcpUrl?: string;
  a2aUrl?: string;
  openApiUrl?: string;
  databaseType?: string;
  telemetryEnabled?: boolean;
  requestHistoryEnabled?: boolean;
  routeCount?: number;
  repositoryUrl?: string;
  startedUtc?: string;
  features?: string[];
  observability?: Json;
}

export type PrincipalType = "Administrator" | "User" | "Credential";
export type AuthScheme = "BearerToken" | "XToken" | "PasswordHeaders" | "AccessKeySecret" | "AccessKeySignature" | "AdminApiKey";

export interface TokenResponse extends Json {
  token: string;
  expiresUtc: string;
  tenantId?: string;
  sessionId?: string;
  principalType?: PrincipalType;
  mustChangePassword?: boolean;
}

export interface TokenValidResponse extends Json {
  valid: boolean;
}

export interface TokenDetails extends Json {
  principalType?: PrincipalType;
  principalId?: string;
  principalName?: string;
  tenantId?: string;
  userId?: string;
  credentialId?: string;
  mustChangePassword?: boolean;
  reauthenticatedUtc?: string;
  sessionId?: string;
  authScheme?: AuthScheme;
  isAdmin?: boolean;
  isTenantAdmin?: boolean;
  email?: string;
  firstName?: string;
  lastName?: string;
  expiresUtc?: string;
}

export interface ReauthenticateResponse extends Json {
  reauthenticatedUtc: string;
  revealAllowedUntilUtc: string;
}

export interface TenantLookupEntry extends Json {
  tenantId: string;
  name: string;
}

// ---------------------------------------------------------------------------------------------------------------
// Identity
// ---------------------------------------------------------------------------------------------------------------

interface Entity extends Json {
  id: string;
  active?: boolean;
  isProtected?: boolean;
  createdUtc?: string;
  lastUpdateUtc?: string;
}

export interface Tenant extends Entity {
  name: string;
  description?: string;
  parentTenantId?: string;
  region?: string;
  defaultAgentProfileId?: string;
  lockdown?: boolean;
}

export interface TenantRequest extends Json {
  name?: string;
  description?: string;
  active?: boolean;
  region?: string;
  parentTenantId?: string;
  defaultAgentProfileId?: string;
}

export interface User extends Entity {
  tenantId: string;
  email: string;
  firstName?: string;
  lastName?: string;
  isAdmin?: boolean;
  isTenantAdmin?: boolean;
  mustChangePassword?: boolean;
  defaultAgentProfileId?: string;
  preferredLocale?: string;
}

export interface UserRequest extends Json {
  email?: string;
  password?: string;
  firstName?: string;
  lastName?: string;
  isTenantAdmin?: boolean;
  isAdmin?: boolean;
  active?: boolean;
  mustChangePassword?: boolean;
  preferredLocale?: string;
  defaultAgentProfileId?: string;
}

export type CredentialAuthMode = "DirectHeader" | "SignedRequest" | "SessionExchange" | "Hybrid";

export interface Credential extends Entity {
  tenantId: string;
  userId?: string;
  name: string;
  accessKey: string;
  secretKeyLast4?: string;
  authMode?: CredentialAuthMode;
  agentProfileId?: string;
  lockToProfile?: boolean;
  lastUsedUtc?: string;
  expiresUtc?: string;
}

export interface CredentialRequest extends Json {
  name?: string;
  userId?: string;
  authMode?: CredentialAuthMode;
  expiresUtc?: string;
  active?: boolean;
  roleName?: string;
}

/** Create and rotate return the secret key once. */
export interface CredentialCreateResponse extends Json {
  credential: Credential;
  accessKey: string;
  secretKey: string;
}

export interface Role extends Entity {
  tenantId?: string;
  name: string;
  description?: string;
  isBuiltIn?: boolean;
}

export interface RoleRequest extends Json {
  name?: string;
  description?: string;
  active?: boolean;
}

export type PermissionType = "Permit" | "Deny";

export interface Permission extends Entity {
  tenantId?: string;
  name: string;
  permissionType: PermissionType;
  isBuiltIn?: boolean;
  resourceTypes?: string[];
  operationTypes?: string[];
}

export interface PermissionRequest extends Json {
  name?: string;
  permissionType?: PermissionType;
  resourceTypes?: string[];
  operationTypes?: string[];
  active?: boolean;
}

export interface RolePermissionMap extends Entity {
  tenantId: string;
  roleId: string;
  permissionId: string;
}

export type ResourceScope = "Tenant" | "Resource";

export interface UserRoleAssignment extends Entity {
  tenantId: string;
  userId: string;
  roleId?: string;
  roleName?: string;
  resourceScope?: ResourceScope;
  resourceId?: string;
  inheritsToChildren?: boolean;
}

export interface CredentialScopeAssignment extends Entity {
  tenantId: string;
  credentialId: string;
  roleId?: string;
  roleName?: string;
  resourceScope?: ResourceScope;
  resourceId?: string;
  inheritsToChildren?: boolean;
}

export interface AssignmentRequest extends Json {
  userId?: string;
  credentialId?: string;
  roleId?: string;
  roleName?: string;
  resourceScope?: ResourceScope;
  resourceId?: string;
  inheritsToChildren?: boolean;
  active?: boolean;
}

export interface EffectivePermissions extends Json {
  principalType?: PrincipalType;
  principalId?: string;
  tenantId?: string;
  isAdmin?: boolean;
  isTenantAdmin?: boolean;
  grants?: Json[];
  hasPermission?: { resourceType: string; operation: string; permitted: boolean }[];
}

export interface AuthSession extends Entity {
  tenantId?: string;
  userId?: string;
  credentialId?: string;
  principalType?: PrincipalType;
  authScheme?: AuthScheme;
  sourceIp?: string;
  userAgent?: string;
  expiresUtc?: string;
  lastUsedUtc?: string;
  revokedUtc?: string;
  revocationReason?: string;
  reauthenticatedUtc?: string;
}

// ---------------------------------------------------------------------------------------------------------------
// Audit and request history
// ---------------------------------------------------------------------------------------------------------------

export interface AuditRecord extends Entity {
  tenantId?: string;
  eventType: string;
  requestId?: string;
  correlationId?: string;
  traceId?: string;
  authScheme?: AuthScheme;
  principalType?: PrincipalType;
  principalId?: string;
  principalName?: string;
  sessionId?: string;
  resourceType?: string;
  resourceId?: string;
  operation?: string;
  method?: string;
  path?: string;
  sourceIp?: string;
  authResult?: string;
  authorizationResult?: string;
  reason?: string;
  statusCode?: number;
  description?: string;
}

export interface AuditFilter {
  eventType?: string;
  principalId?: string;
  result?: string;
  fromUtc?: Date | string;
  toUtc?: Date | string;
  pageNumber?: number;
  pageSize?: number;
}

export interface RequestHistoryHeader extends Json {
  id?: string;
  requestHistoryEntryId?: string;
  direction: "Request" | "Response";
  name: string;
  value: string;
}

export interface RequestHistoryEntry extends Json {
  id: string;
  tenantId?: string;
  userId?: string;
  principalName?: string;
  method: string;
  path: string;
  url?: string;
  statusCode: number;
  durationMs?: number;
  sourceIp?: string;
  traceId?: string;
  correlationId?: string;
  requestBody?: string;
  requestBodyBytes?: number;
  requestBodyTruncated?: boolean;
  responseBody?: string;
  responseBodyBytes?: number;
  responseBodyTruncated?: boolean;
  createdUtc?: string;
  completedUtc?: string;
  headers?: RequestHistoryHeader[];
}

export interface RequestHistoryFilter {
  method?: string;
  statusCode?: number;
  pathContains?: string;
  fromUtc?: Date | string;
  toUtc?: Date | string;
  pageNumber?: number;
  pageSize?: number;
  /** System administrators only. */
  tenantId?: string;
  userId?: string;
}

export interface RequestHistorySummary extends Json {
  totalCount: number;
  totalSuccess: number;
  totalFailure: number;
  averageDurationMs: number;
  buckets: { bucketStartUtc: string; bucketEndUtc: string; successCount: number; failureCount: number; averageDurationMs: number }[];
}

// ---------------------------------------------------------------------------------------------------------------
// Secrets
// ---------------------------------------------------------------------------------------------------------------

export type SecretKind = "Password" | "PrivateKey" | "ApiToken" | "AccessKeyPair" | "ServiceAccountJson" | "Kubeconfig" | "ConnectionString" | "Certificate" | "Generic" | "BearerToken";

export interface SecretReferrer extends Json {
  referrerType: string;
  referrerId: string;
  referrerName?: string;
  detail?: string;
}

/** Secret metadata. Values are never returned except by reveal. */
export interface Secret extends Entity {
  tenantId: string;
  ownerUserId?: string;
  name: string;
  kind: SecretKind;
  description?: string;
  keyVersion?: number;
  fingerprint?: string;
  lastRotatedUtc?: string;
  lastRevealedUtc?: string;
  expiresUtc?: string;
  fields?: string[];
  referrers?: SecretReferrer[];
}

export interface SecretRequest extends Json {
  name?: string;
  description?: string;
  kind?: SecretKind;
  /** Field name to plaintext value, for example { password: "..." } or { accessKeyId, secretAccessKey }. */
  values?: Record<string, string>;
  expiresUtc?: string;
}

export interface SecretValueRequest extends Json {
  values?: Record<string, string>;
  remove?: string[];
}

export interface SecretRevealResponse extends Json {
  secretId: string;
  name: string;
  kind: SecretKind;
  values: Record<string, string>;
  revealedUtc: string;
}

export interface SecretImportRequest extends Json {
  content: string;
  passphrase?: string;
  fileName?: string;
}

export interface SecretImportResult extends Json {
  kind?: SecretKind;
  format?: string;
  suggestedField?: string;
  fields?: string[];
  fingerprint?: string;
  keyType?: string;
  keySizeBits?: number;
  encrypted?: boolean;
  warnings?: string[];
}

// ---------------------------------------------------------------------------------------------------------------
// Prompt templates
// ---------------------------------------------------------------------------------------------------------------

export type PromptTemplateKind = "System" | "Task" | "Fragment" | "ToolDescription" | "ConfigFile" | "Other";

export interface PromptTemplateVariable extends Json {
  id?: string;
  promptTemplateId?: string;
  name: string;
  description?: string;
  required?: boolean;
  defaultValue?: string;
}

export interface PromptTemplate extends Entity {
  tenantId?: string;
  name: string;
  description?: string;
  kind?: PromptTemplateKind;
  content: string;
  currentVersion?: number;
  isBuiltIn?: boolean;
  builtInKey?: string;
  variables?: PromptTemplateVariable[];
}

export interface PromptTemplateRequest extends Json {
  name?: string;
  description?: string;
  kind?: PromptTemplateKind;
  content?: string;
  variables?: PromptTemplateVariable[];
  changeNote?: string;
}

export interface PromptTemplateVersion extends Entity {
  tenantId?: string;
  templateId: string;
  version: number;
  content: string;
  changeNote?: string;
  createdBy?: string;
}

export interface PromptRenderRequest extends Json {
  content?: string;
  context?: Json;
}

export interface PromptRenderResponse extends Json {
  rendered: string;
  success: boolean;
  variables?: string[];
  includes?: string[];
  errors?: string[];
}

// ---------------------------------------------------------------------------------------------------------------
// Settings and system
// ---------------------------------------------------------------------------------------------------------------

export interface SettingField extends Json {
  path: string;
  group?: string;
  description?: string;
  type?: string;
  value?: string;
  isSecret?: boolean;
  isSet?: boolean;
  source?: string;
  applyMode?: string;
  editable?: boolean;
}

export interface SettingsView extends Json {
  settingsFile?: string;
  settingsFileWritable?: boolean;
  restartPending?: boolean;
  fields: SettingField[];
}

export interface SettingsSaveResponse extends Json {
  saved: boolean;
  restartRequired: boolean;
  appliedLive?: string[];
  pendingRestart?: string[];
  settings?: SettingsView;
}

export interface ObservabilityServiceStatus extends Json {
  service: string;
  configured: boolean;
  reachable: boolean;
  version?: string;
  checkedUtc?: string;
}

export interface MasterKeyRotationStatus extends Json {
  running: boolean;
  targetKeyVersion?: number;
  total?: number;
  completed?: number;
  failed?: number;
  remaining?: number;
  startedUtc?: string;
  completedUtc?: string;
  lastError?: string;
}

// ---------------------------------------------------------------------------------------------------------------
// Domain (conversations and chat are preview: verified when the Phase 7 routes land)
// ---------------------------------------------------------------------------------------------------------------

export type AccessMode = "ReadOnly" | "ReadWriteWithApproval" | "ReadWrite";
export type Effect = "Read" | "Write" | "Destructive";
export type Visibility = "Private" | "Tenant";
export type ConnectorCategory = "Compute" | "Storage" | "Database" | "Logging" | "Cache" | "Cloud" | "Orchestration" | "Observability" | "Http" | "Agent" | "Other";
export type SettingValueKind = "String" | "Text" | "Integer" | "Boolean" | "StringList" | "Enum";
export type InstructionMode = "Inherit" | "Append" | "Replace";
export type ToolRule = "Allow" | "Deny" | "RequireApproval";
export type InvocationDecision = "Allowed" | "Denied" | "ApprovalRequired" | "Approved" | "Rejected" | "Expired" | "Cancelled";
export type InvocationStatus = "Pending" | "Running" | "Succeeded" | "Failed" | "TimedOut" | "Cancelled" | "NotExecuted";
export type InvocationSurface = "Chat" | "Mcp" | "A2A" | "OpenAI" | "Rest";
export type ApprovalStatus = "Pending" | "Approved" | "Rejected" | "Expired" | "Cancelled";
export type ModelKind = "Inference" | "Embedding";
export type ModelProvider = "Ollama" | "OpenAI" | "OpenAICompatible" | "Anthropic" | "Gemini" | "AzureOpenAI" | "Bedrock" | "VertexAI" | "VoyageAI";
export type KnowledgeSource = "Operator" | "Generated" | "Agent" | "Derived";

export interface SettingDescriptor extends Json {
  key: string;
  label?: string;
  help?: string;
  kind?: SettingValueKind;
  required?: boolean;
  defaultValue?: string;
  minimum?: number;
  maximum?: number;
  allowedValues?: string[];
  pattern?: string;
  group?: string;
}

export interface SecretSlotDescriptor extends Json {
  name: string;
  label?: string;
  help?: string;
  acceptedKinds?: SecretKind[];
  required?: boolean;
}

export interface ConnectorTool extends Json {
  name: string;
  title?: string;
  description?: string;
  inputSchemaJson?: string;
  baseEffect?: Effect;
  classifiedPerCall?: boolean;
  updatesKnowledge?: boolean;
  requiresModel?: boolean;
  outputDescription?: string;
}

export interface Connector extends Json {
  type: string;
  family?: string;
  displayName?: string;
  description?: string;
  category?: ConnectorCategory;
  iconKey?: string;
  version?: string;
  documentationUrl?: string;
  defaultInstructionsKey?: string;
  plugin?: boolean;
  settings?: SettingDescriptor[];
  secretSlots?: SecretSlotDescriptor[];
  tools?: ConnectorTool[];
}

export interface ResourceSetting extends Json {
  key: string;
  valueKind?: SettingValueKind;
  value?: string;
}

export interface ResourceSecretBinding extends Json {
  id?: string;
  slotName: string;
  secretId: string;
  secretFieldName?: string;
  secretName?: string;
}

export interface Resource extends Entity {
  tenantId: string;
  ownerUserId?: string;
  visibility?: Visibility;
  name: string;
  connectorType: string;
  displayName?: string;
  description?: string;
  endpoint?: string;
  environment?: string;
  notes?: string;
  defaultAccessModeCeiling?: AccessMode;
  lastTestUtc?: string;
  lastTestResult?: boolean;
  lastTestMessage?: string;
  settings?: ResourceSetting[];
  secretBindings?: ResourceSecretBinding[];
}

export interface ResourceRequest extends Json {
  name?: string;
  connectorType?: string;
  displayName?: string;
  description?: string;
  endpoint?: string;
  environment?: string;
  notes?: string;
  visibility?: Visibility;
  defaultAccessModeCeiling?: AccessMode;
  clearAccessModeCeiling?: boolean;
  active?: boolean;
  /** Setting key to value (string, number, boolean, or string list). */
  settings?: Record<string, unknown>;
  secretBindings?: ResourceSecretBinding[];
}

export interface ConnectorValidationError extends Json {
  field: string;
  code: string;
  message: string;
}

export interface ResourceTestResponse extends Json {
  success: boolean;
  message?: string;
  errorCode?: string;
  latencyMs?: number;
  details?: Record<string, string>;
  validationErrors?: ConnectorValidationError[];
  /** Settings the test discovered (for example a server version), keyed by setting key. */
  learnedSettings?: Record<string, string>;
}

/** Alias matching the C# and Python SDK name. */
export type ResourceTestResult = ResourceTestResponse;

/** Filter for resources.list. */
export interface ResourceFilter extends EnumerationQuery {
  connectorType?: string;
}

export interface ResourceReferrer extends Json {
  agentProfileId: string;
  agentProfileName?: string;
  bindingId: string;
  alias?: string;
  accessMode?: AccessMode;
  enabled?: boolean;
}

export interface ResourceKnowledge extends Json {
  id?: string;
  tenantId?: string;
  resourceId: string;
  scope: string;
  key: string;
  content: string;
  source?: KnowledgeSource;
  updatedBy?: string;
  version?: number;
  createdUtc?: string;
  lastUpdateUtc?: string;
}

export interface ResourceKnowledgeVersion extends ResourceKnowledge {
  knowledgeId?: string;
  deleted?: boolean;
}

export interface KnowledgeRequest extends Json {
  content: string;
  source?: KnowledgeSource;
}

export interface AgentProfile extends Entity {
  tenantId: string;
  ownerUserId?: string;
  visibility?: Visibility;
  name: string;
  displayName?: string;
  personaName?: string;
  userAddress?: string;
  description?: string;
  personaTemplateKey?: string;
  inferenceEndpointId?: string;
  fallbackInferenceEndpointId?: string;
  embeddingEndpointId?: string;
  maxIterations?: number;
  maxToolCallsPerTurn?: number;
  toolTimeoutSeconds?: number;
  temperature?: number;
  exposeA2A?: boolean;
  exposeMcp?: boolean;
  exposeOpenAI?: boolean;
  exposeManagementTools?: boolean;
  instructionsAppend?: string;
  isDefault?: boolean;
}

export type AgentProfileRequest = Partial<Omit<AgentProfile, "id" | "tenantId" | "createdUtc" | "lastUpdateUtc">> & Json;

export interface BindingToolRule extends Json {
  id?: string;
  bindingId?: string;
  toolName: string;
  rule: ToolRule;
}

export interface Binding extends Json {
  id: string;
  tenantId?: string;
  agentProfileId: string;
  resourceId: string;
  alias: string;
  accessMode: AccessMode;
  requireApprovalForDestructive?: boolean;
  instructionMode?: InstructionMode;
  instructionText?: string;
  maxOutputBytes?: number;
  maxRows?: number;
  enabled?: boolean;
  allowKnowledgeUpdates?: boolean;
  toolRules?: BindingToolRule[];
  denyPatterns?: string[];
  resourceName?: string;
  connectorType?: string;
}

export interface BindingRequest extends Json {
  resourceId?: string;
  alias?: string;
  accessMode?: AccessMode;
  requireApprovalForDestructive?: boolean;
  instructionMode?: InstructionMode;
  instructionText?: string;
  maxOutputBytes?: number;
  maxRows?: number;
  enabled?: boolean;
  allowKnowledgeUpdates?: boolean;
  toolRules?: BindingToolRule[];
  denyPatterns?: string[];
}

export interface CompiledInstructions extends Json {
  profileId: string;
  profileName?: string;
  surface?: InvocationSurface;
  text: string;
  tokenEstimate?: number;
  bindingCount?: number;
  segments?: Json[];
  warnings?: string[];
  fromCache?: boolean;
  compiledUtc?: string;
}

export interface CallerTool extends ConnectorTool {
  family?: string;
  knownEffect?: Effect;
  isMetaTool?: boolean;
  resources?: { alias: string; connectorType?: string; accessMode?: AccessMode; rule?: string }[];
}

export interface CallerToolCatalog extends Json {
  profileId: string;
  profileName?: string;
  hasModel?: boolean;
  resourceCount?: number;
  tools?: CallerTool[];
  metaTools?: CallerTool[];
}

export interface AgentCardSkill extends Json {
  id: string;
  name: string;
  description?: string;
  tags?: string[];
  examples?: string[];
}

export interface AgentCardPreview extends Json {
  name?: string;
  description?: string;
  url?: string;
  version?: string;
  skills?: AgentCardSkill[];
}

export type PromptLayer = "BuiltIn" | "Global" | "Tenant";

/** One editable layer (Global or Tenant) of a prompt template key. */
export interface PromptLayerState extends Json {
  layer: PromptLayer;
  exists: boolean;
  templateId?: string;
  content?: string;
  version?: number;
  customized?: boolean;
  defaultUpdatedSinceCustomized?: boolean;
  lastUpdateUtc?: string;
}

/** A prompt template key with its layers and the layer in effect. */
export interface PromptTemplateView extends Json {
  key: string;
  description?: string;
  source?: string;
  activeLayer: PromptLayer;
  effectiveContent: string;
  builtInContent?: string;
  builtInHash?: string;
  global?: PromptLayerState | null;
  tenant?: PromptLayerState | null;
}

export interface BuiltInTemplate extends Json {
  key: string;
  content: string;
  hash?: string;
  source?: string;
  description?: string;
}

export interface PromptDiffLine extends Json {
  /** "add", "remove", or "same". */
  op: string;
  text: string;
}

export interface ModelEndpoint extends Entity {
  tenantId: string;
  name: string;
  kind: ModelKind;
  provider: ModelProvider;
  baseUrl?: string;
  model: string;
  apiKeySecretId?: string;
  apiKeySecretField?: string;
  region?: string;
  deploymentName?: string;
  apiVersion?: string;
  contextWindowTokens?: number;
  maxOutputTokens?: number;
  supportsTools?: boolean;
  supportsStreaming?: boolean;
  embeddingDimensions?: number;
  maxConcurrency?: number;
  timeoutSeconds?: number;
  maxRetries?: number;
  healthStatus?: string;
  lastHealthUtc?: string;
  lastHealthMessage?: string;
}

export type ModelEndpointRequest = Partial<Omit<ModelEndpoint, "id" | "tenantId" | "createdUtc" | "lastUpdateUtc">> & Json;

export interface ModelValidationResult extends Json {
  success: boolean;
  kind?: ModelKind;
  latencyMs?: number;
  dimensions?: number;
  model?: string;
  responsePreview?: string;
  statusCode?: number;
  error?: string;
  problems?: string[];
}

export interface ToolCallRequest extends Json {
  /** Agent profile ID; or name via profile. */
  profileId?: string;
  profile?: string;
  /** Binding alias (with a profile) or resource name. */
  resource?: string;
  resourceId?: string;
  toolName: string;
  arguments?: Json;
}

export interface ToolResult extends Json {
  isError?: boolean;
  errorCode?: string;
  text?: string;
  structuredJson?: string;
  mimeType?: string;
  truncated?: boolean;
  originalBytes?: number;
}

export interface ToolInvocationOutcome extends Json {
  invocationId?: string;
  dryRun?: boolean;
  toolName: string;
  profileId?: string;
  bindingAlias?: string;
  resourceId?: string;
  connectorType?: string;
  decision: InvocationDecision;
  decisionCode?: string;
  reason?: string;
  effect?: Effect;
  effectReason?: string;
  accessMode?: AccessMode;
  approvalId?: string;
  approvalStatus?: ApprovalStatus;
  approvalSummary?: string;
  approvalExpiresUtc?: string;
  status?: InvocationStatus;
  result?: ToolResult;
  durationMs?: number;
}

export interface ToolInvocation extends Json {
  id: string;
  tenantId?: string;
  conversationId?: string;
  surface?: InvocationSurface;
  principalType?: PrincipalType;
  principalId?: string;
  agentProfileId?: string;
  bindingId?: string;
  bindingAlias?: string;
  resourceId?: string;
  connectorType?: string;
  toolName: string;
  argumentsRedacted?: string;
  effect?: Effect;
  effectReason?: string;
  accessMode?: AccessMode;
  decision?: InvocationDecision;
  approvalId?: string;
  status?: InvocationStatus;
  errorCode?: string;
  errorMessage?: string;
  outputBytes?: number;
  outputTruncated?: boolean;
  outputPreview?: string;
  durationMs?: number;
  createdUtc?: string;
}

export interface ToolInvocationFilter extends EnumerationQuery {
  agentProfileId?: string;
  resourceId?: string;
  toolName?: string;
  decision?: InvocationDecision;
  status?: InvocationStatus;
  surface?: InvocationSurface;
  conversationId?: string;
  principalId?: string;
  since?: string;
}

export interface ToolInvocationSummary extends Json {
  total: number;
  sinceUtc?: string;
  byDecision?: { status: string; count: number }[];
  byStatus?: { status: string; count: number }[];
  byEffect?: { status: string; count: number }[];
  byTool?: { status: string; count: number }[];
}

export interface Approval extends Json {
  id: string;
  tenantId?: string;
  toolInvocationId?: string;
  agentProfileId?: string;
  bindingId?: string;
  bindingAlias?: string;
  resourceId?: string;
  toolName?: string;
  surface?: InvocationSurface;
  requestedByPrincipalType?: PrincipalType;
  requestedByPrincipalId?: string;
  requestedByName?: string;
  summary: string;
  effect?: Effect;
  effectReason?: string;
  argumentsRedacted?: string;
  status: ApprovalStatus;
  decidedByUserId?: string;
  decidedByName?: string;
  decisionNote?: string;
  decidedUtc?: string;
  expiresUtc?: string;
  invocation?: ToolInvocation;
  createdUtc?: string;
}

export interface ApprovalFilter extends EnumerationQuery {
  status?: ApprovalStatus;
  agentProfileId?: string;
  resourceId?: string;
  principalId?: string;
  toolName?: string;
}

export interface LockdownState extends Json {
  tenantId: string;
  lockdown: boolean;
  approvalsCancelled?: number;
}

export type ConversationSource = "Dashboard" | "A2A" | "OpenAI" | "Mcp" | string;
export type MessageRole = "System" | "User" | "Assistant" | "Tool";

export interface Conversation extends Entity {
  tenantId?: string;
  userId?: string;
  credentialId?: string;
  agentProfileId?: string;
  title?: string;
  source?: ConversationSource;
  messageCount?: number;
  lastMessageUtc?: string;
  totalInputTokens?: number;
  totalOutputTokens?: number;
}

export interface ConversationRequest extends Json {
  agentProfileId?: string;
  title?: string;
}

export interface Message extends Json {
  id: string;
  conversationId: string;
  role: MessageRole;
  content?: string;
  thinkingContent?: string;
  sequence?: number;
  modelEndpointId?: string;
  inputTokens?: number;
  outputTokens?: number;
  timeToFirstTokenMs?: number;
  durationMs?: number;
  finishReason?: string;
  createdUtc?: string;
}

export interface SendMessageRequest extends Json {
  content: string;
}

/** Chat stream event types emitted by POST /conversations/{id}/messages with Accept: text/event-stream. */
export type ChatEventType =
  | "turn.started"
  | "assistant.delta"
  | "assistant.thinking"
  | "tool.proposed"
  | "tool.started"
  | "tool.completed"
  | "approval.required"
  | "approval.resolved"
  | "turn.completed"
  | "error";

/** One chat stream event. payload is the parsed JSON data, when the data is JSON. */
export interface ChatEvent {
  type: ChatEventType | string;
  id?: string;
  data: string;
  payload?: Json;
}
