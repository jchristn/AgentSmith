// Deterministic Hugo API mocks so the Playwright suite runs without a server.
//
// Identity and administration routes follow the Garrison shapes Hugo forks (tenant-scoped paths, enumeration
// envelopes with objects and totalRecords). Hugo's own routes follow plan section 14.2 and answer page results
// ({ items, pageNumber, pageSize, totalCount, totalPages }). `installMocks(page, { missing })` answers 404 for the named
// Hugo capabilities, so tests can check the "server does not expose this yet" states.

const NOW = Date.parse('2026-10-14T12:00:00Z');
const iso = (offsetMinutes) => new Date(NOW - offsetMinutes * 60000).toISOString();
const TENANT = 'default';
const pad = (i) => String(i).padStart(4, '0');

export const PRINCIPAL = {
  principalType: 'User',
  principalId: 'usr_admin',
  principalName: 'admin@hugo.local',
  tenantId: TENANT,
  userId: 'usr_admin',
  sessionId: 'ses_e2e',
  authScheme: 'BearerToken',
  isAdmin: true,
  isTenantAdmin: true,
  email: 'admin@hugo.local',
  expiresUtc: iso(-60)
};

function record(prefix, i, extra = {}) {
  return { id: `${prefix}_${pad(i)}`, ...extra, active: true, createdUtc: iso(i * 7), lastUpdateUtc: iso(i * 3) };
}

/** The SSH connector descriptor as Hugo.Connectors.Abstractions serializes it (camelCase, string enums). */
export const SSH_CONNECTOR = {
  type: 'ssh',
  family: 'ssh',
  displayName: 'SSH host',
  description: 'Run commands and read files on a Linux host over SSH.',
  category: 'Compute',
  iconKey: 'terminal',
  version: '1.0.0',
  settingsSchema: {
    settings: [
      {
        key: 'host',
        label: 'Host',
        help: 'Hostname or IP address.',
        kind: 'String',
        required: true,
        defaultValue: null,
        minimum: null,
        maximum: null,
        allowedValues: [],
        pattern: null,
        group: 'Connection',
        showInSummary: true
      },
      {
        key: 'port',
        label: 'Port',
        help: 'SSH port.',
        kind: 'Integer',
        required: true,
        defaultValue: '22',
        minimum: 1,
        maximum: 65535,
        allowedValues: [],
        pattern: null,
        group: 'Connection',
        showInSummary: true
      },
      {
        key: 'username',
        label: 'Username',
        help: 'Account to sign in as.',
        kind: 'String',
        required: true,
        defaultValue: null,
        minimum: null,
        maximum: null,
        allowedValues: [],
        pattern: null,
        group: 'Authentication',
        showInSummary: false
      },
      {
        key: 'hostKeyFingerprint',
        label: 'Host key fingerprint',
        help: 'Leave empty to trust on first use.',
        kind: 'String',
        required: false,
        defaultValue: null,
        minimum: null,
        maximum: null,
        allowedValues: [],
        pattern: null,
        group: 'Authentication',
        showInSummary: false
      },
      {
        key: 'allowedPaths',
        label: 'Allowed paths',
        help: 'Paths file tools may read.',
        kind: 'StringList',
        required: false,
        defaultValue: '/var/log',
        minimum: null,
        maximum: null,
        allowedValues: [],
        pattern: null,
        group: 'Limits',
        showInSummary: false
      },
      {
        key: 'shell',
        label: 'Shell',
        help: '',
        kind: 'Enum',
        required: false,
        defaultValue: 'bash',
        minimum: null,
        maximum: null,
        allowedValues: ['bash', 'sh'],
        pattern: null,
        group: 'Advanced',
        showInSummary: false
      },
      {
        key: 'useSudo',
        label: 'Allow sudo',
        help: '',
        kind: 'Boolean',
        required: false,
        defaultValue: 'false',
        minimum: null,
        maximum: null,
        allowedValues: [],
        pattern: null,
        group: 'Advanced',
        showInSummary: false
      }
    ]
  },
  secretSlots: [{ name: 'privateKey', label: 'Private key', help: 'SSH private key for the account.', acceptedKinds: ['PrivateKey'], required: true }],
  defaultInstructionsKey: 'connector.ssh.instructions',
  defaultInstructions: 'You can run commands on {{resource.alias}} ({{resource.endpoint}}). Prefer read-only commands.',
  tools: [
    { name: 'ssh_exec', title: 'Run a command', description: 'Run a shell command.', baseEffect: 'Read', classifiedPerCall: true },
    { name: 'ssh_read_file', title: 'Read a file', description: 'Read a file within the allowed paths.', baseEffect: 'Read', classifiedPerCall: false }
  ]
};

const CONNECTORS = [
  SSH_CONNECTOR,
  {
    ...SSH_CONNECTOR,
    type: 'postgresql',
    family: 'sql',
    displayName: 'PostgreSQL',
    description: 'Query a PostgreSQL database.',
    category: 'Database',
    secretSlots: [{ name: 'password', label: 'Password', acceptedKinds: ['Password', 'ConnectionString'], required: true }],
    tools: [{ name: 'sql_query', description: 'Run a query.', baseEffect: 'Read', classifiedPerCall: true }]
  },
  {
    ...SSH_CONNECTOR,
    type: 's3',
    family: 'storage',
    displayName: 'Amazon S3',
    description: 'List and read objects in a bucket.',
    category: 'Storage',
    secretSlots: [{ name: 'accessKey', label: 'Access key', acceptedKinds: ['AccessKeyPair'], required: true }],
    tools: [
      { name: 'storage_list', description: 'List objects.', baseEffect: 'Read' },
      { name: 'storage_delete', description: 'Delete an object.', baseEffect: 'Destructive' }
    ]
  },
  {
    ...SSH_CONNECTOR,
    type: 'papertrail',
    family: 'logs',
    displayName: 'Papertrail',
    description: 'Search logs.',
    category: 'Logging',
    tools: [{ name: 'logs_search', description: 'Search logs.', baseEffect: 'Read' }]
  }
];

const FACTORIES = {
  // Garrison-forked administration records.
  tenants: (i) =>
    record('ten', i, { id: i === 1 ? TENANT : `ten_${i}`, name: i === 1 ? 'default' : `tenant-${i}`, description: 'Sample tenant', lockdown: i === 3 }),
  users: (i) =>
    record('usr', i, {
      tenantId: TENANT,
      firstName: i === 2 ? 'Thomas' : 'User',
      lastName: i === 2 ? 'Anderson' : String(i),
      email: i === 2 ? 'anderson@hugo.local' : `user${i}@hugo.local`,
      isAdmin: i === 1,
      isTenantAdmin: i < 3
    }),
  credentials: (i) =>
    record('cred', i, {
      tenantId: TENANT,
      name: `claude-code-${i}`,
      userId: 'usr_0001',
      accessKey: `access_${i}xyz`,
      secretKeyLast4: 'ab12',
      authMode: 'Hybrid',
      agentProfileId: 'prf_0001',
      lockToProfile: i === 1,
      lastUsedUtc: iso(i)
    }),
  roles: (i) =>
    record('role', i, { tenantId: TENANT, name: ['TenantAdmin', 'Operator', 'Builder', 'Custom'][i % 4], description: 'Role description', isBuiltIn: i < 3 }),
  permissions: (i) =>
    record('perm', i, {
      tenantId: TENANT,
      name: `perm-${i}`,
      permissionType: i % 4 === 0 ? 'Deny' : 'Permit',
      isBuiltIn: i < 3,
      resourceTypes: ['Resource', 'Secret'],
      operationTypes: ['Read', 'Reveal']
    }),
  'user-role-assignments': (i) =>
    record('ura', i, { tenantId: TENANT, userId: 'usr_0001', roleId: 'role_0001', roleName: 'TenantAdmin', resourceScope: 'Tenant', inheritsToChildren: true }),
  'credential-scope-assignments': (i) =>
    record('csa', i, {
      tenantId: TENANT,
      credentialId: 'cred_0001',
      roleId: 'role_0002',
      roleName: 'ProfileUser',
      resourceScope: 'Resource',
      resourceId: 'prf_0001',
      inheritsToChildren: true
    }),
  'prompt-templates': (i) =>
    record('pt', i, {
      tenantId: TENANT,
      name: ['persona.agent-smith', 'system.safety', 'connector.ssh.instructions', 'connector.sql.instructions'][(i - 1) % 4],
      description: 'Prompt template',
      kind: 'System',
      content: 'You are {{persona.name}}. Address the user as {{persona.userAddress}}.',
      currentVersion: i,
      isBuiltIn: i < 3
    }),
  'prompt-template-versions': (v) =>
    record('tpr', v, {
      templateId: 'pt_0001',
      version: v,
      content: `Version ${v}: You are {{persona.name}}.`,
      changeNote: `v${v}`,
      createdBy: 'admin@hugo.local'
    }),
  audit: (i) => ({
    id: `aud_${pad(i)}`,
    tenantId: TENANT,
    eventType: ['AuthSuccess', 'SecretRevealed', 'AuthorizationDenied', 'ResourceChanged'][i % 4],
    principalType: 'User',
    principalName: 'admin@hugo.local',
    principalId: 'usr_0001',
    resourceType: 'Secret',
    operation: 'Reveal',
    reason: 'Incident 42',
    statusCode: 200,
    sourceIp: '127.0.0.1',
    requestId: `req_${pad(i)}`,
    createdUtc: iso(i * 5)
  }),
  'request-history': (i) => ({
    id: `req_${pad(i)}`,
    tenantId: TENANT,
    userId: 'usr_0001',
    principalName: 'admin@hugo.local',
    method: ['GET', 'POST', 'PUT', 'DELETE'][i % 4],
    path: '/v1.0/api/resources',
    url: '/v1.0/api/resources',
    statusCode: i % 7 === 0 ? 500 : i % 5 === 0 ? 404 : 200,
    durationMs: 3 + i,
    sourceIp: '127.0.0.1',
    requestBodyBytes: 0,
    requestBodyTruncated: false,
    responseBody: '{"items":[]}',
    responseBodyBytes: 12,
    responseBodyTruncated: false,
    createdUtc: iso(i * 2),
    completedUtc: iso(i * 2 - 0.01),
    headers: [{ direction: 'Request', name: 'accept', value: 'application/json' }]
  }),
  // Hugo records (plan section 7).
  resources: (i) =>
    record('res', i, {
      tenantId: TENANT,
      name: ['web-01', 'prod-db', 'assets', 'papertrail'][(i - 1) % 4] + (i > 4 ? `-${i}` : ''),
      connectorType: ['ssh', 'postgresql', 's3', 'papertrail'][(i - 1) % 4],
      environment: 'prod',
      visibility: 'Tenant',
      endpoint: ['web-01.example.com:22', 'db.example.com:5432', 's3://acme-assets', 'papertrailapp.com'][(i - 1) % 4],
      notes: 'Checkout runs here.',
      healthStatus: i === 2 ? 'Unhealthy' : 'Healthy',
      lastTestUtc: iso(i * 10),
      lastTestMessage: i === 2 ? 'Connection refused' : 'OK',
      settings: { host: 'web-01.example.com', port: 22, username: 'ops' },
      secretBindings: [{ slotName: 'privateKey', secretId: 'sec_0002' }]
    }),
  secrets: (i) =>
    record('sec', i, {
      tenantId: TENANT,
      name: ['prod-db-password', 'web-01-ssh-key', 'aws-prod', 'papertrail-token'][(i - 1) % 4],
      kind: ['Password', 'PrivateKey', 'AccessKeyPair', 'ApiToken'][(i - 1) % 4],
      description: 'Managed by ops',
      fingerprint: ['****wd42', 'SHA256:abc', 'AKIA****', '****tk'][(i - 1) % 4],
      referrerCount: i % 3,
      lastRotatedUtc: iso(i * 100),
      expiresUtc: i === 3 ? new Date(Date.now() + 3 * 86400000).toISOString() : null
    }),
  'agent-profiles': (i) =>
    record('prf', i, {
      tenantId: TENANT,
      name: i === 1 ? 'Hugo' : `profile-${i}`,
      displayName: i === 1 ? 'Hugo' : `Profile ${i}`,
      personaName: 'Agent Smith',
      userAddress: 'Mr. Anderson',
      visibility: 'Tenant',
      isDefault: i === 1,
      inferenceEndpointId: i === 1 ? 'mdl_0001' : null,
      bindingCount: 4,
      exposeMcp: true,
      exposeA2a: true,
      exposeOpenai: i === 1,
      maxIterations: 12
    }),
  'model-endpoints': (i) =>
    record('mdl', i, {
      tenantId: TENANT,
      name: i === 1 ? 'local-llm' : `endpoint-${i}`,
      kind: i === 2 ? 'Embedding' : 'Inference',
      provider: i === 2 ? 'Ollama' : 'Anthropic',
      baseUrl: 'http://127.0.0.1:11434',
      model: i === 2 ? 'nomic-embed-text' : 'claude-sonnet',
      healthStatus: 'Healthy',
      lastHealthUtc: iso(5),
      lastHealthMessage: 'Responded in 210 ms'
    }),
  conversations: (i) =>
    record('cnv', i, {
      tenantId: TENANT,
      title: i === 1 ? 'Why is checkout slow?' : `Conversation ${i}`,
      agentProfileId: 'prf_0001',
      userId: 'usr_0001',
      source: ['Dashboard', 'A2A', 'Mcp', 'OpenAI'][(i - 1) % 4],
      messageCount: 4 + i,
      lastMessageUtc: iso(i * 30),
      totalInputTokens: 1200,
      totalOutputTokens: 300
    }),
  'tool-invocations': (i) =>
    record('tin', i, {
      tenantId: TENANT,
      surface: ['Chat', 'Mcp', 'A2A', 'Rest'][(i - 1) % 4],
      agentProfileId: 'prf_0001',
      resourceId: 'res_0001',
      resourceAlias: 'web-01',
      toolName: ['ssh_exec', 'sql_query', 'logs_search'][(i - 1) % 3],
      effect: ['Read', 'Write', 'Destructive'][(i - 1) % 3],
      decision: ['Allowed', 'Allowed', 'Approved'][(i - 1) % 3],
      status: i % 5 === 0 ? 'Failed' : 'Succeeded',
      durationMs: 120 + i,
      argumentsRedacted: { command: 'uptime', password: '[redacted:password]' },
      outputPreview: ' 12:00 up 41 days',
      outputBytes: 2048,
      traceId: '4bf92f3577b34da6a3ce929d0e0e4736',
      errorMessage: i % 5 === 0 ? 'Permission denied' : null
    }),
  approvals: (i) =>
    record('apr', i, {
      tenantId: TENANT,
      toolInvocationId: `tin_${pad(i)}`,
      agentProfileId: 'prf_0001',
      summary: i === 1 ? 'Run `sudo systemctl restart nginx` on web-01' : `Delete object backups/${i}.tar.gz from assets`,
      effect: i === 1 ? 'Destructive' : 'Write',
      effectReason: 'systemctl restart',
      command: 'sudo systemctl restart nginx',
      resourceAlias: 'web-01',
      toolName: 'ssh_exec',
      status: i <= 2 ? 'Pending' : 'Approved',
      requestedByName: 'Agent Smith (chat)',
      expiresUtc: new Date(Date.now() + 15 * 60000).toISOString()
    }),
  sessions: (i) =>
    record('ses', i, {
      tenantId: TENANT,
      principalType: i === 1 ? 'Administrator' : 'User',
      principalName: i === 1 ? 'admin@hugo.local' : 'anderson@hugo.local',
      authScheme: 'Password',
      sourceIp: '127.0.0.1',
      issuedUtc: iso(i * 20),
      lastUsedUtc: iso(i),
      expiresUtc: iso(-60),
      userAgent: 'Mozilla/5.0'
    }),
  knowledge: (i) =>
    record('kno', i, {
      tenantId: TENANT,
      title: `Note ${i}`,
      content: 'Checkout runs on web-01 and web-02 behind the ALB.',
      agentProfileId: null,
      resourceId: 'res_0001'
    }),
  'system/plugins': (i) =>
    record('plg', i, {
      name: `Hugo.Connectors.Example${i}`,
      version: '1.0.0',
      connectorTypes: [`example-${i}`],
      enabled: i === 1,
      loadError: i === 2 ? 'Could not load file or assembly' : null,
      sha256: 'abc123def456'
    })
};

const TOTALS = { 'request-history': 64, audit: 42, 'tool-invocations': 37, resources: 4, secrets: 4, connectors: CONNECTORS.length, approvals: 4 };

/** Garrison enumeration envelope (maxResults and skip). */
function enumerate(collection, url) {
  const factory = FACTORIES[collection];
  const total = TOTALS[collection] ?? 6;
  const pageSize = Number(url.searchParams.get('maxResults') || 25);
  const skip = Number(url.searchParams.get('skip') || 0);
  const objects = [];
  for (let i = skip + 1; i <= Math.min(total, skip + pageSize); i++) objects.push(factory(i));
  return { success: true, maxResults: pageSize, skip, totalRecords: total, endOfResults: skip + objects.length >= total, objects };
}

/** Hugo page result (pageNumber and pageSize). */
function pageOf(rows, url) {
  const pageSize = Number(url.searchParams.get('pageSize') || 25);
  const pageNumber = Number(url.searchParams.get('pageNumber') || 1);
  return {
    items: rows.slice((pageNumber - 1) * pageSize, pageNumber * pageSize),
    pageNumber,
    pageSize,
    totalCount: rows.length,
    totalPages: Math.max(1, Math.ceil(rows.length / pageSize))
  };
}

function rows(collection, url) {
  const total = TOTALS[collection] ?? 6;
  let list = Array.from({ length: total }, (_, i) => FACTORIES[collection](i + 1));
  const status = url.searchParams.get('status');
  if (status && collection === 'approvals') list = list.filter((r) => r.status === status);
  const health = url.searchParams.get('health');
  if (health) list = list.filter((r) => r.healthStatus === health);
  return list;
}

/** Garrison page result used by audit and request history. */
function legacyPage(collection, url) {
  const total = TOTALS[collection] ?? 6;
  const pageSize = Number(url.searchParams.get('pageSize') || 25);
  const pageNumber = Number(url.searchParams.get('pageNumber') || 1);
  const objects = [];
  for (let i = (pageNumber - 1) * pageSize + 1; i <= Math.min(total, pageNumber * pageSize); i++) objects.push(FACTORIES[collection](i));
  return { pageNumber, pageSize, totalCount: total, totalPages: Math.ceil(total / pageSize), objects };
}

function summary() {
  const buckets = [];
  for (let i = 0; i < 24; i++)
    buckets.push({ bucketStartUtc: iso(i * 60), bucketEndUtc: iso(i * 60 - 60), successCount: 10 + (i % 5), failureCount: i % 3, averageDurationMs: 20 + i });
  return { totalCount: 400, totalSuccess: 360, totalFailure: 40, averageDurationMs: 24, buckets };
}

const SETTINGS = {
  settingsFile: '/app/config/hugo.json',
  settingsFileWritable: true,
  restartPending: false,
  fields: [
    {
      path: 'Rest.Hostname',
      group: 'Rest',
      description: 'Listener hostname.',
      type: 'string',
      value: '0.0.0.0',
      isSecret: false,
      isSet: true,
      source: 'Environment',
      applyMode: 'RestartRequired',
      editable: false
    },
    {
      path: 'Rest.Port',
      group: 'Rest',
      description: 'Listener port.',
      type: 'int',
      value: '8800',
      isSecret: false,
      isSet: true,
      source: 'File',
      applyMode: 'RestartRequired',
      editable: true,
      minimum: 1,
      maximum: 65535
    },
    {
      path: 'Database.Type',
      group: 'Database',
      description: 'Database provider.',
      type: 'enum',
      value: 'Sqlite',
      isSecret: false,
      isSet: true,
      source: 'File',
      applyMode: 'RestartRequired',
      editable: true,
      allowedValues: ['Sqlite', 'Postgresql']
    },
    {
      path: 'Database.Password',
      group: 'Database',
      description: 'Database password.',
      type: 'secret',
      isSecret: true,
      isSet: true,
      value: '********',
      source: 'File',
      applyMode: 'RestartRequired',
      editable: true
    },
    {
      path: 'Secrets.RevealReauthMinutes',
      group: 'Secrets',
      description: 'Minutes after re-authentication during which secrets may be revealed.',
      type: 'int',
      value: '5',
      isSecret: false,
      isSet: true,
      source: 'File',
      applyMode: 'Live',
      editable: true,
      minimum: 1,
      maximum: 60
    },
    {
      path: 'Agent.DefaultMaxIterations',
      group: 'Agent',
      description: 'Default maximum model rounds per turn.',
      type: 'int',
      value: '12',
      isSecret: false,
      isSet: true,
      source: 'File',
      applyMode: 'Live',
      editable: true,
      minimum: 1,
      maximum: 100
    },
    {
      path: 'Approvals.ExpirySeconds',
      group: 'Approvals',
      description: 'Seconds before an undecided approval expires.',
      type: 'int',
      value: '900',
      isSecret: false,
      isSet: true,
      source: 'File',
      applyMode: 'Live',
      editable: true,
      minimum: 30,
      maximum: 86400
    },
    {
      path: 'Approvals.AllowSelfApproval',
      group: 'Approvals',
      description: 'Allow approving your own calls.',
      type: 'bool',
      value: 'true',
      isSecret: false,
      isSet: true,
      source: 'File',
      applyMode: 'Live',
      editable: true
    },
    {
      path: 'Connectors.EnablePlugins',
      group: 'Connectors',
      description: 'Load connector plugins.',
      type: 'bool',
      value: 'false',
      isSecret: false,
      isSet: true,
      source: 'File',
      applyMode: 'RestartRequired',
      editable: true
    }
  ]
};

export const OPENAPI = {
  openapi: '3.0.1',
  info: { title: 'Hugo', version: '0.1.0-alpha', description: 'Hugo REST API.' },
  paths: {
    '/v1.0/api/health': { get: { tags: ['Health'], summary: 'Health check', operationId: 'health' } },
    '/v1.0/api/resources': {
      get: { tags: ['Resources'], summary: 'List resources', parameters: [{ name: 'pageSize', in: 'query', schema: { type: 'integer' } }] },
      post: {
        tags: ['Resources'],
        summary: 'Create resource',
        requestBody: {
          content: { 'application/json': { schema: { type: 'object', properties: { name: { type: 'string' }, connectorType: { type: 'string' } } } } }
        }
      }
    },
    '/v1.0/api/resources/{id}': {
      delete: { tags: ['Resources'], summary: 'Delete resource', parameters: [{ name: 'id', in: 'path', required: true, schema: { type: 'string' } }] }
    }
  }
};

const SERVER_INFO = {
  product: 'Hugo',
  version: '0.1.0-alpha',
  nodeId: 'hugo-server',
  restUrl: 'http://127.0.0.1:8800',
  mcpUrl: 'http://127.0.0.1:8800/mcp',
  openApiUrl: 'http://127.0.0.1:8800/openapi.json',
  databaseType: 'Sqlite',
  connectors: CONNECTORS.map((c) => c.type),
  features: ['mcp', 'a2a', 'openai', 'request-history'],
  observability: {
    grafanaUrl: 'http://127.0.0.1:3000',
    prometheusUrl: 'http://127.0.0.1:9090',
    lokiUrl: null,
    tempoUrl: 'http://127.0.0.1:3200',
    datasources: { prometheus: 'prometheus', loki: 'loki', tempo: 'tempo' },
    dashboards: { overview: 'hugo-overview' }
  }
};

const PERMISSIONS = (principalType, principalId) => ({
  principalType,
  principalId,
  tenantId: TENANT,
  isAdmin: false,
  isTenantAdmin: false,
  grants: [
    { source: 'Operator', permissionType: 'Permit', resourceTypes: ['Resource', 'Connector'], operations: ['Read'], scope: 'Tenant', inheritsToChildren: true }
  ],
  hasPermission: [{ resourceType: 'Secret', operation: 'Reveal', permitted: false }]
});

const BINDINGS = [
  {
    id: 'bnd_0001',
    agentProfileId: 'prf_0001',
    resourceId: 'res_0002',
    resourceName: 'prod-db',
    alias: 'prod-db',
    accessMode: 'ReadOnly',
    requireApprovalForDestructive: true,
    instructionMode: 'Inherit',
    enabled: true
  },
  {
    id: 'bnd_0002',
    agentProfileId: 'prf_0001',
    resourceId: 'res_0001',
    resourceName: 'web-01',
    alias: 'web-01',
    accessMode: 'ReadWriteWithApproval',
    requireApprovalForDestructive: true,
    instructionMode: 'Append',
    instructionText: 'Never restart payments.',
    enabled: true
  }
];

const PROFILE_TOOLS = [
  { name: 'hugo_whoami', description: 'Who am I', baseEffect: 'Read', inputSchema: { type: 'object', properties: {} } },
  {
    name: 'sql_query',
    description: 'Run a SQL query.',
    baseEffect: 'Read',
    classifiedPerCall: true,
    inputSchema: {
      type: 'object',
      required: ['resource', 'sql'],
      properties: { resource: { type: 'string', enum: ['prod-db'] }, sql: { type: 'string', description: 'The query.' } }
    }
  },
  {
    name: 'ssh_exec',
    description: 'Run a command.',
    baseEffect: 'Read',
    classifiedPerCall: true,
    inputSchema: {
      type: 'object',
      required: ['resource', 'command'],
      properties: { resource: { type: 'string', enum: ['web-01'] }, command: { type: 'string' } }
    }
  }
];

export const MESSAGES = [
  { id: 'msg_0001', role: 'User', content: 'Why is checkout slow?', sequence: 1, createdUtc: iso(30) },
  {
    id: 'msg_0002',
    role: 'Assistant',
    content: 'Mr. Anderson, `prod-db` shows **14 waiting queries** on the orders table.',
    sequence: 2,
    inputTokens: 1800,
    outputTokens: 120,
    timeToFirstTokenMs: 640,
    durationMs: 4200,
    toolInvocations: [
      {
        id: 'tin_0002',
        toolName: 'sql_query',
        resourceAlias: 'prod-db',
        effect: 'Read',
        status: 'Succeeded',
        durationMs: 88,
        argumentsRedacted: { sql: 'select * from pg_stat_activity' },
        outputPreview: '14 rows'
      }
    ],
    createdUtc: iso(29)
  }
];

/** The chat turn the mock streams for POST /conversations/{id}/messages (D-022 event names). */
export const CHAT_FRAMES = [
  ['turn.started', { messageId: 'msg_0010', conversationId: 'cnv_0001' }],
  ['assistant.thinking', { text: 'Restart requested; this is destructive.' }],
  [
    'tool.proposed',
    { invocationId: 'tin_0099', toolName: 'ssh_exec', resourceAlias: 'web-01', effect: 'Destructive', arguments: { command: 'sudo systemctl restart nginx' } }
  ],
  [
    'approval.required',
    {
      approvalId: 'apr_0099',
      invocationId: 'tin_0099',
      summary: 'Run `sudo systemctl restart nginx` on web-01',
      effect: 'Destructive',
      effectReason: 'systemctl restart',
      command: 'sudo systemctl restart nginx',
      resourceAlias: 'web-01',
      toolName: 'ssh_exec'
    }
  ],
  ['assistant.delta', { text: 'I need your approval to restart nginx, ' }],
  ['assistant.delta', { text: 'Mr. Anderson.' }],
  ['turn.completed', { messageId: 'msg_0010', inputTokens: 900, outputTokens: 40, timeToFirstTokenMs: 410, durationMs: 1300 }]
];

export function sse(frames) {
  return frames.map(([name, data]) => `event: ${name}\ndata: ${JSON.stringify(data)}\n\n`).join('');
}

const MCP_TOOLS = PROFILE_TOOLS;

/** One JSON-RPC answer from the mock MCP server. */
function mcpAnswer(message) {
  if (!message || message.id === undefined) return null;
  if (message.method === 'initialize')
    return {
      jsonrpc: '2.0',
      id: message.id,
      result: {
        protocolVersion: '2025-06-18',
        serverInfo: { name: 'Hugo', version: '0.1.0-alpha' },
        capabilities: { tools: {} },
        instructions: '# Agent Smith\nYou are Agent Smith. Address the user as Mr. Anderson.\n\n## prod-db (PostgreSQL, ReadOnly)'
      }
    };
  if (message.method === 'tools/list') return { jsonrpc: '2.0', id: message.id, result: { tools: MCP_TOOLS } };
  if (message.method === 'tools/call')
    return {
      jsonrpc: '2.0',
      id: message.id,
      result: { content: [{ type: 'text', text: `called ${message.params?.name} with ${JSON.stringify(message.params?.arguments)}` }], isError: false }
    };
  return { jsonrpc: '2.0', id: message.id, error: { code: -32601, message: 'Method not found' } };
}

const AGENT_CARD = {
  name: 'Hugo',
  description: 'Agent Smith with four bound resources.',
  version: '0.1.0-alpha',
  capabilities: { streaming: true },
  skills: [{ id: 'agent-smith', name: 'Agent Smith', description: 'Investigate the stack.' }],
  securitySchemes: { bearer: { type: 'http', scheme: 'bearer' } }
};

function a2aAnswer(message) {
  if (message.method === 'ListTasks')
    return { jsonrpc: '2.0', id: message.id, result: { tasks: [{ id: 'task_1', contextId: 'ctx_1', status: { state: 'completed' } }] } };
  const text = message.params?.message?.parts?.[0]?.text || '';
  return {
    jsonrpc: '2.0',
    id: message.id,
    result: {
      kind: 'task',
      id: 'task_2',
      contextId: 'ctx_2',
      status: { state: 'completed', message: { kind: 'message', role: 'agent', parts: [{ kind: 'text', text: `Agent Smith received: ${text}` }] } }
    }
  };
}

/** Hugo capability groups that `missing` can switch off (their routes answer 404). */
export const CAPABILITY_PREFIXES = {
  resources: '/v1.0/api/resources',
  secrets: '/v1.0/api/secrets',
  connectors: '/v1.0/api/connectors',
  profiles: '/v1.0/api/agent-profiles',
  models: '/v1.0/api/model-endpoints',
  conversations: '/v1.0/api/conversations',
  toolInvocations: '/v1.0/api/tool-invocations',
  approvals: '/v1.0/api/approvals',
  sessions: '/v1.0/api/sessions',
  knowledge: '/v1.0/api/knowledge',
  analytics: '/v1.0/api/analytics',
  plugins: '/v1.0/api/system/plugins',
  events: '/v1.0/api/events',
  mcp: '/mcp',
  a2a: '/v1.0/a2a'
};

/** JSON body for a request, or undefined when unhandled (404). */
export function respond(method, url, body) {
  const p = url.pathname;
  if (p === '/openapi.json') return OPENAPI;
  if (p === '/v1.0/token' && method === 'POST')
    return { token: 'tok_e2e', expiresUtc: iso(-60), tenantId: TENANT, sessionId: 'ses_e2e', principalType: 'User', mustChangePassword: false };
  if (p === '/v1.0/token/details') return PRINCIPAL;
  if (p === '/v1.0/tenants/lookup') return [{ id: TENANT, name: 'Default' }];
  if (p.startsWith('/v1.0/token')) return {};
  if (p === '/v1.0/api/health') return { status: 'healthy', version: '0.1.0-alpha', utc: iso(0) };
  if (p === '/v1.0/api/info') return SERVER_INFO;
  if (p === '/v1.0/api/observability/status')
    return ['grafana', 'prometheus', 'loki', 'tempo'].map((service) => ({
      service,
      configured: service !== 'loki',
      reachable: service === 'grafana',
      version: service === 'grafana' ? '11.2.0' : null
    }));
  if (p === '/v1.0/api/settings') return method === 'GET' ? SETTINGS : { saved: true, restartRequired: false };
  if (p === '/v1.0/api/system/restart') return { restarting: true };
  if (p === '/v1.0/api/tenant/lockdown') return { lockdown: method === 'POST' };
  if (p === '/v1.0/api/request-history/summary') return summary();
  if (p === '/v1.0/api/request-history') return legacyPage('request-history', url);
  if (p.startsWith('/v1.0/api/request-history/')) return FACTORIES['request-history'](1);
  if (/^\/v1\.0\/api\/users\/[^/]+\/password$/.test(p)) return {};

  // Garrison-forked tenant and tenant-scoped administration routes.
  let m = p.match(/^\/v1\.0\/api\/tenants(?:\/([^/]+))?$/);
  if (m) return m[1] ? FACTORIES.tenants(1) : method === 'GET' ? enumerate('tenants', url) : FACTORIES.tenants(99);
  m = p.match(/^\/v1\.0\/api\/tenants\/[^/]+\/(.+)$/);
  if (m) {
    const parts = m[1].split('/');
    const collection = parts[0];
    if (collection === 'audit' && parts.length === 1) return legacyPage('audit', url);
    if ((collection === 'users' || collection === 'credentials') && parts[2] === 'permissions')
      return PERMISSIONS(collection === 'users' ? 'User' : 'Credential', parts[1]);
    if (collection === 'credentials' && parts[2] === 'rotate') return { accessKey: 'access_1xyz', secretKey: 'secret_rotated_value' };
    if (collection === 'credentials' && parts.length === 1 && method === 'POST') return { ...FACTORIES.credentials(9), secretKey: 'secret_new_value' };
    if (collection === 'prompt-templates' && parts[1] === 'render')
      return { rendered: 'You are Agent Smith. Address the user as Mr. Anderson.', missingVariables: [] };
    if (collection === 'prompt-templates' && parts[2] === 'versions') return [1, 2].map(FACTORIES['prompt-template-versions']);
    if (!FACTORIES[collection]) return undefined;
    if (parts.length === 1) return method === 'GET' ? enumerate(collection, url) : FACTORIES[collection](99);
    return FACTORIES[collection](1);
  }

  // Hugo routes (plan section 14.2).
  if (p === '/v1.0/api/connectors') return pageOf(CONNECTORS, url);
  m = p.match(/^\/v1\.0\/api\/connectors\/([^/]+)$/);
  if (m) return CONNECTORS.find((c) => c.type === m[1]) || undefined;
  if (p === '/v1.0/api/resources/test') return { success: true, message: 'Connected to web-01 as ops in 42 ms.', latencyMs: 42 };
  if (p === '/v1.0/api/tool-invocations/summary') return { totals: { total: 37, failed: 7 } };
  if (p === '/v1.0/api/tool-invocations/dry-run')
    return { effect: 'Read', effectReason: 'uptime is read only', decision: 'Allowed', accessMode: 'ReadWriteWithApproval' };
  if (p === '/v1.0/api/tool-invocations' && method === 'POST') return { id: 'tin_0100', status: 'Succeeded', outputPreview: ' 12:00 up 41 days' };
  if (p.startsWith('/v1.0/api/analytics/')) {
    const buckets = Array.from({ length: 8 }, (_, i) => ({
      timestampUtc: iso(i * 180),
      inputTokens: 1000 + i * 50,
      outputTokens: 200 + i * 10,
      Read: 10 + i,
      Write: i % 3,
      Destructive: i % 2,
      latencyMsP50: 400 + i * 10,
      latencyMsP95: 900 + i * 20,
      errors: i % 2
    }));
    return {
      bucketSeconds: 900,
      buckets,
      totals: {
        turns: 42,
        toolCalls: 180,
        toolFailures: 7,
        inputTokens: 52000,
        outputTokens: 9100,
        approvals: 6,
        approvalTurnaroundMsP50: 45000,
        approvalRate: 0.83
      },
      byConnector: [
        { connectorType: 'ssh', count: 90, failures: 3, durationMsP95: 1200 },
        { connectorType: 'postgresql', count: 70, failures: 1, durationMsP95: 300 }
      ]
    };
  }
  m = p.match(/^\/v1\.0\/api\/agent-profiles\/([^/]+)\/(bindings|tools|compiled-instructions|card)(?:\/([^/]+))?(?:\/(tool-rules))?$/);
  if (m) {
    if (m[2] === 'bindings' && !m[3]) return method === 'GET' ? pageOf(BINDINGS, url) : { ...BINDINGS[0], id: 'bnd_0099', ...(body || {}) };
    if (m[2] === 'bindings' && m[4]) return method === 'GET' ? { rules: [{ toolName: 'ssh_exec', rule: 'RequireApproval' }] } : {};
    if (m[2] === 'bindings') return BINDINGS[0];
    if (m[2] === 'tools') return { tools: PROFILE_TOOLS };
    if (m[2] === 'card') return AGENT_CARD;
    return {
      text: '# Agent Smith\nYou are Agent Smith. Address the user as Mr. Anderson.\n\n## prod-db (PostgreSQL, access: ReadOnly)\n...',
      sections: [
        { title: 'Persona', templateKey: 'persona.agent-smith', layer: 'BuiltIn' },
        { title: 'prod-db', templateKey: 'connector.sql.instructions', layer: 'Tenant' }
      ]
    };
  }
  m = p.match(/^\/v1\.0\/api\/secrets\/([^/]+)\/(reveal|referrers|values)$/);
  if (m) {
    if (m[2] === 'reveal') return { values: { password: 'swordfish' } };
    if (m[2] === 'referrers') return { resources: [{ id: 'res_0002', name: 'prod-db' }], modelEndpoints: [] };
    return {};
  }
  m = p.match(/^\/v1\.0\/api\/resources\/([^/]+)\/(test|referrers|duplicate)$/);
  if (m)
    return m[2] === 'test'
      ? { success: true, message: 'Connected in 42 ms.', latencyMs: 42 }
      : m[2] === 'referrers'
        ? { profiles: [{ id: 'prf_0001', name: 'Hugo' }], bindings: [] }
        : FACTORIES.resources(9);
  m = p.match(/^\/v1\.0\/api\/model-endpoints\/([^/]+)\/(validate|models)$/);
  if (m) return m[2] === 'validate' ? { success: true, latencyMs: 210, message: 'The model answered.' } : { models: ['claude-sonnet', 'claude-haiku'] };
  m = p.match(/^\/v1\.0\/api\/approvals\/([^/]+)\/(approve|reject|cancel)$/);
  if (m) return { ...FACTORIES.approvals(1), id: m[1], status: m[2] === 'approve' ? 'Approved' : m[2] === 'reject' ? 'Rejected' : 'Cancelled' };
  m = p.match(/^\/v1\.0\/api\/conversations\/([^/]+)\/(messages|cancel|export)$/);
  if (m) {
    if (m[2] === 'messages' && method === 'GET') return pageOf(MESSAGES, url);
    if (m[2] === 'export') return '# Why is checkout slow?\n';
    return {};
  }
  if (p === '/v1.0/api/conversations' && method === 'POST') return { ...FACTORIES.conversations(1), id: 'cnv_0042', title: null, messageCount: 0 };
  if (p === '/v1.0/api/agent-profiles' && method === 'POST') return { ...FACTORIES['agent-profiles'](9), ...(body || {}) };
  m = p.match(
    /^\/v1\.0\/api\/(resources|secrets|agent-profiles|model-endpoints|conversations|tool-invocations|approvals|sessions|knowledge|system\/plugins)(?:\/([^/]+))?$/
  );
  if (m) {
    const collection = m[1];
    if (!m[2]) return method === 'GET' ? pageOf(rows(collection, url), url) : { ...FACTORIES[collection](99), ...(body || {}) };
    const n = Number(String(m[2]).replace(/^\D+_/, '')) || 1;
    return { ...FACTORIES[collection](n), id: m[2] };
  }
  return undefined;
}

/**
 * Install mocks on a Playwright page. Options: `missing` (capability names from CAPABILITY_PREFIXES that answer 404),
 * `events` (SSE body for GET /v1.0/api/events; default: 404, so the dashboard polls), `chatFrames` (the streamed turn).
 */
export async function installMocks(page, { missing = [], events = null, chatFrames = CHAT_FRAMES, onRequest } = {}) {
  const blocked = missing.map((k) => CAPABILITY_PREFIXES[k]).filter(Boolean);
  await page.route(/\/(v1\.0|openapi\.json|mcp)(\/|$|\?)/, async (route) => {
    const req = route.request();
    const url = new URL(req.url());
    const method = req.method();
    let body = null;
    try {
      body = req.postData() ? JSON.parse(req.postData()) : null;
    } catch {
      body = null;
    }
    onRequest?.({ method, path: url.pathname, body, headers: req.headers() });
    const notFound = () =>
      route.fulfill({
        status: 404,
        contentType: 'application/json',
        body: JSON.stringify({ error: 'NotFound', message: 'Route not found.', requestId: 'req_404' })
      });
    if (blocked.some((prefix) => url.pathname === prefix || url.pathname.startsWith(`${prefix}/`))) return notFound();
    if (url.pathname === '/v1.0/api/events') {
      if (!events) return notFound();
      return route.fulfill({ status: 200, headers: { 'content-type': 'text/event-stream' }, body: events });
    }
    if (url.pathname === '/mcp' || url.pathname.startsWith('/mcp/')) {
      const answer = mcpAnswer(body);
      if (!answer) return route.fulfill({ status: 202, body: '' });
      return route.fulfill({ status: 200, headers: { 'content-type': 'application/json', 'mcp-session-id': 'mcp_session_e2e' }, body: JSON.stringify(answer) });
    }
    const a2a = url.pathname.match(/^\/v1\.0\/a2a\/([^/]+)\/([^/]+)(\/\.well-known\/agent-card\.json)?$/);
    if (a2a) {
      if (a2a[3]) return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(AGENT_CARD) });
      return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(a2aAnswer(body)) });
    }
    if (/^\/v1\.0\/api\/conversations\/[^/]+\/messages$/.test(url.pathname) && method === 'POST') {
      return route.fulfill({ status: 200, headers: { 'content-type': 'text/event-stream' }, body: sse(chatFrames) });
    }
    if (method === 'DELETE') return route.fulfill({ status: 204, body: '' });
    const answer = respond(method, url, body);
    if (answer === undefined) return notFound();
    if (typeof answer === 'string') return route.fulfill({ status: 200, contentType: 'text/markdown', body: answer });
    return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(answer) });
  });
}

/** Sign in through the login form with the seeded local administrator. */
export async function login(page, { expect }) {
  await page.goto('/');
  await page.locator('#email').fill('admin@hugo.local');
  await page.locator('#password').fill('password');
  await page.getByTestId('login-submit').click();
  await expect(page.locator('.dashboard')).toBeVisible();
}
