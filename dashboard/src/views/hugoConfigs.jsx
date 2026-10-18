// Declarative record page configurations for Hugo's own domain (plan section 18.3), rendered by ResourceView.
// Every list names its route in `endpoint`, so a server that does not expose it yet gets an explanatory empty state
// (resources.<id>.purpose) instead of an error.

import CopyableId from '../components/CopyableId';
import { Badge, EffectBadge, EnumBadge } from '../components/StatusBadge';
import NamedId from '../components/NamedId';
import CodeBlock from '../components/CodeBlock';
import ResultModal from '../components/ResultModal';
import ModelValidationView, { ModelDraftValidator } from '../components/ModelValidationView';
import RevealSecretModal from '../components/RevealSecretModal';
import TryToolModal from '../components/TryToolModal';
import ApprovalDecideModal from '../components/ApprovalDecideModal';
import ResourceSettingsPanel, { validateResourceConnector, resourceConnectorBody } from '../components/ResourceSettingsPanel';
import { resourceHealth, resourceToForm, resourceFieldErrors, referrerRows } from '../utils/resources';
import { notifyValidated } from '../components/ModelHealthHistory';
import ModelHealthModal from '../components/ModelHealthModal';
import SecretSlotPicker, { SecretValuesForm } from '../components/SecretField';
import ValuesModal from '../components/ValuesModal';
import { tip } from '../components/Tooltip';
import { ProfileRelationDiagram } from '../components/Explainer';
import { ENUMS, approvalStatusTone, toolStatusTone, toolDecisionTone, healthTone, accessModeTone } from '../utils/constants';
import { pathFor } from '../config/navConfig';
import { formatDuration, formatNumber, formatBytes } from '../i18n/formatters';
import { errorMessage } from '../utils/errors';
import { enumOptions } from '../utils/enums';
import { validateSecretValues, secretValuesBody } from '../utils/secrets';
import { traceUrl } from '../utils/grafana';
import { downloadText } from '../utils/exporters';
import { field, section, col, idCol, nameCol, createdCol, actionTip, safeList, hugoApi, timeCell } from './configHelpers';
import { useGrafana } from '../hooks/useServerInfo';

/** Surfaces a profile is exposed on, as translated surface labels (chat is always available). */
const exposure = (t, r) =>
  [r.exposeMcp && 'Mcp', (r.exposeA2A ?? r.exposeA2a) && 'A2A', (r.exposeOpenAI ?? r.exposeOpenai) && 'OpenAI']
    .filter(Boolean)
    .map((s) => t(`enums.surface.${s}`));

/**
 * A stored profile as the form edits it. The server spells the exposure flags exposeA2A and exposeOpenAI; the form
 * fields (and their catalog keys) are exposeA2a and exposeOpenai.
 */
export function profileToForm(p) {
  const { exposeA2A, exposeOpenAI, ...rest } = p || {};
  return {
    ...rest,
    exposeA2a: exposeA2A ?? p?.exposeA2a ?? true,
    exposeOpenai: exposeOpenAI ?? p?.exposeOpenai ?? true,
    inferenceEndpointId: p?.inferenceEndpointId || '',
    fallbackInferenceEndpointId: p?.fallbackInferenceEndpointId || '',
    embeddingEndpointId: p?.embeddingEndpointId || '',
    instructionsAppend: p?.instructionsAppend || '',
    _originalInstructionsAppend: p?.instructionsAppend || ''
  };
}
/** Health states the resource and model endpoint list filters accept (the server answers 400 for others). */
const HEALTH_FILTER = ['Healthy', 'Unhealthy', 'Unknown'];

const lookupMap = (items, label) => Object.fromEntries((items || []).map((x) => [x.id, label(x)]));
const profileLabel = (p) => p.displayName || p.name;

/** Profiles and resources by ID, for showing names in place of IDs. */
async function profileAndResourceLookups({ apiClient }) {
  const [profiles, resources] = await Promise.all([
    safeList(apiClient.listProfiles({ pageNumber: 1, pageSize: 1000 })),
    safeList(apiClient.listResources({ pageNumber: 1, pageSize: 1000 }))
  ]);
  return { profiles: lookupMap(profiles, profileLabel), resources: lookupMap(resources, (r) => r.name), profileList: profiles, resourceList: resources };
}

// ---------------------------------------------------------------- agent profiles

export const profilesConfig = {
  id: 'profiles',
  explainer: 'profiles',
  explainerDiagram: () => <ProfileRelationDiagram />,
  endpoint: '/v1.0/api/agent-profiles',
  api: hugoApi('listProfiles', 'createProfile', 'updateProfile', 'deleteProfile'),
  loadOptions: async ({ apiClient }) => ({ models: await safeList(apiClient.listModelEndpoints({ pageNumber: 1, pageSize: 1000 })) }),
  loadLookups: async ({ apiClient }) => ({ models: lookupMap(await safeList(apiClient.listModelEndpoints({ pageNumber: 1, pageSize: 1000 })), (m) => m.name) }),
  blank: () => ({
    name: '',
    displayName: '',
    description: '',
    personaName: 'Agent Smith',
    userAddress: 'Mr. Anderson',
    visibility: 'Tenant',
    inferenceEndpointId: '',
    fallbackInferenceEndpointId: '',
    embeddingEndpointId: '',
    maxIterations: 12,
    maxToolCallsPerTurn: 24,
    toolTimeoutSeconds: 120,
    temperature: null,
    exposeMcp: true,
    exposeA2a: true,
    exposeOpenai: true,
    exposeManagementTools: false,
    instructionsAppend: ''
  }),
  toForm: profileToForm,
  isEditable: () => true,
  canDelete: (r) => !r?.isDefault,
  detailPath: (r) => pathFor('profiles', r.id),
  columns: ({ t, i18n, lookups }) => [
    col(
      'profiles',
      t,
      'name',
      (r) => (
        <span>
          <strong>{r.displayName || r.name}</strong>
          {r.isDefault && (
            <>
              {' '}
              <Badge tone="info" tip={t('tips.badges.defaultProfile')}>
                {t('common.generic.default')}
              </Badge>
            </>
          )}
        </span>
      ),
      { sortable: true, pinned: true }
    ),
    col('profiles', t, 'personaName', (r) => r.personaName || '-'),
    col('profiles', t, 'visibility', (r) => <EnumBadge group="visibility" value={r.visibility} />),
    col(
      'profiles',
      t,
      'inferenceEndpointId',
      (r) =>
        r.inferenceEndpointId ? (
          <NamedId id={r.inferenceEndpointId} name={lookups?.models?.[r.inferenceEndpointId]} />
        ) : (
          <span className="text-muted">{t('resources.profiles.noModel')}</span>
        ),
      {
        csv: (r) => lookups?.models?.[r.inferenceEndpointId] || r.inferenceEndpointId || ''
      }
    ),
    col('profiles', t, 'bindingCount', (r) => formatNumber(r.bindingCount ?? 0, {}, i18n.language), { cellClass: 'right', csv: (r) => r.bindingCount ?? 0 }),
    col('profiles', t, 'exposure', (r) => exposure(t, r).join(' / ') || '-', { csv: (r) => exposure(t, r).join(' ') }),
    idCol('profiles', t),
    createdCol('profiles', t, i18n)
  ],
  fields: (_d, { t, options }) => {
    const models = (kind) =>
      (options.models || []).filter((m) => !m.kind || m.kind === kind).map((m) => ({ value: m.id, label: `${m.name} (${m.model || m.provider})` }));
    return [
      section('profiles', t, 'identity'),
      field('profiles', t, 'name', 'text', { required: true }),
      field('profiles', t, 'displayName', 'text'),
      field('profiles', t, 'description', 'textarea'),
      field('profiles', t, 'personaName', 'text'),
      field('profiles', t, 'userAddress', 'text'),
      field('profiles', t, 'visibility', 'select', { required: true, options: enumOptions(t, 'visibility') }),
      section('profiles', t, 'models'),
      field('profiles', t, 'inferenceEndpointId', 'select', { options: models('Inference') }),
      field('profiles', t, 'fallbackInferenceEndpointId', 'select', { options: models('Inference') }),
      field('profiles', t, 'embeddingEndpointId', 'select', { options: models('Embedding') }),
      section('profiles', t, 'limits'),
      field('profiles', t, 'maxIterations', 'number', { min: 1, max: 100 }),
      field('profiles', t, 'maxToolCallsPerTurn', 'number', { min: 1, max: 500 }),
      field('profiles', t, 'toolTimeoutSeconds', 'number', { min: 1, max: 3600 }),
      field('profiles', t, 'temperature', 'number', { min: 0, max: 2, step: 0.1 }),
      section('profiles', t, 'exposure'),
      field('profiles', t, 'exposeMcp', 'checkbox'),
      field('profiles', t, 'exposeA2a', 'checkbox'),
      field('profiles', t, 'exposeOpenai', 'checkbox'),
      field('profiles', t, 'exposeManagementTools', 'checkbox'),
      field('profiles', t, 'instructionsAppend', 'textarea', { rows: 4 })
    ];
  },
  validate: (d, t) => {
    const e = {};
    if (!d.name) e.name = t('common.errors.required');
    else if (!/^[A-Za-z0-9][A-Za-z0-9_.-]*$/.test(d.name)) e.name = t('resources.profiles.errors.urlSafe');
    return e;
  },
  toBody: (d) => ({
    name: d.name,
    displayName: d.displayName ?? '',
    description: d.description ?? '',
    personaName: d.personaName ?? '',
    userAddress: d.userAddress ?? '',
    visibility: d.visibility,
    inferenceEndpointId: d.inferenceEndpointId ?? '',
    fallbackInferenceEndpointId: d.fallbackInferenceEndpointId ?? '',
    embeddingEndpointId: d.embeddingEndpointId ?? '',
    maxIterations: d.maxIterations ?? null,
    maxToolCallsPerTurn: d.maxToolCallsPerTurn ?? null,
    toolTimeoutSeconds: d.toolTimeoutSeconds ?? null,
    temperature: d.temperature ?? null,
    exposeMcp: !!d.exposeMcp,
    exposeA2A: !!d.exposeA2a,
    exposeOpenAI: !!d.exposeOpenai,
    exposeManagementTools: !!d.exposeManagementTools,
    ...(d.id && d.instructionsAppend === d._originalInstructionsAppend ? {} : { instructionsAppend: d.instructionsAppend ?? '' })
  }),
  rowActions: (r, ctx) => [
    { label: ctx.t('resources.profiles.actions.chat'), tip: actionTip(ctx.t, 'profiles', 'chat'), onClick: () => ctx.navigate(pathFor('chat')) },
    {
      label: ctx.t('resources.profiles.actions.duplicate'),
      tip: actionTip(ctx.t, 'profiles', 'duplicate'),
      onClick: async () => {
        try {
          const created = await ctx.apiClient.duplicateProfile(r.id, { name: `${r.name}-copy` });
          ctx.addToast({ type: 'success', message: ctx.t('common.toasts.cloned') });
          if (created?.id) ctx.navigate(pathFor('profiles', created.id));
          else ctx.reload();
        } catch (e) {
          ctx.addToast({ type: 'error', message: errorMessage(ctx.t, e) });
        }
      }
    }
  ],
  canDuplicate: false
};

// ---------------------------------------------------------------- approvals

export const approvalsConfig = {
  id: 'approvals',
  explainer: 'approvals',
  endpoint: '/v1.0/api/approvals',
  api: { list: (c, _t, p) => c.listApprovals(p), create: null, update: null, remove: null },
  defaultFilters: { status: 'Pending' },
  urlFilters: ['status', 'effect', 'agentProfileId', 'resourceId', 'toolName'],
  autoRefresh: 15,
  search: false,
  loadLookups: profileAndResourceLookups,
  filters: [
    { name: 'status', label: (t) => t('resources.approvals.fields.status'), options: ENUMS.approvalStatus, enumGroup: 'approvalStatus' },
    { name: 'effect', label: (t) => t('resources.approvals.fields.effect'), options: ENUMS.effect, enumGroup: 'effect' },
    {
      name: 'agentProfileId',
      label: (t) => t('resources.approvals.fields.agentProfileId'),
      options: (lookups) => Object.keys(lookups.profiles || {}),
      optionLabel: (v, _t, lookups) => lookups?.profiles?.[v] || v
    },
    {
      name: 'resourceId',
      label: (t) => t('resources.toolInvocations.fields.resourceId'),
      options: (lookups) => Object.keys(lookups.resources || {}),
      optionLabel: (v, _t, lookups) => lookups?.resources?.[v] || v
    },
    { name: 'toolName', type: 'text', label: (t) => t('resources.toolInvocations.fields.toolName') },
    { name: 'fromUtc', type: 'datetime', label: (t) => t('resources.approvals.fields.fromUtc') },
    { name: 'toUtc', type: 'datetime', label: (t) => t('resources.toolInvocations.fields.toUtc') }
  ],
  columns: ({ t, i18n, lookups }) => [
    col('approvals', t, 'summary', (r) => <span className="cell-wrap">{r.summary || '-'}</span>, { pinned: true }),
    col('approvals', t, 'effect', (r) => <EffectBadge effect={r.effect} />, { csv: (r) => r.effect }),
    col('toolInvocations', t, 'resourceId', (r) => <NamedId id={r.resourceId} name={r.bindingAlias || lookups?.resources?.[r.resourceId]} />, {
      csv: (r) => r.bindingAlias || lookups?.resources?.[r.resourceId] || r.resourceId || ''
    }),
    col('toolInvocations', t, 'toolName', (r) => <code>{r.toolName}</code>, { defaultHidden: true }),
    col('approvals', t, 'status', (r) => <EnumBadge group="approvalStatus" value={r.status} tone={approvalStatusTone(r.status)} />, { csv: (r) => r.status }),
    col('approvals', t, 'agentProfileId', (r) => <NamedId id={r.agentProfileId} name={lookups?.profiles?.[r.agentProfileId]} />, {
      csv: (r) => lookups?.profiles?.[r.agentProfileId] || r.agentProfileId
    }),
    col('approvals', t, 'requestedBy', (r) => r.requestedByName || r.requestedByPrincipalId || '-'),
    col('approvals', t, 'createdUtc', (r) => timeCell(r.createdUtc, i18n), { sortable: true }),
    col('approvals', t, 'expiresUtc', (r) => timeCell(r.expiresUtc, i18n)),
    col('approvals', t, 'decidedBy', (r) => r.decidedByName || r.decidedByUserId || '-', { defaultHidden: true }),
    idCol('approvals', t)
  ],
  fields: (_d, { t }) => [
    field('approvals', t, 'summary', 'textarea', { readOnly: true }),
    field('approvals', t, 'effect', 'text', { readOnly: true }),
    field('approvals', t, 'status', 'text', { readOnly: true }),
    field('approvals', t, 'decisionNote', 'textarea', { readOnly: true })
  ],
  rowActions: (r, ctx) => [
    {
      label: ctx.t('resources.approvals.actions.decide'),
      tip: actionTip(ctx.t, 'approvals', 'decide'),
      hidden: r.status !== 'Pending',
      onClick: () => ctx.setExtraModal(<ApprovalDecideModal approval={r} onClose={() => ctx.setExtraModal(null)} onDecided={ctx.reload} />)
    },
    {
      label: ctx.t('resources.approvals.actions.invocation'),
      tip: actionTip(ctx.t, 'approvals', 'invocation'),
      hidden: !r.toolInvocationId,
      onClick: () =>
        ctx.setExtraModal(
          <ResultModal
            title={ctx.t('resources.approvals.actions.invocation')}
            load={() => ctx.apiClient.getApproval(r.id)}
            renderResult={(a) => (a?.invocation ? <ToolInvocationDetail t={ctx.t} r={a.invocation} /> : <CodeBlock value={a} prettyJson />)}
            onClose={() => ctx.setExtraModal(null)}
          />
        )
    },
    {
      label: ctx.t('resources.approvals.actions.cancel'),
      tip: actionTip(ctx.t, 'approvals', 'cancel'),
      hidden: r.status !== 'Pending',
      onClick: async () => {
        try {
          await ctx.apiClient.cancelApproval(r.id);
          ctx.addToast({ type: 'success', message: ctx.t('resources.approvals.cancelled') });
          ctx.reload();
        } catch (e) {
          ctx.addToast({ type: 'error', message: errorMessage(ctx.t, e) });
        }
      }
    }
  ],
  bulkActions: (selected, ctx) => (
    <button
      type="button"
      className="button-danger button-sm"
      onClick={async () => {
        let failed = 0;
        for (const id of selected) {
          try {
            await ctx.apiClient.rejectApproval(id, ctx.t('resources.approvals.bulkNote'));
          } catch {
            failed += 1;
          }
        }
        ctx.addToast({ type: failed ? 'warning' : 'success', message: ctx.t('resources.approvals.bulkRejected', { count: selected.length - failed, failed }) });
        ctx.clearSelection();
        ctx.reload();
      }}
      {...tip(ctx.t('tips.resources.approvals.bulkReject'))}
    >
      {ctx.t('resources.approvals.actions.bulkReject')}
    </button>
  )
};

// ---------------------------------------------------------------- conversations

export const conversationsConfig = {
  id: 'conversations',
  explainer: 'conversations',
  endpoint: '/v1.0/api/conversations',
  api: { list: (c, _t, p) => c.listConversations(p), create: null, update: null, remove: (c, _t, id) => c.deleteConversation(id) },
  loadLookups: profileAndResourceLookups,
  filters: [
    { name: 'source', label: (t) => t('resources.conversations.fields.source'), options: ENUMS.conversationSource, enumGroup: 'conversationSource' },
    {
      name: 'agentProfileId',
      label: (t) => t('resources.conversations.fields.agentProfileId'),
      options: (lookups) => Object.keys(lookups.profiles || {}),
      optionLabel: (v, _t, lookups) => lookups?.profiles?.[v] || v
    },
    { name: 'userId', type: 'text', label: (t) => t('resources.conversations.fields.userId') },
    { name: 'fromUtc', type: 'datetime', label: (t) => t('resources.conversations.fields.fromUtc') }
  ],
  detailPath: (r) => pathFor('chat', r.id),
  columns: ({ t, i18n, lookups }) => [
    col('conversations', t, 'title', (r) => <strong>{r.title || t('views.chat.untitled')}</strong>, { pinned: true }),
    col('conversations', t, 'agentProfileId', (r) => <NamedId id={r.agentProfileId} name={lookups?.profiles?.[r.agentProfileId]} />, {
      csv: (r) => lookups?.profiles?.[r.agentProfileId] || r.agentProfileId
    }),
    col('conversations', t, 'userId', (r) => r.userName || r.userId || r.credentialId || '-'),
    col('conversations', t, 'source', (r) => <EnumBadge group="conversationSource" value={r.source} />, { csv: (r) => r.source }),
    col('conversations', t, 'messageCount', (r) => formatNumber(r.messageCount ?? 0, {}, i18n.language), { cellClass: 'right' }),
    col('conversations', t, 'tokens', (r) => formatNumber((r.totalInputTokens || 0) + (r.totalOutputTokens || 0), {}, i18n.language), {
      cellClass: 'right',
      defaultHidden: true
    }),
    col('conversations', t, 'lastMessageUtc', (r) => timeCell(r.lastMessageUtc, i18n), { sortable: true }),
    idCol('conversations', t)
  ],
  fields: () => [],
  rowActions: (r, ctx) => [
    {
      label: ctx.t('resources.conversations.actions.export'),
      tip: actionTip(ctx.t, 'conversations', 'export'),
      onClick: async () => {
        try {
          const text = await ctx.apiClient.exportConversation(r.id, 'md');
          downloadText(`conversation-${r.id}.md`, typeof text === 'string' ? text : JSON.stringify(text, null, 2), 'text/markdown');
        } catch (e) {
          ctx.addToast({ type: 'error', message: errorMessage(ctx.t, e) });
        }
      }
    }
  ],
  canDuplicate: false
};

// ---------------------------------------------------------------- resources

export const resourcesConfig = {
  id: 'resources',
  explainer: 'resources',
  explainerDiagram: () => <ProfileRelationDiagram />,
  endpoint: '/v1.0/api/resources',
  api: hugoApi('listResources', 'createResource', 'updateResource', 'deleteResource'),
  formSize: 'xlarge',
  loadLookups: async ({ apiClient }) => {
    const connectors = await safeList(apiClient.listConnectors({ pageNumber: 1, pageSize: 1000 }));
    return {
      connectors: lookupMap(
        connectors.map((c) => ({ ...c, id: c.type })),
        (c) => c.displayName || c.type
      )
    };
  },
  urlFilters: ['connectorType', 'environment', 'health'],
  filters: [
    {
      name: 'connectorType',
      label: (t) => t('resources.resources.fields.connectorType'),
      options: (lookups) => Object.keys(lookups.connectors || {}),
      optionLabel: (v, _t, lookups) => lookups?.connectors?.[v] || v
    },
    { name: 'environment', type: 'text', label: (t) => t('resources.resources.fields.environment') },
    { name: 'health', label: (t) => t('resources.resources.columns.health'), options: HEALTH_FILTER, enumGroup: 'healthStatus' }
  ],
  detailPath: (r) => pathFor('resources', r.id),
  // Connector setting errors (400 SettingsInvalid) are keyed by setting; the form shows them under settings.<key>.
  mapErrors: (fields, e) => resourceFieldErrors(fields, e),
  blank: () => ({
    name: '',
    displayName: '',
    description: '',
    environment: '',
    visibility: 'Tenant',
    notes: '',
    endpoint: '',
    defaultAccessModeCeiling: '',
    connectorType: '',
    settings: {},
    secretBindings: {}
  }),
  toForm: resourceToForm,
  duplicateStrip: ['lastTestUtc', 'lastTestResult', 'lastTestMessage'],
  columns: ({ t, i18n, lookups }) => [
    nameCol('resources', t),
    col('resources', t, 'connectorType', (r) => <Badge tone="info">{lookups?.connectors?.[r.connectorType] || r.connectorType}</Badge>, {
      csv: (r) => r.connectorType
    }),
    col('resources', t, 'environment', (r) => (r.environment ? <Badge>{r.environment}</Badge> : '-')),
    col('resources', t, 'endpoint', (r) => (r.endpoint ? <code className="cell-wrap">{r.endpoint}</code> : '-')),
    col('resources', t, 'health', (r) => <EnumBadge group="healthStatus" value={resourceHealth(r)} tone={healthTone(resourceHealth(r))} />, {
      csv: (r) => resourceHealth(r)
    }),
    col('resources', t, 'lastTestUtc', (r) => timeCell(r.lastTestUtc, i18n, t('common.generic.never'))),
    col('resources', t, 'visibility', (r) => <EnumBadge group="visibility" value={r.visibility} />, { defaultHidden: true }),
    idCol('resources', t),
    createdCol('resources', t, i18n)
  ],
  fields: (_d, { t, mode }) => [
    {
      name: 'connector',
      type: 'custom',
      render: (data, { disabled, onChange, errors }) => (
        <ResourceSettingsPanel
          data={data}
          onChange={onChange}
          errors={errors}
          disabled={disabled}
          mode={mode}
          generalFields={[
            section('resources', t, 'general'),
            field('resources', t, 'name', 'text', { required: true }),
            field('resources', t, 'displayName', 'text'),
            field('resources', t, 'environment', 'text', { placeholder: 'prod' }),
            field('resources', t, 'visibility', 'select', { required: true, options: enumOptions(t, 'visibility') }),
            field('resources', t, 'defaultAccessModeCeiling', 'select', { options: enumOptions(t, 'accessMode') }),
            field('resources', t, 'endpoint', 'text'),
            field('resources', t, 'description', 'textarea', { rows: 2 }),
            field('resources', t, 'notes', 'textarea', { rows: 3 })
          ]}
        />
      )
    }
  ],
  validate: (d, t) => {
    const e = {};
    if (!d.name) e.name = t('common.errors.required');
    Object.assign(e, validateResourceConnector(t, d, d._connector));
    return e;
  },
  toBody: (d) => ({
    name: d.name,
    displayName: d.displayName ?? '',
    description: d.description ?? '',
    environment: d.environment ?? '',
    visibility: d.visibility,
    notes: d.notes ?? '',
    endpoint: d.endpoint ?? '',
    defaultAccessModeCeiling: d.defaultAccessModeCeiling || null,
    clearAccessModeCeiling: !d.defaultAccessModeCeiling,
    ...resourceConnectorBody(d, d._connector)
  }),
  rowActions: (r, ctx) => [
    {
      label: ctx.t('resources.resources.actions.test'),
      tip: actionTip(ctx.t, 'resources', 'test'),
      onClick: () =>
        ctx.setExtraModal(
          <ResultModal
            title={ctx.t('resources.resources.actions.testTitle', { name: r.name })}
            load={() => ctx.apiClient.testResource(r.id)}
            onClose={() => {
              ctx.setExtraModal(null);
              ctx.reload();
            }}
          />
        )
    },
    {
      label: ctx.t('resources.resources.actions.tryTool'),
      tip: actionTip(ctx.t, 'resources', 'tryTool'),
      onClick: () => ctx.setExtraModal(<TryToolModal resource={r} onClose={() => ctx.setExtraModal(null)} />)
    },
    {
      label: ctx.t('resources.resources.actions.referrers'),
      tip: actionTip(ctx.t, 'resources', 'referrers'),
      onClick: () =>
        ctx.setExtraModal(
          <ResultModal
            title={ctx.t('resources.resources.actions.referrersTitle', { name: r.name })}
            load={() => ctx.apiClient.getResourceReferrers(r.id)}
            renderResult={(x) => <ReferrerList t={ctx.t} result={x} />}
            onClose={() => ctx.setExtraModal(null)}
          />
        )
    }
  ],
  deletePreview: async (r, { apiClient, t }) => {
    const refs = await apiClient.getResourceReferrers(r.id).catch(() => null);
    const items = referrerItems(refs);
    if (!items.length) return null;
    return { message: t('resources.resources.deleteReferenced', { name: r.name, count: items.length }), details: <ReferrerList t={t} result={refs} /> };
  }
};

/** Referrer rows for display: a resource's bindings or a secret's resources and model endpoints. */
export function referrerItems(result) {
  return referrerRows(result);
}

export function ReferrerList({ t, result }) {
  const items = referrerItems(result);
  if (!items.length) return <p className="text-muted">{t('resources.resources.noReferrers')}</p>;
  return (
    <ul className="plain-list confirm-list" data-testid="referrers">
      {items.map((x, i) => (
        <li key={x.id || i}>
          <Badge>{t(`resources.resources.referrerTypes.${x.type}`, { defaultValue: x.type || '-' })}</Badge>{' '}
          {x.agentProfileId ? (
            <>
              <NamedId id={x.agentProfileId} name={x.agentProfileName} /> / <code>{x.alias}</code>
            </>
          ) : x.name === x.id ? (
            <CopyableId value={x.id} max={80} />
          ) : (
            <code>{x.name}</code>
          )}
          {x.accessMode && (
            <>
              {' '}
              <EnumBadge group="accessMode" value={x.accessMode} tone={accessModeTone(x.accessMode)} />
            </>
          )}
          {x.detail && <span className="text-muted"> {x.detail}</span>}
        </li>
      ))}
    </ul>
  );
}

// ---------------------------------------------------------------- secrets

export const secretsConfig = {
  id: 'secrets',
  explainer: 'secrets',
  endpoint: '/v1.0/api/secrets',
  api: {
    list: (c, _t, p) => c.listSecrets(p),
    create: (c, _t, b) => c.createSecret(b),
    update: (c, _t, id, b) => c.updateSecret(id, b),
    remove: (c, _t, id) => c.deleteSecret(id)
  },
  urlFilters: ['kind'],
  filters: [{ name: 'kind', label: (t) => t('resources.secrets.fields.kind'), options: ENUMS.secretKind, enumGroup: 'secretKind' }],
  blank: () => ({ name: '', description: '', kind: 'Password', values: {}, expiresUtc: null }),
  toForm: (r) => ({ ...r, kind: r.kind || r.secretKind, values: {} }),
  canDuplicate: false,
  columns: ({ t, i18n }) => [
    nameCol('secrets', t),
    col('secrets', t, 'kind', (r) => <EnumBadge group="secretKind" value={r.kind || r.secretKind} tone="info" />, { csv: (r) => r.kind || r.secretKind }),
    col('secrets', t, 'fingerprint', (r) => (r.fingerprint ? <code>{r.fingerprint}</code> : '-')),
    col(
      'secrets',
      t,
      'referrerCount',
      (r) => {
        const count = r.referrerCount ?? r.referrers?.length;
        return count == null ? '-' : formatNumber(count, {}, i18n.language);
      },
      { cellClass: 'right', defaultHidden: true }
    ),
    col('secrets', t, 'fields', (r) => (r.fields || []).join(', ') || '-', { csv: (r) => (r.fields || []).join(' ') }),
    col('secrets', t, 'lastRotatedUtc', (r) => timeCell(r.lastRotatedUtc, i18n, t('common.generic.never'))),
    col('secrets', t, 'expiresUtc', (r) => (r.expiresUtc ? <ExpiryCell t={t} i18n={i18n} value={r.expiresUtc} /> : t('common.generic.never'))),
    col('secrets', t, 'lastRevealedUtc', (r) => timeCell(r.lastRevealedUtc, i18n, t('common.generic.never')), { defaultHidden: true }),
    idCol('secrets', t),
    createdCol('secrets', t, i18n)
  ],
  fields: (d, { t, mode }) => [
    field('secrets', t, 'name', 'text', { required: true }),
    field('secrets', t, 'kind', 'select', { required: true, options: enumOptions(t, 'secretKind'), readOnly: mode !== 'create' }),
    field('secrets', t, 'description', 'textarea', { rows: 2 }),
    field('secrets', t, 'expiresUtc', 'datetime', { futureOnly: true }),
    ...(mode === 'create'
      ? [
          section('secrets', t, 'values'),
          {
            name: 'values',
            type: 'custom',
            render: (data, { disabled, onChange, errors }) => (
              <SecretValuesForm
                kind={data.kind}
                values={data.values || {}}
                errors={errors}
                disabled={disabled}
                idPrefix="secret-create"
                onChange={(values) => onChange({ ...data, values })}
              />
            )
          }
        ]
      : [])
  ],
  validate: (d, t) => {
    const e = {};
    if (!d.name) e.name = t('common.errors.required');
    if (!d.id) for (const [k] of Object.entries(validateSecretValues(d.kind, d.values))) e[k] = t('common.errors.required');
    if (d.expiresUtc && new Date(d.expiresUtc).getTime() <= Date.now()) e.expiresUtc = t('resources.secrets.errors.expiryPast');
    return e;
  },
  toBody: (d, mode) =>
    mode === 'edit'
      ? // The server keeps an omitted (null) description, so an emptied one is sent as '' to clear it.
        { name: d.name, description: d.description ?? '', expiresUtc: d.expiresUtc || null }
      : { name: d.name, description: d.description || null, kind: d.kind, expiresUtc: d.expiresUtc || null, values: secretValuesBody(d.kind, d.values) },
  rowActions: (r, ctx) => [
    {
      label: ctx.t('resources.secrets.actions.replaceValues'),
      tip: actionTip(ctx.t, 'secrets', 'replaceValues'),
      onClick: () =>
        ctx.setExtraModal(
          <ValuesModal
            title={ctx.t('resources.secrets.actions.replaceValues')}
            kind={r.kind || r.secretKind}
            onSubmit={async (values) => {
              await ctx.apiClient.updateSecretValues(r.id, secretValuesBody(r.kind || r.secretKind, values));
              ctx.addToast({ type: 'success', message: ctx.t('common.toasts.saved') });
              ctx.reload();
            }}
            onClose={() => ctx.setExtraModal(null)}
          />
        )
    },
    {
      label: ctx.t('resources.secrets.actions.reveal'),
      tip: actionTip(ctx.t, 'secrets', 'reveal'),
      danger: true,
      onClick: () =>
        ctx.setExtraModal(
          <RevealSecretModal
            secret={r}
            onClose={() => {
              ctx.setExtraModal(null);
              ctx.reload();
            }}
          />
        )
    },
    {
      label: ctx.t('resources.secrets.actions.referrers'),
      tip: actionTip(ctx.t, 'secrets', 'referrers'),
      onClick: () =>
        ctx.setExtraModal(
          <ResultModal
            title={ctx.t('resources.secrets.actions.referrersTitle', { name: r.name })}
            load={() => ctx.apiClient.getSecretReferrers(r.id)}
            renderResult={(x) => <ReferrerList t={ctx.t} result={x} />}
            onClose={() => ctx.setExtraModal(null)}
          />
        )
    }
  ],
  deletePreview: async (r, { apiClient, t }) => {
    const refs = await apiClient.getSecretReferrers(r.id).catch(() => null);
    const items = referrerItems(refs);
    if (!items.length) return null;
    return { message: t('resources.secrets.deleteReferenced', { name: r.name, count: items.length }), details: <ReferrerList t={t} result={refs} /> };
  }
};

/** Expiry with a warning badge when it is within 14 days (plan section 18.3: expiring soon warning). */
function ExpiryCell({ t, i18n, value }) {
  const days = (new Date(value).getTime() - Date.now()) / 86400000;
  const soon = days <= 14;
  return (
    <span>
      {timeCell(value, i18n)}
      {soon && (
        <>
          {' '}
          <Badge tone={days <= 0 ? 'danger' : 'warning'} tip={t('tips.badges.expiringSoon')}>
            {days <= 0 ? t('resources.secrets.expired') : t('resources.secrets.expiringSoon')}
          </Badge>
        </>
      )}
    </span>
  );
}

// ---------------------------------------------------------------- model endpoints

/** A model endpoint form as the create, update, and draft validation request body. */
function modelEndpointBody(d) {
  return {
    name: d.name,
    kind: d.kind,
    provider: d.provider,
    baseUrl: d.baseUrl || null,
    model: d.model,
    apiKeySecretId: d.apiKeySecretId || null,
    apiKeySecretField: d.apiKeySecretField || null,
    region: d.region || null,
    deploymentName: d.deploymentName || null,
    apiVersion: d.apiVersion || null,
    contextWindowTokens: d.contextWindowTokens ?? null,
    maxOutputTokens: d.maxOutputTokens ?? null,
    supportsTools: !!d.supportsTools,
    supportsStreaming: !!d.supportsStreaming,
    embeddingDimensions: d.embeddingDimensions ?? null,
    maxConcurrency: d.maxConcurrency ?? null,
    timeoutSeconds: d.timeoutSeconds ?? null,
    maxRetries: d.maxRetries ?? null
  };
}

/** Open a read-only health view separately from the model endpoint definition form. */
function modelHealthAction(endpoint, ctx) {
  return {
    label: ctx.t('resources.models.actions.health'),
    tip: actionTip(ctx.t, 'models', 'health'),
    onClick: () => ctx.setExtraModal(<ModelHealthModal endpoint={endpoint} onClose={() => ctx.setExtraModal(null)} />)
  };
}

export const modelsConfig = {
  id: 'models',
  explainer: 'models',
  endpoint: '/v1.0/api/model-endpoints',
  api: hugoApi('listModelEndpoints', 'createModelEndpoint', 'updateModelEndpoint', 'deleteModelEndpoint'),
  loadOptions: async ({ apiClient }) => ({ secrets: await safeList(apiClient.listSecrets({ pageNumber: 1, pageSize: 1000 })) }),
  urlFilters: ['kind', 'provider', 'health'],
  filters: [
    { name: 'kind', label: (t) => t('resources.models.fields.kind'), options: ENUMS.modelKind, enumGroup: 'modelKind' },
    { name: 'provider', label: (t) => t('resources.models.fields.provider'), options: ENUMS.modelProvider, enumGroup: 'modelProvider' },
    {
      name: 'health',
      label: (t) => t('resources.models.columns.healthStatus'),
      tip: (t) => t('tips.filters.healthStatus'),
      options: HEALTH_FILTER,
      enumGroup: 'healthStatus'
    }
  ],
  blank: () => ({
    name: '',
    kind: 'Inference',
    provider: 'Ollama',
    baseUrl: 'http://127.0.0.1:11434',
    model: '',
    apiKeySecretId: '',
    region: '',
    deploymentName: '',
    apiVersion: '',
    contextWindowTokens: null,
    maxOutputTokens: null,
    supportsTools: true,
    supportsStreaming: true,
    embeddingDimensions: null,
    maxConcurrency: 4,
    timeoutSeconds: 120,
    maxRetries: 2
  }),
  columns: (ctx) => {
    const { t, i18n } = ctx;
    return [
      nameCol('models', t),
      col('models', t, 'kind', (r) => <EnumBadge group="modelKind" value={r.kind} tone="info" />, { csv: (r) => r.kind }),
      col('models', t, 'provider', (r) => <EnumBadge group="modelProvider" value={r.provider} />, { csv: (r) => r.provider }),
      col('models', t, 'model', (r) => <code>{r.model || '-'}</code>),
      col('models', t, 'baseUrl', (r) => (r.baseUrl ? <CopyableId value={r.baseUrl} max={36} /> : '-'), { defaultHidden: true }),
      col(
        'models',
        t,
        'healthStatus',
        (r) => (
          <button
            type="button"
            className="button-ghost button-sm"
            aria-label={t('resources.models.actions.healthTitle', { name: r.name })}
            aria-haspopup="dialog"
            {...tip(actionTip(t, 'models', 'health'))}
            onClick={(event) => {
              event.stopPropagation();
              modelHealthAction(r, ctx).onClick();
            }}
          >
            <EnumBadge group="healthStatus" value={r.healthStatus || 'Unknown'} tone={healthTone(r.healthStatus)} />
          </button>
        ),
        { csv: (r) => r.healthStatus }
      ),
      col('models', t, 'lastHealthUtc', (r) => timeCell(r.lastHealthUtc, i18n, t('common.generic.never'))),
      idCol('models', t)
    ];
  },
  fields: (d, { t, options, apiClient }) => [
    section('models', t, 'endpoint'),
    field('models', t, 'name', 'text', { required: true }),
    field('models', t, 'kind', 'select', { required: true, options: enumOptions(t, 'modelKind') }),
    field('models', t, 'provider', 'select', { required: true, options: enumOptions(t, 'modelProvider') }),
    field('models', t, 'baseUrl', 'text'),
    field('models', t, 'model', 'text', { required: true }),
    {
      name: 'apiKey',
      type: 'custom',
      render: (data, { disabled, onChange }) => (
        <SecretSlotPicker
          slot={{
            name: 'apiKey',
            label: t('resources.models.fields.apiKeySecretId'),
            help: t('tips.resources.models.apiKeySecretId'),
            acceptedKinds: ['ApiToken', 'BearerToken', 'AccessKeyPair', 'ServiceAccountJson', 'Generic']
          }}
          secrets={options.secrets || []}
          value={{ secretId: data.apiKeySecretId }}
          disabled={disabled}
          idPrefix="model"
          onChange={(v) => onChange({ ...data, apiKeySecretId: v.secretId || '' })}
        />
      )
    },
    field('models', t, 'region', 'text', { showIf: (x) => ['Bedrock', 'VertexAI', 'AzureOpenAI'].includes(x.provider) }),
    field('models', t, 'deploymentName', 'text', { showIf: (x) => x.provider === 'AzureOpenAI' }),
    field('models', t, 'apiVersion', 'text', { showIf: (x) => x.provider === 'AzureOpenAI' }),
    section('models', t, 'capabilities'),
    field('models', t, 'contextWindowTokens', 'number', { min: 1, showIf: (x) => x.kind === 'Inference' }),
    field('models', t, 'maxOutputTokens', 'number', { min: 1, showIf: (x) => x.kind === 'Inference' }),
    field('models', t, 'embeddingDimensions', 'number', { min: 1, showIf: (x) => x.kind === 'Embedding' }),
    field('models', t, 'supportsTools', 'checkbox', { showIf: (x) => x.kind === 'Inference' }),
    field('models', t, 'supportsStreaming', 'checkbox', { showIf: (x) => x.kind === 'Inference' }),
    section('models', t, 'limits'),
    field('models', t, 'maxConcurrency', 'number', { min: 1, max: 256 }),
    field('models', t, 'timeoutSeconds', 'number', { min: 1, max: 3600 }),
    field('models', t, 'maxRetries', 'number', { min: 0, max: 10 }),
    section('models', t, 'validation'),
    {
      name: 'validateDraft',
      type: 'custom',
      render: (data, { disabled }) => <ModelDraftValidator apiClient={apiClient} data={data} toBody={modelEndpointBody} disabled={disabled} />
    }
  ],
  toForm: (r) => ({
    ...r,
    baseUrl: r.baseUrl || '',
    apiKeySecretId: r.apiKeySecretId || '',
    region: r.region || '',
    deploymentName: r.deploymentName || '',
    apiVersion: r.apiVersion || ''
  }),
  toBody: (d) => modelEndpointBody(d),
  validate: (d, t) => {
    const e = {};
    if (!d.name) e.name = t('common.errors.required');
    if (!d.model) e.model = t('common.errors.required');
    if (d.baseUrl && !/^https?:\/\//i.test(d.baseUrl)) e.baseUrl = t('resources.models.errors.baseUrl');
    return e;
  },
  rowActions: (r, ctx) => [
    modelHealthAction(r, ctx),
    {
      label: ctx.t('resources.models.actions.validate'),
      tip: actionTip(ctx.t, 'models', 'validate'),
      onClick: () =>
        ctx.setExtraModal(
          <ResultModal
            title={ctx.t('resources.models.actions.validateTitle', { name: r.name })}
            // The server records every validation in the endpoint's health history; open history panels re-read it.
            renderResult={(x) => <ModelValidationView result={x} />}
            load={() =>
              ctx.apiClient.validateModelEndpoint(r.id).then(
                (result) => notifyValidated(r.id, result),
                (e) => {
                  notifyValidated(r.id);
                  throw e;
                }
              )
            }
            onClose={() => {
              ctx.setExtraModal(null);
              ctx.reload();
            }}
          />
        )
    },
    {
      label: ctx.t('resources.models.actions.listModels'),
      tip: actionTip(ctx.t, 'models', 'listModels'),
      onClick: () =>
        ctx.setExtraModal(
          <ResultModal
            title={ctx.t('resources.models.actions.listModelsTitle', { name: r.name })}
            load={() => ctx.apiClient.listProviderModels(r.id)}
            renderResult={(x) => <ProviderModelList t={ctx.t} models={x} />}
            onClose={() => ctx.setExtraModal(null)}
          />
        )
    }
  ]
};

/** The provider's model names (a bare array, or { models } from older servers). */
function ProviderModelList({ t, models }) {
  const list = Array.isArray(models) ? models : models?.models || [];
  if (!list.length) return <p className="text-muted">{t('resources.models.noProviderModels')}</p>;
  return (
    <ul className="plain-list" data-testid="provider-models">
      {list.map((m) => (
        <li key={typeof m === 'string' ? m : m.id || m.name}>
          <code>{typeof m === 'string' ? m : m.id || m.name}</code>
        </li>
      ))}
    </ul>
  );
}

// ---------------------------------------------------------------- knowledge (M2)

export const knowledgeConfig = {
  id: 'knowledge',
  explainer: 'knowledge',
  endpoint: '/v1.0/api/knowledge',
  api: hugoApi('listKnowledge', 'createKnowledge', 'updateKnowledge', 'deleteKnowledge'),
  loadOptions: profileAndResourceLookups,
  loadLookups: profileAndResourceLookups,
  blank: () => ({ title: '', content: '', agentProfileId: '', resourceId: '' }),
  columns: ({ t, i18n, lookups }) => [
    col('knowledge', t, 'title', (r) => <strong>{r.title}</strong>, { pinned: true, sortable: true }),
    col('knowledge', t, 'agentProfileId', (r) =>
      r.agentProfileId ? <NamedId id={r.agentProfileId} name={lookups?.profiles?.[r.agentProfileId]} /> : t('resources.knowledge.allProfiles')
    ),
    col('knowledge', t, 'resourceId', (r) => (r.resourceId ? <NamedId id={r.resourceId} name={lookups?.resources?.[r.resourceId]} /> : '-')),
    col('knowledge', t, 'lastUpdateUtc', (r) => timeCell(r.lastUpdateUtc || r.createdUtc, i18n)),
    idCol('knowledge', t)
  ],
  fields: (_d, { t, options }) => [
    field('knowledge', t, 'title', 'text', { required: true }),
    field('knowledge', t, 'agentProfileId', 'select', {
      noneLabel: t('resources.knowledge.allProfiles'),
      options: (options.profileList || []).map((p) => ({ value: p.id, label: profileLabel(p) }))
    }),
    field('knowledge', t, 'resourceId', 'select', { options: (options.resourceList || []).map((r) => ({ value: r.id, label: r.name })) }),
    field('knowledge', t, 'content', 'textarea', { rows: 10, required: true })
  ],
  validate: (d, t) => {
    const e = {};
    if (!d.title) e.title = t('common.errors.required');
    if (!d.content) e.content = t('common.errors.required');
    return e;
  },
  toBody: (d) => ({ title: d.title, content: d.content, agentProfileId: d.agentProfileId || null, resourceId: d.resourceId || null })
};

// ---------------------------------------------------------------- tool invocations

export const toolInvocationsConfig = {
  id: 'toolInvocations',
  explainer: 'toolInvocations',
  endpoint: '/v1.0/api/tool-invocations',
  api: { list: (c, _t, p) => c.listToolInvocations(p), create: null, update: null, remove: null },
  autoRefresh: 15,
  urlFilters: ['surface', 'effect', 'decision', 'status', 'agentProfileId', 'resourceId', 'toolName', 'conversationId'],
  loadLookups: profileAndResourceLookups,
  filters: [
    { name: 'surface', label: (t) => t('resources.toolInvocations.fields.surface'), options: ENUMS.surface, enumGroup: 'surface' },
    { name: 'effect', label: (t) => t('resources.toolInvocations.fields.effect'), options: ENUMS.effect, enumGroup: 'effect' },
    { name: 'decision', label: (t) => t('resources.toolInvocations.fields.decision'), options: ENUMS.toolDecision, enumGroup: 'toolDecision' },
    { name: 'status', label: (t) => t('resources.toolInvocations.fields.status'), options: ENUMS.toolStatus, enumGroup: 'toolStatus' },
    {
      name: 'agentProfileId',
      label: (t) => t('resources.toolInvocations.fields.agentProfileId'),
      options: (lookups) => Object.keys(lookups.profiles || {}),
      optionLabel: (v, _t, lookups) => lookups?.profiles?.[v] || v
    },
    {
      name: 'resourceId',
      label: (t) => t('resources.toolInvocations.fields.resourceId'),
      options: (lookups) => Object.keys(lookups.resources || {}),
      optionLabel: (v, _t, lookups) => lookups?.resources?.[v] || v
    },
    { name: 'toolName', type: 'text', label: (t) => t('resources.toolInvocations.fields.toolName') },
    { name: 'principalId', type: 'text', label: (t) => t('resources.toolInvocations.fields.principalId') },
    { name: 'fromUtc', type: 'datetime', label: (t) => t('resources.toolInvocations.fields.fromUtc') },
    { name: 'toUtc', type: 'datetime', label: (t) => t('resources.toolInvocations.fields.toUtc') }
  ],
  search: false,
  columns: ({ t, i18n, lookups }) => [
    col('toolInvocations', t, 'createdUtc', (r) => timeCell(r.createdUtc, i18n), { sortable: true, pinned: true }),
    col('toolInvocations', t, 'toolName', (r) => <code>{r.toolName}</code>),
    col(
      'toolInvocations',
      t,
      'resourceId',
      (r) => <NamedId id={r.resourceId} name={r.bindingAlias || r.resourceAlias || lookups?.resources?.[r.resourceId]} />,
      {
        csv: (r) => r.bindingAlias || r.resourceAlias || lookups?.resources?.[r.resourceId] || r.resourceId
      }
    ),
    col('toolInvocations', t, 'effect', (r) => <EffectBadge effect={r.effect} />, { csv: (r) => r.effect }),
    col('toolInvocations', t, 'decision', (r) => <EnumBadge group="toolDecision" value={r.decision} tone={toolDecisionTone(r.decision)} />, {
      csv: (r) => r.decision
    }),
    col('toolInvocations', t, 'status', (r) => <EnumBadge group="toolStatus" value={r.status} tone={toolStatusTone(r.status)} />, { csv: (r) => r.status }),
    col('toolInvocations', t, 'durationMs', (r) => formatDuration(r.durationMs, i18n.language), { cellClass: 'right' }),
    col('toolInvocations', t, 'surface', (r) => <EnumBadge group="surface" value={r.surface} />, { csv: (r) => r.surface }),
    col('toolInvocations', t, 'agentProfileId', (r) => <NamedId id={r.agentProfileId} name={lookups?.profiles?.[r.agentProfileId]} />, { defaultHidden: true }),
    col('toolInvocations', t, 'principalId', (r) => r.principalName || r.principalId || '-', { defaultHidden: true }),
    idCol('toolInvocations', t)
  ],
  fields: (d, { t }) => [
    {
      name: 'detail',
      type: 'custom',
      render: (data) => <ToolInvocationDetail t={t} r={data} />
    }
  ],
  formSize: 'xlarge',
  canDuplicate: false
};

/** Tool invocation detail: identity, classification, redacted arguments, output preview, approval, and trace link. */
export function ToolInvocationDetail({ t, r }) {
  const grafana = useGrafana();
  const trace = traceUrl(grafana, r);
  return (
    <div className="detail-sections" data-testid="tool-invocation-detail">
      <dl className="kv-grid">
        <dt>{t('resources.toolInvocations.columns.toolName')}</dt>
        <dd>
          <code>{r.toolName}</code>
        </dd>
        <dt>{t('resources.toolInvocations.columns.resourceId')}</dt>
        <dd>{r.bindingAlias || r.resourceAlias || (r.resourceId ? <CopyableId value={r.resourceId} max={80} /> : '-')}</dd>
        <dt>{t('resources.toolInvocations.columns.principalId')}</dt>
        <dd>{r.principalName || (r.principalId ? <CopyableId value={r.principalId} max={80} /> : '-')}</dd>
        <dt>{t('resources.toolInvocations.columns.effect')}</dt>
        <dd>
          <EffectBadge effect={r.effect} /> {r.effectReason && <span className="text-muted">{r.effectReason}</span>}
        </dd>
        <dt>{t('resources.toolInvocations.fields.accessMode')}</dt>
        <dd>{r.accessMode ? <EnumBadge group="accessMode" value={r.accessMode} tone={accessModeTone(r.accessMode)} /> : '-'}</dd>
        <dt>{t('resources.toolInvocations.columns.decision')}</dt>
        <dd>
          <EnumBadge group="toolDecision" value={r.decision} tone={toolDecisionTone(r.decision)} />
          {r.decisionReason && <span className="text-muted"> {r.decisionReason}</span>}
        </dd>
        <dt>{t('resources.toolInvocations.columns.status')}</dt>
        <dd>
          <EnumBadge group="toolStatus" value={r.status} tone={toolStatusTone(r.status)} />
        </dd>
        <dt>{t('resources.toolInvocations.fields.outputBytes')}</dt>
        <dd>
          {formatBytes(r.outputBytes)} {r.outputTruncated && <Badge tone="warning">{t('resources.toolInvocations.truncated')}</Badge>}
        </dd>
        <dt>{t('resources.toolInvocations.fields.approvalId')}</dt>
        <dd>{r.approvalId ? <CopyableId value={r.approvalId} /> : '-'}</dd>
        <dt>{t('resources.toolInvocations.fields.traceId')}</dt>
        <dd>
          {r.traceId ? (
            <>
              <CopyableId value={r.traceId} />{' '}
              {trace && (
                <a href={trace} target="_blank" rel="noopener noreferrer" {...tip(t('tips.resources.toolInvocations.trace'))}>
                  {t('resources.toolInvocations.openTrace')}
                </a>
              )}
            </>
          ) : (
            '-'
          )}
        </dd>
      </dl>
      {r.errorMessage && (
        <div className="error-banner">
          {r.errorCode ? `${r.errorCode}: ` : ''}
          {r.errorMessage}
        </div>
      )}
      <div className="section-title">{t('resources.toolInvocations.fields.argumentsRedacted')}</div>
      <CodeBlock value={r.argumentsRedacted ?? r.arguments} prettyJson />
      <div className="section-title">{t('resources.toolInvocations.fields.outputPreview')}</div>
      <CodeBlock value={r.outputPreview} />
    </div>
  );
}

// ---------------------------------------------------------------- sessions

export const sessionsConfig = {
  id: 'sessions',
  explainer: 'sessions',
  endpoint: '/v1.0/api/sessions',
  api: { list: (c, _t, p) => c.listSessions(p), create: null, update: null, remove: (c, _t, id) => c.revokeSession(id) },
  search: false,
  urlFilters: ['principalType', 'userId', 'credentialId'],
  filters: [
    { name: 'principalType', label: (t) => t('resources.sessions.fields.principalType'), options: ENUMS.principalType, enumGroup: 'principalType' },
    { name: 'userId', type: 'text', label: (t) => t('resources.sessions.fields.userId') },
    { name: 'credentialId', type: 'text', label: (t) => t('resources.sessions.fields.credentialId') }
  ],
  columns: ({ t, i18n }) => [
    col('sessions', t, 'principalName', (r) => <strong>{r.principalName || '-'}</strong>, { pinned: true }),
    col('sessions', t, 'principalType', (r) => <EnumBadge group="principalType" value={r.principalType} />, { csv: (r) => r.principalType }),
    col('sessions', t, 'authScheme', (r) => (r.authScheme ? <EnumBadge group="authScheme" value={r.authScheme} /> : '-'), { csv: (r) => r.authScheme || '' }),
    col('sessions', t, 'sourceIp', (r) => (r.sourceIp ? <code>{r.sourceIp}</code> : '-')),
    col('sessions', t, 'issuedUtc', (r) => timeCell(r.issuedUtc || r.createdUtc, i18n), { sortable: true }),
    col('sessions', t, 'lastUsedUtc', (r) => timeCell(r.lastUsedUtc, i18n)),
    col('sessions', t, 'expiresUtc', (r) => timeCell(r.expiresUtc, i18n)),
    col(
      'sessions',
      t,
      'revokedUtc',
      (r) => (r.revokedUtc ? <Badge tone="danger">{t('resources.sessions.revoked')}</Badge> : <Badge tone="success">{t('common.generic.active')}</Badge>),
      { csv: (r) => (r.revokedUtc ? 'revoked' : 'active') }
    ),
    col('sessions', t, 'userAgent', (r) => <span className="cell-wrap">{r.userAgent || '-'}</span>, { defaultHidden: true }),
    idCol('sessions', t)
  ],
  fields: (_d, { t }) => [
    field('sessions', t, 'principalName', 'text', { readOnly: true }),
    field('sessions', t, 'authScheme', 'text', { readOnly: true }),
    field('sessions', t, 'sourceIp', 'text', { readOnly: true }),
    field('sessions', t, 'userAgent', 'textarea', { readOnly: true })
  ],
  canDelete: (r) => !r.revokedUtc,
  canDuplicate: false
};

// ---------------------------------------------------------------- plugins (system administrators)

export const pluginsConfig = {
  id: 'plugins',
  explainer: 'plugins',
  endpoint: '/v1.0/api/system/plugins',
  api: { list: (c, _t, p) => c.listPlugins(p), create: null, update: (c, _t, id, b) => c.updatePlugin(id, b), remove: null },
  search: false,
  columns: ({ t }) => [
    nameCol('plugins', t),
    col('plugins', t, 'version', (r) => <code>{r.version || '-'}</code>),
    col('plugins', t, 'connectorTypes', (r) => (r.connectorTypes || []).join(', ') || '-', { csv: (r) => (r.connectorTypes || []).join(' ') }),
    col(
      'plugins',
      t,
      'enabled',
      (r) => (r.enabled ? <Badge tone="success">{t('common.generic.enabled')}</Badge> : <Badge>{t('common.generic.disabled')}</Badge>),
      { csv: (r) => String(!!r.enabled) }
    ),
    col('plugins', t, 'loadError', (r) => (r.loadError ? <span className="text-danger cell-wrap">{r.loadError}</span> : '-')),
    col('plugins', t, 'sha256', (r) => (r.sha256 ? <CopyableId value={r.sha256} max={16} /> : '-'), { defaultHidden: true }),
    idCol('plugins', t)
  ],
  fields: (_d, { t }) => [
    field('plugins', t, 'name', 'text', { readOnly: true }),
    field('plugins', t, 'version', 'text', { readOnly: true }),
    field('plugins', t, 'enabled', 'checkbox')
  ],
  toBody: (d) => ({ enabled: !!d.enabled }),
  canDuplicate: false,
  rowActions: (r, ctx) => [
    {
      label: r.enabled ? ctx.t('resources.plugins.actions.disable') : ctx.t('resources.plugins.actions.enable'),
      tip: actionTip(ctx.t, 'plugins', r.enabled ? 'disable' : 'enable'),
      onClick: async () => {
        try {
          await ctx.apiClient.updatePlugin(r.id, { enabled: !r.enabled });
          ctx.addToast({ type: 'success', message: ctx.t('common.toasts.saved') });
          ctx.reload();
        } catch (e) {
          ctx.addToast({ type: 'error', message: errorMessage(ctx.t, e) });
        }
      }
    }
  ]
};
