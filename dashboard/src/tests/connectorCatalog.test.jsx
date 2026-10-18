// Wave 2 connectors (P13.32) are first-class in the dashboard: each built-in type has its own icon, and every
// setting, enum value, secret slot, and schema group the server descriptors declare has a label in every catalog.
// The key lists mirror the descriptors in src/Hugo.Connectors.{Redis,Aws,Kubernetes,Docker,Http,Observability,Agents}.

import { describe, it, expect } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { en, TRANSLATIONS } from '../i18n/resources';
import ConnectorIcon, { CONNECTOR_TYPE_ICONS, CONNECTOR_ICON_KEYS, connectorIconFor } from '../components/ConnectorIcon';
import { PlugIcon } from '../components/Icons';
import { connectorSlotLabel } from '../utils/tips';

const WAVE2 = {
  cryptography: {
    settings: ['algorithm', 'keyEncoding', 'keyDerivation', 'iterations', 'derivedKeyBytes', 'maxInputBytes'],
    values: {
      algorithm: ['AES-GCM', 'AES-CBC'],
      keyEncoding: ['base64', 'hex'],
      keyDerivation: ['None', 'PBKDF2-SHA256'],
      derivedKeyBytes: ['16', '24', '32']
    },
    slots: ['encryptionKey', 'initializationVector']
  },
  redis: {
    settings: [
      'endpoints',
      'clusterMode',
      'sentinelServiceName',
      'database',
      'username',
      'useSsl',
      'sslHost',
      'verifyTls',
      'caCertificate',
      'connectTimeoutSeconds',
      'commandTimeoutSeconds',
      'clientName',
      'maxKeysScanned',
      'maxValueBytes',
      'deniedCommands',
      'allowKeysCommand',
      'readOnlyCommands'
    ],
    values: { clusterMode: ['Auto', 'Standalone', 'Cluster', 'Sentinel'] },
    slots: ['password']
  },
  aws: {
    settings: [
      'region',
      'useInstanceRole',
      'assumeRoleArn',
      'externalId',
      'sessionName',
      'assumeRoleDurationSeconds',
      'allowedServices',
      'endpointOverride',
      'maxResults',
      'maxRetries',
      'defaultLookbackMinutes',
      'maxTimeRangeHours',
      'logsQueryTimeoutSeconds',
      'logsMaxEvents',
      'lambdaMaxPayloadBytes'
    ],
    values: {},
    slots: ['accessKeyPair']
  },
  kubernetes: {
    settings: [
      'context',
      'serverUrl',
      'insecureSkipTlsVerify',
      'requestTimeoutSeconds',
      'allowedNamespaces',
      'defaultNamespace',
      'clusterScopedReads',
      'allowClusterScoped',
      'destructiveKinds',
      'fieldManager',
      'maxLogLines',
      'maxObjects',
      'execTimeoutSeconds'
    ],
    values: {},
    slots: ['kubeconfig', 'serviceAccountToken', 'certificateAuthority']
  },
  docker: {
    settings: [
      'endpoint',
      'tlsVerify',
      'requestTimeoutSeconds',
      'showEnvironmentValues',
      'maxLogLines',
      'maxEventsWindowSeconds',
      'execTimeoutSeconds',
      'maxStatsContainers'
    ],
    values: {},
    slots: ['tlsClientCertificate']
  },
  http: {
    settings: [
      'baseUrl',
      'healthPath',
      'defaultHeaders',
      'timeoutSeconds',
      'followRedirects',
      'maxRedirects',
      'verifyTls',
      'authType',
      'apiKeyHeaderName',
      'tokenUrl',
      'scopes',
      'allowedMethods',
      'allowedPathPrefixes',
      'methodEffectOverrides',
      'maxResponseBytes',
      'maxRequestBytes'
    ],
    values: { authType: ['None', 'Basic', 'Bearer', 'ApiKeyHeader', 'OAuth2ClientCredentials'] },
    slots: ['basicAuth', 'bearerToken', 'apiKey', 'clientCredentials']
  },
  prometheus: {
    settings: [
      'baseUrl',
      'authType',
      'apiKeyHeaderName',
      'apiKeyPrefix',
      'tokenUrl',
      'scopes',
      'defaultHeaders',
      'verifyTls',
      'maxResponseBytes',
      'maxSeries',
      'maxPointsPerSeries',
      'maxRangeHours'
    ],
    values: { authType: ['None', 'Basic', 'Bearer', 'ApiKeyHeader', 'OAuth2ClientCredentials'] },
    slots: ['basicAuth', 'bearerToken', 'apiKey', 'clientCredentials']
  },
  loki: {
    settings: [
      'baseUrl',
      'authType',
      'apiKeyHeaderName',
      'apiKeyPrefix',
      'tokenUrl',
      'scopes',
      'defaultHeaders',
      'verifyTls',
      'maxResponseBytes',
      'tenantHeader',
      'maxLines',
      'maxRangeHours'
    ],
    values: { authType: ['None', 'Basic', 'Bearer', 'ApiKeyHeader', 'OAuth2ClientCredentials'] },
    slots: ['basicAuth', 'bearerToken', 'apiKey', 'clientCredentials']
  },
  elasticsearch: {
    settings: [
      'baseUrl',
      'authType',
      'apiKeyHeaderName',
      'apiKeyPrefix',
      'tokenUrl',
      'scopes',
      'defaultHeaders',
      'verifyTls',
      'maxResponseBytes',
      'flavor',
      'allowedIndexPatterns',
      'maxHits'
    ],
    values: { authType: ['None', 'Basic', 'Bearer', 'ApiKeyHeader', 'OAuth2ClientCredentials'], flavor: ['Elasticsearch', 'OpenSearch'] },
    slots: ['basicAuth', 'bearerToken', 'apiKey', 'clientCredentials']
  },
  mcp: {
    settings: ['transport', 'url', 'command', 'arguments', 'allowStdio', 'includeTools', 'excludeTools', 'refreshIntervalMinutes', 'timeoutSeconds'],
    values: { transport: ['StreamableHttp', 'Stdio'] },
    slots: ['authorizationHeader', 'extraHeaders']
  },
  'a2a-agent': {
    settings: ['agentCardUrl', 'skillId', 'timeoutSeconds', 'streaming', 'allowCrossOriginEndpoint'],
    values: {},
    slots: ['bearerToken']
  }
};

const WAVE1 = ['ssh', 's3', 'azure-blob', 'gcs', 'sqlite', 'mysql', 'sqlserver', 'postgresql', 'solarwinds-observability'];
const WAVE2_ICON_KEYS = ['cache', 'cloud', 'cluster', 'container', 'globe', 'chart', 'logs', 'search', 'plug', 'bot', 'shield'];
const WAVE2_GROUPS = ['Connection', 'Authentication', 'Limits', 'TLS', 'Safety', 'Scope', 'Search', 'Security', 'Execution', 'Credentials'];
const CATALOGS = [['en', en], ...Object.entries(TRANSLATIONS)];

function lookup(catalog, key) {
  return key.split('.').reduce((n, part) => (n && typeof n === 'object' ? n[part] : undefined), catalog);
}

function labelKeys(type, def) {
  const keys = [];
  for (const key of def.settings) keys.push(`connectors.${type}.settings.${key}.label`);
  for (const [key, values] of Object.entries(def.values)) for (const v of values) keys.push(`connectors.${type}.settings.${key}.values.${v}`);
  for (const slot of def.slots) keys.push(`connectors.${type}.slots.${slot}.label`);
  return keys;
}

describe('connector icons', () => {
  it.each(Object.keys(WAVE2))('%s has its own icon', (type) => {
    expect(CONNECTOR_TYPE_ICONS[type]).toBeTypeOf('function');
    const html = renderToStaticMarkup(<ConnectorIcon type={type} />);
    expect(html).toContain('<svg');
    expect(html).toContain(`data-connector-icon="${type}"`);
  });

  it('gives wave 1 connectors an icon too', () => {
    for (const type of WAVE1) expect(CONNECTOR_TYPE_ICONS[type], type).toBeTypeOf('function');
  });

  it('maps every descriptor iconKey wave 2 uses, and falls back to the plug for unknown connectors', () => {
    for (const key of WAVE2_ICON_KEYS) expect(CONNECTOR_ICON_KEYS[key], key).toBeTypeOf('function');
    expect(connectorIconFor('some-plugin', 'cloud')).toBe(CONNECTOR_ICON_KEYS.cloud);
    expect(connectorIconFor('some-plugin', 'unknown')).toBe(PlugIcon);
    expect(connectorIconFor(undefined, undefined)).toBe(PlugIcon);
  });

  it('gives most wave 2 connectors a distinct icon', () => {
    const distinct = new Set(Object.keys(WAVE2).map((type) => CONNECTOR_TYPE_ICONS[type]));
    expect(distinct.size).toBeGreaterThanOrEqual(8);
  });
});

describe('wave 2 connector labels', () => {
  it.each(CATALOGS)('%s labels every wave 2 setting, enum value, and secret slot', (_code, catalog) => {
    const missing = [];
    for (const [type, def] of Object.entries(WAVE2)) {
      for (const key of labelKeys(type, def)) {
        const value = lookup(catalog, key);
        if (typeof value !== 'string' || value.trim() === '') missing.push(key);
      }
    }
    expect(missing).toEqual([]);
  });

  it.each(CATALOGS)('%s labels every settings group wave 2 descriptors use', (_code, catalog) => {
    const missing = WAVE2_GROUPS.filter((g) => typeof lookup(catalog, `components.schemaForm.groups.${g}`) !== 'string');
    expect(missing).toEqual([]);
  });

  it('labels no setting the descriptors do not declare', () => {
    const extra = [];
    for (const [type, block] of Object.entries(en.connectors)) {
      const def = WAVE2[type];
      if (!def) {
        extra.push(type);
        continue;
      }
      for (const key of Object.keys(block.settings || {})) if (!def.settings.includes(key)) extra.push(`${type}.${key}`);
      for (const slot of Object.keys(block.slots || {})) if (!def.slots.includes(slot)) extra.push(`${type}.slots.${slot}`);
    }
    expect(extra).toEqual([]);
  });

  it('resolves slot labels from the catalog, then the server label, then the slot name', () => {
    const t = (key) => {
      const value = lookup(en, key);
      return typeof value === 'string' ? value : key;
    };
    expect(connectorSlotLabel(t, 'kubernetes', { name: 'serviceAccountToken', label: 'x' })).toBe('Service account token');
    expect(connectorSlotLabel(t, 'plugin', { name: 'token', label: 'Plugin token' })).toBe('Plugin token');
    expect(connectorSlotLabel(t, 'plugin', { name: 'token' })).toBe('token');
  });
});
