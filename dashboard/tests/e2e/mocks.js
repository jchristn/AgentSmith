// Deterministic Hugo API mocks so the Playwright suite runs without a server.
//
// The shapes match the real server (src/Hugo.Server/Routes and the src/Hugo.Core models). Identity and administration
// routes are tenant-scoped (Garrison paths); Hugo's own routes are flat (tenant from the session). Both enumerate with
// Garrison's envelope ({ objects, totalRecords, maxResults, skip }); connectors, bindings, knowledge, and referrers answer
// bare arrays; request history and audit page with pageNumber and pageSize. `installMocks(page, { missing })` answers 404
// RouteNotFound for the named capabilities, so tests can check the "server does not expose this yet" states.

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
  ],
  secretSlots: [{ name: 'privateKey', label: 'Private key', help: 'SSH private key for the account.', acceptedKinds: ['PrivateKey'], required: true }],
  defaultInstructionsKey: 'connector.ssh.instructions',
  plugin: false,
  tools: [
    {
      name: 'ssh_exec',
      title: 'Run a command',
      description: 'Run a shell command.',
      baseEffect: 'Read',
      classifiedPerCall: true,
      updatesKnowledge: false,
      requiresModel: false
    },
    {
      name: 'ssh_read_file',
      title: 'Read a file',
      description: 'Read a file within the allowed paths.',
      baseEffect: 'Read',
      classifiedPerCall: false,
      updatesKnowledge: false,
      requiresModel: false
    }
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
    defaultInstructionsKey: 'connector.postgresql.instructions',
    secretSlots: [{ name: 'password', label: 'Password', acceptedKinds: ['Password', 'ConnectionString'], required: true }],
    tools: [
      { name: 'sql_query', description: 'Run a query.', baseEffect: 'Read', classifiedPerCall: true },
      { name: 'sql_get_context', description: 'Read durable context.', baseEffect: 'Read' },
      { name: 'sql_get_intelligence', description: 'Domain intelligence.', baseEffect: 'Read' },
      { name: 'sql_list_relationships', description: 'Declared and inferred relationships.', baseEffect: 'Read' },
      { name: 'sql_update_context', description: 'Record context.', baseEffect: 'Read', updatesKnowledge: true },
      { name: 'sql_generate_context', description: 'Generate context with the profile model.', baseEffect: 'Read', updatesKnowledge: true, requiresModel: true }
    ]
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
    type: 'solarwinds-observability',
    family: 'logs',
    displayName: 'SolarWinds Observability',
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
      inheritOwnerBypass: i === 2,
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
      name: ['web-01', 'prod-db', 'assets', 'checkout-logs'][(i - 1) % 4] + (i > 4 ? `-${i}` : ''),
      connectorType: ['ssh', 'postgresql', 's3', 'solarwinds-observability'][(i - 1) % 4],
      environment: 'prod',
      visibility: 'Tenant',
      endpoint: ['web-01.example.com:22', 'db.example.com:5432', 's3://acme-assets', 'api.na-01.cloud.solarwinds.com'][(i - 1) % 4],
      notes: 'Checkout runs here.',
      lastTestResult: i !== 2,
      lastTestUtc: iso(i * 10),
      lastTestMessage: i === 2 ? 'Connection refused' : 'OK',
      settings: [
        { key: 'host', valueKind: 'String', value: 'web-01.example.com' },
        { key: 'port', valueKind: 'Integer', value: '22' },
        { key: 'username', valueKind: 'String', value: 'ops' }
      ],
      secretBindings: [{ slotName: 'privateKey', secretId: 'sec_0002', secretName: 'web-01-ssh-key' }]
    }),
  secrets: (i) =>
    record('sec', i, {
      tenantId: TENANT,
      name: ['prod-db-password', 'web-01-ssh-key', 'aws-prod', 'solarwinds-token'][(i - 1) % 4],
      kind: ['Password', 'PrivateKey', 'AccessKeyPair', 'ApiToken'][(i - 1) % 4],
      description: 'Managed by ops',
      fingerprint: ['****wd42', 'SHA256:abc', 'AKIA****', '****tk'][(i - 1) % 4],
      fields: [['password'], ['privateKey'], ['accessKeyId', 'secretAccessKey'], ['token']][(i - 1) % 4],
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
      exposeMcp: true,
      exposeA2A: true,
      exposeOpenAI: i === 1,
      maxIterations: 12,
      bindingCount: i === 1 ? 2 : 0
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
      bindingAlias: 'web-01',
      principalName: 'admin@hugo.local',
      toolName: ['ssh_exec', 'sql_query', 'logs_search'][(i - 1) % 3],
      effect: ['Read', 'Write', 'Destructive'][(i - 1) % 3],
      decision: ['Allowed', 'Allowed', 'Approved'][(i - 1) % 3],
      status: i % 5 === 0 ? 'Failed' : 'Succeeded',
      durationMs: 120 + i,
      argumentsRedacted: JSON.stringify({ command: 'uptime', password: '[redacted:password]' }),
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
      argumentsRedacted: JSON.stringify({ command: 'sudo systemctl restart nginx' }),
      bindingAlias: 'web-01',
      resourceId: 'res_0001',
      toolName: 'ssh_exec',
      surface: 'Chat',
      status: i <= 2 ? 'Pending' : 'Approved',
      requestedByName: 'Agent Smith (chat)',
      expiresUtc: new Date(Date.now() + 15 * 60000).toISOString()
    }),
  sessions: (i) =>
    record('ses', i, {
      tenantId: TENANT,
      principalType: i === 3 ? 'Credential' : 'User',
      userId: i === 1 ? 'usr_0001' : 'usr_0002',
      credentialId: i === 3 ? 'cred_0001' : null,
      principalName: i === 3 ? 'claude-code-1' : i === 1 ? 'admin@hugo.local' : 'anderson@hugo.local',
      authScheme: i === 1 ? 'BearerToken' : i === 3 ? 'AccessKeySecret' : 'PasswordHeaders',
      sourceIp: '127.0.0.1',
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

/** Garrison enumeration envelope over prepared rows (every list route of the server). */
function enumerateRows(rows, url) {
  const maxResults = Number(url.searchParams.get('maxResults') || 25);
  const skip = Number(url.searchParams.get('skip') || 0);
  const objects = rows.slice(skip, skip + maxResults);
  return {
    success: true,
    maxResults,
    skip,
    totalRecords: rows.length,
    recordsRemaining: Math.max(0, rows.length - skip - objects.length),
    endOfResults: skip + objects.length >= rows.length,
    objects
  };
}

/** Resource health from the last connection test, as the server's health filter sees it. */
const resourceHealthOf = (r) => (r.lastTestResult === true ? 'Healthy' : r.lastTestResult === false ? 'Unhealthy' : 'Unknown');

/** List filters each flat route applies (plan section 18.3): query parameter to a row predicate. */
const LIST_FILTERS = {
  approvals: ['status', 'effect', 'agentProfileId', 'resourceId', 'principalId', 'toolName', 'fromUtc', 'toUtc'],
  'tool-invocations': [
    'effect',
    'agentProfileId',
    'resourceId',
    'toolName',
    'decision',
    'status',
    'surface',
    'conversationId',
    'principalId',
    'fromUtc',
    'toUtc'
  ],
  resources: ['connectorType', 'environment', 'health'],
  secrets: ['kind'],
  'model-endpoints': ['kind', 'provider', 'health'],
  sessions: ['principalType', 'userId', 'credentialId']
};

function matches(collection, row, name, value) {
  if (name === 'fromUtc') return Date.parse(row.createdUtc) >= Date.parse(value);
  if (name === 'toUtc') return Date.parse(row.createdUtc) < Date.parse(value);
  if (name === 'environment') return String(row.environment || '').toLowerCase() === value.toLowerCase();
  if (name === 'health') return (collection === 'resources' ? resourceHealthOf(row) : row.healthStatus || 'Unknown') === value;
  return row[name] === value;
}

function rows(collection, url) {
  const total = TOTALS[collection] ?? 6;
  let list = Array.from({ length: total }, (_, i) => FACTORIES[collection](i + 1));
  const params = { ...Object.fromEntries(url.searchParams) };
  if (collection === 'tool-invocations' && !params.fromUtc && params.since) params.fromUtc = params.since;
  for (const name of LIST_FILTERS[collection] || []) {
    const value = params[name];
    if (value) list = list.filter((r) => matches(collection, r, name, value));
  }
  return list;
}

/** Recent health checks of a model endpoint, newest first (src/Hugo.Core/Models/ModelEndpointHealthRecord.cs). */
function healthHistory(id, url) {
  const max = Math.min(1000, Math.max(1, Number(url.searchParams.get('maxResults') || 50)));
  return [
    { source: 'Validate', status: 'Healthy', latencyMs: 210, message: 'Responded in 210 ms', checkedUtc: iso(5) },
    { source: 'Sweep', status: 'Unhealthy', latencyMs: null, message: 'Connection refused', checkedUtc: iso(65) },
    { source: 'Sweep', status: 'Healthy', latencyMs: 180, message: 'Responded in 180 ms', checkedUtc: iso(125) }
  ]
    .slice(0, max)
    .map((r, i) => ({ id: `mhh_${pad(i + 1)}`, tenantId: TENANT, modelEndpointId: id, ...r }));
}

/** An analytics report (src/Hugo.Core/Responses/AnalyticsReport.cs): every bucket filled, with per-kind rows. */
function analyticsReport(kind, url) {
  const interval = url.searchParams.get('interval') || 'hour';
  const step = { minute: 60000, hour: 3600000, day: 86400000 }[interval] || 3600000;
  const buckets = Array.from({ length: 8 }, (_, i) => ({
    startUtc: new Date(NOW - (7 - i) * step).toISOString(),
    turns: 4 + i,
    inputTokens: 1000 + i * 50,
    outputTokens: 200 + i * 10,
    toolCalls: 10 + i,
    toolErrors: i % 2,
    approvals: i % 3,
    avgTimeToFirstTokenMs: i % 4 === 0 ? null : 400 + i * 10,
    avgTurnDurationMs: i % 4 === 0 ? null : 2400 + i * 100
  }));
  return {
    kind,
    fromUtc: buckets[0].startUtc,
    toUtc: iso(0),
    interval,
    conversations: 12,
    approvalsRequested: 6,
    totals: {
      startUtc: buckets[0].startUtc,
      turns: 42,
      inputTokens: 52000,
      outputTokens: 9100,
      toolCalls: 180,
      toolErrors: 7,
      approvals: 6,
      avgTimeToFirstTokenMs: 450,
      avgTurnDurationMs: 2800
    },
    buckets,
    tools:
      kind === 'tools'
        ? [
            { toolName: 'ssh_exec', connectorType: 'ssh', calls: 90, errors: 3, denied: 1, approvals: 4, avgDurationMs: 1200 },
            { toolName: 'sql_query', connectorType: 'postgresql', calls: 70, errors: 1, denied: 0, approvals: 2, avgDurationMs: 300 }
          ]
        : null,
    models:
      kind === 'models'
        ? [
            {
              modelEndpointId: 'mdl_0001',
              name: 'local-llm',
              provider: 'Anthropic',
              model: 'claude-sonnet',
              turns: 42,
              inputTokens: 52000,
              outputTokens: 9100,
              avgTimeToFirstTokenMs: 450,
              avgTurnDurationMs: 2800
            }
          ]
        : null
  };
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
    },
    {
      path: 'Connectors.McpAllowStdio',
      group: 'Connectors',
      description: 'Whether mcp resources may launch local MCP servers over stdio (each resource must also set allowStdio).',
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
    tenantId: TENANT,
    agentProfileId: 'prf_0001',
    resourceId: 'res_0002',
    resourceName: 'prod-db',
    connectorType: 'postgresql',
    alias: 'prod-db',
    accessMode: 'ReadOnly',
    requireApprovalForDestructive: true,
    instructionMode: 'Inherit',
    enabled: true,
    allowKnowledgeUpdates: true,
    toolRules: [],
    denyPatterns: []
  },
  {
    id: 'bnd_0002',
    tenantId: TENANT,
    agentProfileId: 'prf_0001',
    resourceId: 'res_0001',
    resourceName: 'web-01',
    connectorType: 'ssh',
    alias: 'web-01',
    accessMode: 'ReadWriteWithApproval',
    requireApprovalForDestructive: true,
    instructionMode: 'Append',
    instructionText: 'Never restart payments.',
    enabled: true,
    allowKnowledgeUpdates: true,
    toolRules: [{ toolName: 'ssh_exec', rule: 'RequireApproval' }],
    denyPatterns: ['rm -rf']
  }
];

/** Tools as GET /agent-profiles/{id}/tools lists them (src/Hugo.Core/Connectors/CallerTool.cs). */
const PROFILE_TOOLS = [
  {
    name: 'sql_query',
    family: 'sql',
    title: 'Run SQL',
    description: 'Run a SQL query.',
    baseEffect: 'Read',
    classifiedPerCall: true,
    inputSchemaJson: JSON.stringify({
      type: 'object',
      required: ['resource', 'sql'],
      properties: { resource: { type: 'string', enum: ['prod-db'] }, sql: { type: 'string', description: 'The query.' } }
    }),
    isMetaTool: false,
    resources: [{ alias: 'prod-db', connectorType: 'postgresql', accessMode: 'ReadOnly' }]
  },
  {
    name: 'ssh_exec',
    family: 'ssh',
    title: 'Run a command',
    description: 'Run a command.',
    baseEffect: 'Read',
    classifiedPerCall: true,
    inputSchemaJson: JSON.stringify({
      type: 'object',
      required: ['resource', 'command'],
      properties: { resource: { type: 'string', enum: ['web-01'] }, command: { type: 'string' } }
    }),
    isMetaTool: false,
    resources: [{ alias: 'web-01', connectorType: 'ssh', accessMode: 'ReadWriteWithApproval', rule: 'RequireApproval' }]
  }
];

const COMPILED_TEXT =
  '# Agent Smith\nYou are Agent Smith. Address the user as Mr. Anderson.\n\n## prod-db (PostgreSQL, access: ReadOnly)\nUse sql_list_tables first.\n';
/** Compiled instructions with the compiler's source map (src/Hugo.Core/Instructions/CompiledInstructions.cs). */
const COMPILED = {
  profileId: 'prf_0001',
  profileName: 'Hugo',
  surface: 'Chat',
  text: COMPILED_TEXT,
  tokenEstimate: 42,
  bindingCount: 1,
  warnings: [],
  segments: [
    { key: 'instructions.root', layer: 'Global', version: 1, start: 0, length: COMPILED_TEXT.length, depth: 0 },
    { key: 'persona.agent-smith', layer: 'BuiltIn', version: 0, start: 0, length: 70, depth: 1 },
    { key: 'section.resource', layer: 'Global', version: 1, start: 72, length: COMPILED_TEXT.length - 72, depth: 1 },
    { key: 'connector.postgresql.instructions', layer: 'Tenant', version: 2, bindingAlias: 'prod-db', start: 115, length: COMPILED_TEXT.length - 115, depth: 2 }
  ]
};

/** Prompt template views (src/Hugo.Core/Prompts/PromptTemplateView.cs). */
const PROMPT_VIEWS = [
  {
    key: 'connector.ssh.instructions',
    description: 'Default instructions for SSH resources.',
    source: 'ssh',
    activeLayer: 'Tenant',
    effectiveContent: '# SSH resources\nPrefer read-only commands. Never restart payments.',
    builtInContent: '# SSH resources\nPrefer read-only commands.',
    builtInHash: 'abc123',
    global: {
      layer: 'Global',
      exists: true,
      templateId: 'tpl_1',
      content: '# SSH resources\nPrefer read-only commands.',
      version: 1,
      customized: false,
      defaultUpdatedSinceCustomized: false,
      lastUpdateUtc: iso(600)
    },
    tenant: {
      layer: 'Tenant',
      exists: true,
      templateId: 'tpl_2',
      content: '# SSH resources\nPrefer read-only commands. Never restart payments.',
      version: 2,
      customized: true,
      defaultUpdatedSinceCustomized: true,
      lastUpdateUtc: iso(60)
    }
  },
  {
    key: 'persona.agent-smith',
    description: 'Default persona.',
    source: 'hugo',
    activeLayer: 'Global',
    effectiveContent: 'You are {{profile.personaName}}.',
    builtInContent: 'You are {{profile.personaName}}.',
    builtInHash: 'def456',
    global: {
      layer: 'Global',
      exists: true,
      templateId: 'tpl_3',
      content: 'You are {{profile.personaName}}.',
      version: 1,
      customized: false,
      defaultUpdatedSinceCustomized: false,
      lastUpdateUtc: iso(900)
    },
    tenant: null
  },
  {
    key: 'system.safety',
    description: 'Safety rules.',
    source: 'hugo',
    activeLayer: 'Global',
    effectiveContent: 'Respect access modes.',
    builtInContent: 'Respect access modes.',
    builtInHash: 'ghi789',
    global: {
      layer: 'Global',
      exists: true,
      templateId: 'tpl_4',
      content: 'Respect access modes.',
      version: 1,
      customized: false,
      defaultUpdatedSinceCustomized: false,
      lastUpdateUtc: iso(900)
    },
    tenant: null
  }
];

/** Resource knowledge entries (src/Hugo.Core/Models/ResourceKnowledge.cs). */
const KNOWLEDGE = [
  {
    id: 'rkn_1',
    tenantId: TENANT,
    resourceId: 'res_0002',
    scope: 'database',
    key: '_root',
    content: 'Shop database. Orders belong to customers.',
    source: 'Generated',
    updatedBy: 'Hugo profile',
    version: 1,
    createdUtc: iso(100),
    lastUpdateUtc: iso(100)
  },
  {
    id: 'rkn_2',
    tenantId: TENANT,
    resourceId: 'res_0002',
    scope: 'table',
    key: 'public.orders',
    content: 'One row per order.',
    source: 'Agent',
    updatedBy: 'Agent Smith',
    version: 2,
    createdUtc: iso(90),
    lastUpdateUtc: iso(30)
  }
];

const KNOWLEDGE_VERSIONS = [
  {
    id: 'rkv_2',
    knowledgeId: 'rkn_2',
    scope: 'table',
    key: 'public.orders',
    version: 2,
    content: 'One row per order.',
    source: 'Agent',
    updatedBy: 'Agent Smith',
    deleted: false,
    createdUtc: iso(30)
  },
  {
    id: 'rkv_1',
    knowledgeId: 'rkn_2',
    scope: 'table',
    key: 'public.orders',
    version: 1,
    content: 'Orders.',
    source: 'Operator',
    updatedBy: 'admin@hugo.local',
    deleted: false,
    createdUtc: iso(90)
  }
];

const RELATIONSHIPS = [
  {
    fromSchema: 'public',
    fromTable: 'orders',
    fromColumn: 'customer_id',
    toSchema: 'public',
    toTable: 'customers',
    toColumn: 'id',
    constraintName: 'fk_orders_customer',
    source: 'declared_fk',
    confidence: 1,
    evidence: ['Declared foreign key constraint fk_orders_customer.'],
    isDeclared: true
  },
  {
    fromSchema: 'public',
    fromTable: 'order_items',
    fromColumn: 'order_id',
    toSchema: 'public',
    toTable: 'orders',
    toColumn: 'id',
    source: 'inferred_name_match',
    confidence: 0.9,
    evidence: ['Column order_id is named after table orders.'],
    isDeclared: false
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

/** MCP tools/list entries: the same tools with their input schema as an object (MCP protocol shape). */
const MCP_TOOLS = PROFILE_TOOLS.map((tool) => ({ name: tool.name, description: tool.description, inputSchema: JSON.parse(tool.inputSchemaJson) }));

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
  feedback: '/v1.0/api/feedback',
  approvals: '/v1.0/api/approvals',
  sessions: '/v1.0/api/sessions',
  prompts: '/v1.0/api/prompt-templates',
  lockdown: '/v1.0/api/tenant/lockdown',
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
  if (p === '/v1.0/api/feedback') return { objects: [], totalRecords: 0, maxResults: 25, skip: 0 };
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
  if (p === '/v1.0/api/tenant/lockdown') return { tenantId: TENANT, lockdown: method === 'POST', approvalsCancelled: 0 };
  if (p === '/v1.0/api/request-history/summary') return summary();
  if (p === '/v1.0/api/request-history') return legacyPage('request-history', url);
  if (p.startsWith('/v1.0/api/request-history/')) return FACTORIES['request-history'](1);
  if (p === '/v1.0/api/users/me/password') return { ...FACTORIES.users(1), mustChangePassword: false };

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
    if (!FACTORIES[collection]) return undefined;
    if (parts.length === 1) return method === 'GET' ? enumerate(collection, url) : FACTORIES[collection](99);
    return FACTORIES[collection](1);
  }

  // Hugo routes (flat, plan section 14.2; shapes from src/Hugo.Core).
  if (p === '/v1.0/api/connectors') return CONNECTORS;
  m = p.match(/^\/v1\.0\/api\/connectors\/([^/]+)$/);
  if (m) return CONNECTORS.find((c) => c.type === m[1]) || undefined;
  if (p === '/v1.0/api/resources/test')
    return { success: true, message: 'Connected to web-01 as ops in 42 ms.', latencyMs: 42, details: {}, validationErrors: [], learnedSettings: {} };
  if (p === '/v1.0/api/tool-invocations/summary')
    return {
      total: 37,
      byDecision: [
        { status: 'Allowed', count: 30 },
        { status: 'Approved', count: 5 },
        { status: 'Denied', count: 2 }
      ],
      byStatus: [
        { status: 'Succeeded', count: 30 },
        { status: 'Failed', count: 7 }
      ],
      byEffect: [
        { status: 'Read', count: 30 },
        { status: 'Write', count: 5 },
        { status: 'Destructive', count: 2 }
      ],
      byTool: [
        { status: 'ssh_exec', count: 20 },
        { status: 'sql_query', count: 17 }
      ]
    };
  if (p === '/v1.0/api/tool-invocations/dry-run') return outcome(body, true);
  if (p === '/v1.0/api/tool-invocations' && method === 'POST') return outcome(body, false);
  m = p.match(/^\/v1\.0\/api\/analytics\/(overview|tokens|tools|models)$/);
  if (m) return analyticsReport(m[1], url);
  m = p.match(/^\/v1\.0\/api\/agent-profiles\/([^/]+)\/(bindings|tools|compiled-instructions|card)(?:\/([^/]+))?(?:\/(tool-rules))?$/);
  if (m) {
    if (m[2] === 'bindings' && !m[3]) return method === 'GET' ? BINDINGS : { ...BINDINGS[0], id: 'bnd_0099', ...(body || {}) };
    if (m[2] === 'bindings' && m[4]) return method === 'GET' ? [{ toolName: 'ssh_exec', rule: 'RequireApproval' }] : { ...BINDINGS[1], toolRules: body || [] };
    if (m[2] === 'bindings') return BINDINGS[0];
    if (m[2] === 'tools') return { profileId: m[1], profileName: 'Hugo', hasModel: true, resourceCount: 2, tools: PROFILE_TOOLS, metaTools: [] };
    if (m[2] === 'card') return AGENT_CARD;
    return COMPILED;
  }
  m = p.match(/^\/v1\.0\/api\/secrets\/([^/]+)\/(reveal|referrers|values)$/);
  if (m) {
    if (m[2] === 'reveal') return { values: { password: 'swordfish' } };
    if (m[2] === 'referrers') return [{ referrerType: 'Resource', referrerId: 'res_0002', referrerName: 'prod-db', detail: 'slot password' }];
    return FACTORIES.secrets(1);
  }
  m = p.match(/^\/v1\.0\/api\/resources\/([^/]+)\/(test|referrers|duplicate|invoke)$/);
  if (m) {
    if (m[2] === 'test') return { success: true, message: 'Connected in 42 ms.', latencyMs: 42, details: {}, validationErrors: [], learnedSettings: {} };
    if (m[2] === 'referrers')
      return [
        { agentProfileId: 'prf_0001', agentProfileName: 'Hugo', bindingId: 'bnd_0002', alias: 'web-01', accessMode: 'ReadWriteWithApproval', enabled: true }
      ];
    if (m[2] === 'invoke') return knowledgeTool(body);
    return FACTORIES.resources(9);
  }
  m = p.match(/^\/v1\.0\/api\/resources\/([^/]+)\/knowledge(?:\/([^/]+))?(?:\/([^/]+))?(\/versions)?$/);
  if (m) {
    if (m[4]) return KNOWLEDGE_VERSIONS;
    if (method === 'PUT')
      return {
        ...KNOWLEDGE[0],
        scope: m[2],
        key: m[3] === '_root' || !m[3] ? '_root' : decodeURIComponent(m[3]),
        content: body?.content,
        source: 'Operator',
        version: 2
      };
    if (m[3]) return KNOWLEDGE.find((k) => k.scope === m[2]) || KNOWLEDGE[0];
    return m[2] ? KNOWLEDGE.filter((k) => k.scope === m[2]) : KNOWLEDGE;
  }
  m = p.match(/^\/v1\.0\/api\/model-endpoints\/([^/]+)\/(validate|models|health-history)$/);
  if (m) {
    if (m[2] === 'health-history') return healthHistory(m[1], url);
    return m[2] === 'validate' ? { success: true, kind: 'Inference', latencyMs: 210, model: 'claude-sonnet', problems: [] } : ['claude-sonnet', 'claude-haiku'];
  }
  m = p.match(/^\/v1\.0\/api\/approvals\/([^/]+)\/(approve|reject|cancel)$/);
  if (m) return { ...FACTORIES.approvals(1), id: m[1], status: m[2] === 'approve' ? 'Approved' : m[2] === 'reject' ? 'Rejected' : 'Cancelled' };
  m = p.match(/^\/v1\.0\/api\/approvals\/([^/]+)$/);
  if (m) return { ...FACTORIES.approvals(1), id: m[1], invocation: FACTORIES['tool-invocations'](1) };
  m = p.match(/^\/v1\.0\/api\/conversations\/([^/]+)\/(messages|cancel|export)$/);
  if (m) {
    if (m[2] === 'messages' && method === 'GET') return enumerateRows(MESSAGES, url);
    if (m[2] === 'export') return '# Why is checkout slow?\n';
    return {};
  }
  // Prompt templates, layered by key (plan section 10.2).
  if (p === '/v1.0/api/prompt-templates') return PROMPT_VIEWS;
  if (p === '/v1.0/api/prompt-templates/render')
    return { rendered: 'You are Agent Smith. Address the user as Mr. Anderson.', success: true, variables: ['profile.personaName'], includes: [], errors: [] };
  m = p.match(/^\/v1\.0\/api\/prompt-templates\/([^/]+)(?:\/(reset|versions|builtin|diff))?$/);
  if (m) {
    const view = PROMPT_VIEWS.find((v) => v.key === decodeURIComponent(m[1]));
    if (!view) return undefined;
    if (m[2] === 'versions')
      return [2, 1].map((v) => ({
        id: `tpv_${v}`,
        templateId: 'tpl_1',
        version: v,
        content: `Version ${v} text`,
        changeNote: `v${v}`,
        createdBy: 'admin@hugo.local',
        createdUtc: iso(v * 60)
      }));
    if (m[2] === 'builtin') return { key: view.key, content: view.builtInContent, hash: view.builtInHash, source: view.source, description: view.description };
    if (m[2] === 'diff')
      return [
        { op: 'same', text: '# SSH resources' },
        { op: 'remove', text: 'Prefer read-only commands.' },
        { op: 'add', text: 'Prefer read-only commands. Never restart payments.' }
      ];
    if (m[2] === 'reset') return { ...view, activeLayer: 'Global', tenant: { ...view.tenant, exists: false, customized: false } };
    if (method === 'PUT') {
      const layer = url.searchParams.get('layer') || 'Tenant';
      const state = { layer, exists: true, content: body?.content, version: 3, customized: true, defaultUpdatedSinceCustomized: false, lastUpdateUtc: iso(0) };
      return {
        ...view,
        activeLayer: layer === 'Global' && view.tenant?.exists ? 'Tenant' : layer,
        effectiveContent: body?.content,
        [layer === 'Global' ? 'global' : 'tenant']: state
      };
    }
    return view;
  }
  if (p === '/v1.0/api/conversations' && method === 'POST') return { ...FACTORIES.conversations(1), id: 'cnv_0042', title: null, messageCount: 0 };
  if (p === '/v1.0/api/agent-profiles' && method === 'POST') return { ...FACTORIES['agent-profiles'](9), ...(body || {}) };
  m = p.match(
    /^\/v1\.0\/api\/(resources|secrets|agent-profiles|model-endpoints|conversations|tool-invocations|approvals|sessions|knowledge|system\/plugins)(?:\/([^/]+))?$/
  );
  if (m) {
    const collection = m[1];
    if (!m[2]) return method === 'GET' ? enumerateRows(rows(collection, url), url) : { ...FACTORIES[collection](99), ...(body || {}) };
    const n = Number(String(m[2]).replace(/^\D+_/, '')) || 1;
    return { ...FACTORIES[collection](n), id: m[2] };
  }
  return undefined;
}

/** A tool invocation outcome for dry run and invoke (src/Hugo.Core/Responses/ToolInvocationOutcome.cs). */
function outcome(body, dryRun) {
  const sql = String(body?.arguments?.sql || body?.arguments?.command || '');
  const write = /^\s*(insert|update|delete|drop|restart)/i.test(sql);
  const base = {
    dryRun,
    toolName: body?.toolName,
    profileId: body?.profileId,
    bindingAlias: body?.resource,
    resourceId: 'res_0001',
    connectorType: 'ssh',
    effect: write ? 'Write' : 'Read',
    effectReason: write ? 'statement type INSERT' : 'uptime is read only',
    accessMode: 'ReadWriteWithApproval',
    approvalSummary: `Run on ${body?.resource}`,
    durationMs: dryRun ? 0 : 12,
    approvalWaitMs: 0
  };
  if (write)
    return {
      ...base,
      invocationId: dryRun ? undefined : 'tin_0101',
      decision: 'ApprovalRequired',
      decisionCode: 'approval_required',
      reason: 'Write calls on this resource require approval.',
      status: dryRun ? 'NotExecuted' : 'Pending',
      approvalId: dryRun ? undefined : 'apr_0101',
      approvalStatus: dryRun ? undefined : 'Pending'
    };
  return {
    ...base,
    invocationId: dryRun ? undefined : 'tin_0100',
    decision: 'Allowed',
    decisionCode: 'allowed',
    reason: 'Read call.',
    status: dryRun ? 'NotExecuted' : 'Succeeded',
    result: dryRun ? undefined : { isError: false, text: ' 12:00 up 41 days', mimeType: 'text/plain', truncated: false, originalBytes: 18, metrics: {} }
  };
}

/** Direct resource invocations used by the Knowledge tab (sql_get_context, sql_get_intelligence, sql_list_relationships). */
function knowledgeTool(body) {
  const result = (structured) => ({
    invocationId: 'tin_0200',
    toolName: body?.toolName,
    decision: 'Allowed',
    status: 'Succeeded',
    result: { isError: false, text: 'ok', structuredJson: JSON.stringify(structured), mimeType: 'text/plain', truncated: false, originalBytes: 2, metrics: {} }
  });
  if (body?.toolName === 'sql_get_context')
    return result({ schema: 'public', tables: [], tablesWithoutContext: ['public.customers', 'public.order_items'], totalRecords: 3 });
  if (body?.toolName === 'sql_list_relationships') return result({ schema: 'public', items: RELATIONSHIPS });
  if (body?.toolName === 'sql_get_intelligence')
    return result({
      engine: 'postgresql',
      schema: 'public',
      crawledUtc: iso(5),
      domain: {
        summary: 'Crawled 3 tables with 1 declared and 1 inferred relationship.',
        entities: [
          { schemaName: 'public', tableName: 'orders', rowEstimate: 1200, role: 'core entity', keyColumns: ['id', 'customer_id', 'total'], hasContext: true },
          { schemaName: 'public', tableName: 'customers', rowEstimate: 300, role: 'entity', keyColumns: ['id', 'email'], hasContext: false }
        ],
        metricDetails: [{ column: 'public.orders.total', dataType: 'numeric', aggregations: ['SUM', 'AVG'] }],
        commonFilters: ['public.orders.status'],
        freshnessColumns: ['public.orders.created_at'],
        tenantColumns: [],
        softDeleteColumns: []
      },
      relationships: RELATIONSHIPS,
      ambiguities: [{ term: 'revenue', question: 'Which column defines revenue?', candidates: ['public.orders.total'] }],
      contextQuality: {
        score: 48,
        label: 'Needs context',
        tablesWithContext: 1,
        totalTables: 3,
        declaredRelationships: 1,
        inferredRelationships: 1,
        signals: [
          {
            key: 'table_context',
            severity: 'warning',
            message: 'Only 1 of 3 tables have saved context.',
            recommendation: 'Build table context for core tables first.'
          }
        ]
      },
      suggestedQuestions: ['What is the total public.orders.total by month?']
    });
  return result({});
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
        body: JSON.stringify({ error: 'NotFound', code: 'RouteNotFound', message: 'No route matches the request.', requestId: 'req_404' })
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
      // Like the server (ChatMessageRequest), the turn text travels in "message".
      if (!body || typeof body.message !== 'string' || !body.message.trim()) {
        return route.fulfill({
          status: 400,
          contentType: 'application/json',
          body: JSON.stringify({ error: 'BadRequest', code: 'MessageRequired', message: 'A message is required.', requestId: 'req_400' })
        });
      }
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
