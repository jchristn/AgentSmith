// Application-wide constants for the Hugo dashboard.

/** Product version. Mirrors src/Directory.Build.props and package.json; change all three together. */
export const APP_VERSION = '0.1.0-alpha';

export const STORAGE_KEYS = {
  serverUrl: 'hugo_server_url',
  token: 'hugo_token',
  tenant: 'hugo_tenant',
  theme: 'hugo_theme',
  locale: 'hugo.locale',
  pageSize: 'hugo_page_size',
  columns: 'hugo_columns',
  apiExplorerHistory: 'hugo_api_explorer_history',
  setupDismissed: 'hugo_setup_dismissed',
  tourDone: 'hugo_tour_done',
  sidebarCollapsed: 'hugo_sidebar_collapsed',
  explainers: 'hugo_explainers_hidden',
  chatDrafts: 'hugo_chat_drafts',
  chatProfile: 'hugo_chat_profile',
  chatDrawer: 'hugo_chat_drawer'
};

export const GITHUB_URL = 'https://github.com/jchristn/AgentSmith';

/** Local development API URL (the Hugo server's default listener). */
export const LOCAL_SERVER_URL = 'http://127.0.0.1:8800';

/** Default API URL: runtime config from /config.js, else the page origin (nginx or Vite proxy), else local dev. */
export function defaultServerUrl() {
  const configured = typeof window !== 'undefined' ? window.HUGO_CONFIG?.apiUrl : '';
  if (configured) return configured;
  if (typeof window !== 'undefined' && window.location?.origin && window.location.origin.startsWith('http')) {
    return window.location.origin;
  }
  return LOCAL_SERVER_URL;
}

// Development defaults pre-filled into the login form (plan section 17.3 seeding).
export const DEV_DEFAULTS = {
  tenantId: 'default',
  email: 'admin@hugo.local',
  password: 'password',
  operatorEmail: 'anderson@hugo.local'
};

export const PAGE_SIZE_OPTIONS = [10, 25, 50, 100];
export const LARGE_PAGE_SIZE_OPTIONS = [10, 25, 50, 100, 250, 500, 1000];
export const DEFAULT_PAGE_SIZE = 25;

// Auto-refresh interval options, in seconds. 0 disables.
export const AUTO_REFRESH_OPTIONS = [0, 5, 15, 30, 60, 120, 300];
export const DEFAULT_AUTO_REFRESH = 15;

export const HTTP_METHODS = ['GET', 'POST', 'PUT', 'DELETE', 'PATCH', 'HEAD'];

/** Seconds a revealed secret value stays on screen before it is hidden again (plan P8.32). */
export const REVEAL_VISIBLE_SECONDS = 30;

/** How often the sidebar badge re-reads the pending approval count, in milliseconds, when no live event arrives. */
export const APPROVAL_POLL_MS = 30000;

/** Polling interval for the event stream fallback, in milliseconds. */
export const EVENT_POLL_MS = 15000;

// Enumerations mirrored from the Hugo server (string values on the wire). Display labels live in enums.<group>.<value>.
export const ENUMS = {
  // Identity and RBAC (forked from Garrison, plan section 15.3).
  principalType: ['Administrator', 'User', 'Credential'],
  credentialAuthMode: ['DirectHeader', 'SignedRequest', 'SessionExchange', 'Hybrid'],
  authScheme: ['BearerToken', 'XToken', 'PasswordHeaders', 'AccessKeySecret', 'AccessKeySignature', 'AdminApiKey'],
  permissionType: ['Permit', 'Deny'],
  resourceScope: ['Tenant', 'Resource'],
  resourceType: [
    'All',
    'Tenant',
    'Admin',
    'User',
    'Credential',
    'Session',
    'Role',
    'Permission',
    'Assignment',
    'Audit',
    'Settings',
    'Secret',
    'Resource',
    'Connector',
    'AgentProfile',
    'Binding',
    'PromptTemplate',
    'ModelEndpoint',
    'Conversation',
    'ToolInvocation',
    'Approval',
    'RequestHistory',
    'Knowledge',
    'Plugin',
    'Analytics'
  ],
  operationType: ['All', 'Create', 'Read', 'Update', 'Delete', 'Write', 'Execute', 'Admin', 'Reveal'],
  auditEventType: [
    'AuthSuccess',
    'AuthFailure',
    'SessionIssued',
    'SessionRevoked',
    'SessionReauthenticated',
    'CredentialCreated',
    'CredentialRotated',
    'CredentialUsed',
    'AuthorizationDenied',
    'AuthorizationBypass',
    'RoleChanged',
    'PermissionChanged',
    'AssignmentChanged',
    'ConfigurationChanged',
    'PasswordChanged',
    'SecretCreated',
    'SecretUpdated',
    'SecretRevealed',
    'SecretDeleted',
    'MasterKeyRotated',
    'ResourceChanged',
    'ProfileChanged',
    'BindingChanged',
    'PromptTemplateChanged',
    'ApprovalDecided',
    'LockdownChanged',
    'SettingsChanged',
    'RestartRequested',
    'ServerRestarted',
    'KnowledgeChanged',
    'ModelEndpointChanged',
    'PluginLoaded',
    'HostKeyPinned',
    'HostKeyMismatch',
    'SettingLearned'
  ],
  // Connector contract (src/Hugo.Connectors.Abstractions).
  secretKind: ['Password', 'PrivateKey', 'ApiToken', 'AccessKeyPair', 'ServiceAccountJson', 'Kubeconfig', 'ConnectionString', 'Certificate', 'Generic'],
  effect: ['Read', 'Write', 'Destructive'],
  accessMode: ['ReadOnly', 'ReadWriteWithApproval', 'ReadWrite'],
  connectorCategory: ['Compute', 'Storage', 'Database', 'Logging', 'Cache', 'Cloud', 'Orchestration', 'Observability', 'Http', 'Agent', 'Other'],
  settingValueKind: ['String', 'Text', 'Integer', 'Boolean', 'StringList', 'Enum'],
  // Runtime entities (plan section 7.5).
  approvalStatus: ['Pending', 'Approved', 'Rejected', 'Expired', 'Cancelled'],
  toolDecision: ['Allowed', 'Denied', 'ApprovalRequired', 'Approved', 'Rejected', 'Expired', 'Cancelled'],
  toolStatus: ['Pending', 'Running', 'Succeeded', 'Failed', 'TimedOut', 'Cancelled', 'NotExecuted'],
  surface: ['Chat', 'Mcp', 'A2A', 'OpenAI', 'Rest'],
  conversationSource: ['Dashboard', 'A2A', 'OpenAI', 'Mcp'],
  visibility: ['Private', 'Tenant'],
  instructionMode: ['Inherit', 'Append', 'Replace'],
  toolRule: ['Allow', 'Deny', 'RequireApproval'],
  modelKind: ['Inference', 'Embedding'],
  modelProvider: ['Ollama', 'OpenAI', 'OpenAICompatible', 'Anthropic', 'Gemini', 'AzureOpenAI', 'Bedrock', 'VertexAI', 'VoyageAI'],
  healthStatus: ['Healthy', 'Degraded', 'Unhealthy', 'Unknown'],
  promptLayer: ['BuiltIn', 'Global', 'Tenant'],
  knowledgeSource: ['Operator', 'Generated', 'Agent', 'Derived'],
  promptTemplateKind: ['System', 'Task', 'Fragment', 'ToolDescription', 'ConfigFile', 'Other']
};

/** Semantic tone for an approval status pill: Pending needs a person, Approved and Rejected are final verdicts. */
export function approvalStatusTone(status) {
  switch (status) {
    case 'Pending':
      return 'warning';
    case 'Approved':
      return 'success';
    case 'Rejected':
      return 'danger';
    default:
      return 'neutral';
  }
}

/** Semantic tone for a tool call effect. Read is neutral, Write is amber, Destructive is red (plan section 18.4). */
export function effectTone(effect) {
  switch (effect) {
    case 'Destructive':
      return 'danger';
    case 'Write':
      return 'warning';
    default:
      return 'neutral';
  }
}

/** Semantic tone for a tool invocation status. */
export function toolStatusTone(status) {
  switch (status) {
    case 'Succeeded':
      return 'success';
    case 'Failed':
    case 'TimedOut':
      return 'danger';
    case 'Cancelled':
      return 'warning';
    case 'NotExecuted':
      return 'neutral';
    default:
      return 'info';
  }
}

/** Semantic tone for a tool decision. */
export function toolDecisionTone(decision) {
  switch (decision) {
    case 'Allowed':
    case 'Approved':
      return 'success';
    case 'Denied':
    case 'Rejected':
      return 'danger';
    case 'ApprovalRequired':
    case 'Expired':
      return 'warning';
    default:
      return 'neutral';
  }
}

/** Semantic tone for a resource or model endpoint health status. */
export function healthTone(status) {
  switch (status) {
    case 'Healthy':
      return 'success';
    case 'Degraded':
      return 'warning';
    case 'Unhealthy':
      return 'danger';
    default:
      return 'neutral';
  }
}

/** Semantic tone for a binding access mode. */
export function accessModeTone(mode) {
  switch (mode) {
    case 'ReadWrite':
      return 'warning';
    case 'ReadWriteWithApproval':
      return 'info';
    default:
      return 'neutral';
  }
}

/**
 * Chart range presets. Bucket sizes and counts are exact: Last Hour 60 x 1 minute, Last Day 96 x 15 minutes,
 * Last Week 84 x 2 hours, Last Month 120 x 6 hours.
 */
export const REQUEST_RANGES = {
  hour: { id: 'hour', bucketMinutes: 1, bucketCount: 60, windowMs: 60 * 60 * 1000 },
  day: { id: 'day', bucketMinutes: 15, bucketCount: 96, windowMs: 24 * 60 * 60 * 1000 },
  week: { id: 'week', bucketMinutes: 120, bucketCount: 84, windowMs: 7 * 24 * 60 * 60 * 1000 },
  month: { id: 'month', bucketMinutes: 360, bucketCount: 120, windowMs: 30 * 24 * 60 * 60 * 1000 }
};

/** Compute { fromUtc, toUtc, bucketMinutes } for a chart range preset. */
export function requestRangeToParams(rangeId, now = new Date()) {
  const range = REQUEST_RANGES[rangeId] || REQUEST_RANGES.day;
  const from = new Date(now.getTime() - range.windowMs);
  return { fromUtc: from.toISOString(), toUtc: now.toISOString(), bucketMinutes: range.bucketMinutes };
}

/** Chat stream event names (plan decision D-022). */
export const CHAT_EVENTS = [
  'turn.started',
  'assistant.delta',
  'assistant.thinking',
  'tool.proposed',
  'tool.started',
  'tool.completed',
  'approval.required',
  'approval.resolved',
  'turn.completed',
  'error'
];

/** Server event stream names on GET /v1.0/api/events (plan section 14.2). */
export const SERVER_EVENTS = [
  'approval.created',
  'approval.resolved',
  'tool.invoked',
  'resource.health',
  'model.health',
  'settings.restartRequired',
  'lockdown.changed'
];
