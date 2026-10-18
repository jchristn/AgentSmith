import { useEffect, useMemo, useState } from 'react';
import { Trans, useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import Modal from './Modal';
import CopyableId from './CopyableId';
import CodeBlock from './CodeBlock';
import { FormField } from './FormFields';
import { EffectBadge, EnumBadge } from './StatusBadge';
import { ErrorPresenter } from './States';
import { tip } from './Tooltip';
import { useAuth } from '../context/AuthContext';
import { usePendingApprovals } from '../context/ApprovalsContext';
import { toolDecisionTone, toolStatusTone, accessModeTone } from '../utils/constants';
import { pathFor } from '../config/navConfig';

function safeParse(text) {
  try {
    return text ? JSON.parse(text) : null;
  } catch {
    return null;
  }
}

/** A tool's input schema, from inputSchemaJson (server) or inputSchema (object or string). */
export function toolSchema(tool) {
  if (!tool) return null;
  if (tool.inputSchema && typeof tool.inputSchema === 'object') return tool.inputSchema;
  return safeParse(typeof tool.inputSchema === 'string' ? tool.inputSchema : tool.inputSchemaJson);
}

/** Tools that operate on this resource: tools whose `resource` argument accepts one of the resource's aliases. */
export function toolsForResource(tools, aliases) {
  const list = Array.isArray(tools) ? tools : [...(tools?.tools || tools?.items || [])];
  if (!aliases?.length) return list;
  return list.filter((tool) => {
    const allowed = toolSchema(tool)?.properties?.resource?.enum;
    return Array.isArray(allowed) && allowed.some((a) => aliases.includes(a));
  });
}

/**
 * A starting arguments object for a tool: its required properties (except `resource`, which the request names
 * separately) with empty values of the right type.
 */
export function argumentSkeleton(tool) {
  const schema = toolSchema(tool);
  const out = {};
  for (const name of schema?.required || []) {
    if (name === 'resource') continue;
    const type = schema.properties?.[name]?.type;
    out[name] = type === 'integer' || type === 'number' ? 0 : type === 'boolean' ? false : type === 'array' ? [] : type === 'object' ? {} : '';
  }
  return out;
}

/** The request body for dry run and invoke: the profile, the binding alias, the tool, and the arguments. */
export function toolCallBody(profileId, alias, toolName, args) {
  const { resource, ...rest } = args || {};
  return { profileId, resource: alias || resource, toolName, arguments: rest };
}

/** The outcome of a dry run or an invocation: decision, effect, access mode, reason, and the result text. */
export function OutcomeView({ outcome, testId }) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  if (!outcome) return null;
  const pending = outcome.decision === 'ApprovalRequired' || outcome.approvalStatus === 'Pending';
  return (
    <div className="panel" data-testid={testId} data-decision={outcome.decision}>
      <div className="inline-meta">
        {outcome.effect && <EffectBadge effect={outcome.effect} />}
        <EnumBadge group="toolDecision" value={outcome.decision} tone={toolDecisionTone(outcome.decision)} />
        {outcome.status && !outcome.dryRun && <EnumBadge group="toolStatus" value={outcome.status} tone={toolStatusTone(outcome.status)} />}
        {outcome.accessMode && <EnumBadge group="accessMode" value={outcome.accessMode} tone={accessModeTone(outcome.accessMode)} />}
        {outcome.bindingAlias && <code>{outcome.bindingAlias}</code>}
      </div>
      {outcome.reason && <p className="field-hint">{outcome.reason}</p>}
      {outcome.effectReason && <p className="field-hint">{t('views.resources.tryTool.effectReason', { reason: outcome.effectReason })}</p>}
      {pending && outcome.approvalId && (
        <div className="warning-banner" data-testid="pending-approval">
          <span>
            <Trans i18nKey="views.resources.tryTool.pendingApproval" components={{ id: <CopyableId value={outcome.approvalId} max={80} /> }} />
          </span>{' '}
          <button
            type="button"
            className="button-primary button-sm"
            onClick={() => navigate(pathFor('approvals'))}
            {...tip(t('tips.views.resources.tryTool.openApprovals'))}
          >
            {t('views.resources.tryTool.openApprovals')}
          </button>
        </div>
      )}
      {outcome.result && !pending && <CodeBlock value={outcome.result.text ?? outcome.result} emptyText={t('views.resources.tryTool.noOutput')} />}
      {outcome.invocationId && (
        <p className="field-hint">
          {t('views.resources.tryTool.recorded')}{' '}
          <a
            href={pathFor('toolInvocations', null, { toolName: outcome.toolName })}
            onClick={(e) => {
              e.preventDefault();
              navigate(pathFor('toolInvocations', null, { toolName: outcome.toolName }));
            }}
            {...tip(t('tips.views.resources.tryTool.openInvocations'))}
          >
            {t('views.resources.tryTool.openInvocations')}
          </a>
        </p>
      )}
    </div>
  );
}

/**
 * Try a tool on a resource (P8.30): choose a profile that binds the resource, pick one of the tools the profile
 * exposes for it, edit the arguments, then Dry run (resolve, authorize, classify, and apply rules without running)
 * before Execute (the same invocation pipeline as every surface, so access modes, approvals, and limits apply).
 */
export function TryToolPanel({ resource }) {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const { refresh } = usePendingApprovals();
  const [profiles, setProfiles] = useState([]);
  const [profileId, setProfileId] = useState('');
  const [bindings, setBindings] = useState([]);
  const [tools, setTools] = useState([]);
  const [toolName, setToolName] = useState('');
  const [args, setArgs] = useState('{}');
  const [dryRun, setDryRun] = useState(null);
  const [result, setResult] = useState(null);
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    apiClient
      .listProfiles({ pageNumber: 1, pageSize: 1000 })
      .then((r) => {
        const list = r.items || [];
        setProfiles(list);
        if (list.length) setProfileId((list.find((p) => p.isDefault) || list[0]).id);
      })
      .catch(setError);
  }, [apiClient]);

  useEffect(() => {
    if (!profileId) return;
    setDryRun(null);
    setResult(null);
    Promise.allSettled([apiClient.listBindings(profileId, { pageNumber: 1, pageSize: 1000 }), apiClient.getProfileTools(profileId)]).then(([b, tl]) => {
      const bound = b.status === 'fulfilled' ? (b.value.items || []).filter((x) => x.resourceId === resource.id) : [];
      setBindings(bound);
      const list =
        tl.status === 'fulfilled' && bound.length
          ? toolsForResource(
              tl.value,
              bound.map((x) => x.alias)
            )
          : [];
      setTools(list);
      const first = list[0];
      setToolName(first?.name || '');
      setArgs(JSON.stringify(argumentSkeleton(first), null, 2));
    });
  }, [apiClient, profileId, resource]);

  const parsedArgs = useMemo(() => safeParse(args), [args]);
  const alias = bindings[0]?.alias;
  const tool = tools.find((x) => x.name === toolName);

  const run = async (kind) => {
    if (!parsedArgs) {
      setError({ status: 400, code: 'InvalidJson', message: t('common.errors.invalidJson') });
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const body = toolCallBody(profileId, alias, toolName, parsedArgs);
      if (kind === 'dry') {
        setDryRun(await apiClient.dryRunTool(body));
        setResult(null);
      } else {
        setResult(await apiClient.invokeTool(body));
        refresh?.();
      }
    } catch (e) {
      setError(e);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="try-tool" data-testid="try-tool-panel">
      <p className="text-muted">{t('views.resources.tryTool.body')}</p>
      <div className="form-grid">
        <FormField
          id="try-profile"
          field={{
            name: 'profile',
            type: 'select',
            required: true,
            label: t('views.resources.tryTool.profile'),
            tip: t('tips.views.resources.tryTool.profile'),
            options: profiles.map((p) => ({ value: p.id, label: p.displayName || p.name }))
          }}
          value={profileId}
          onChange={setProfileId}
        />
        <FormField
          id="try-tool-name"
          field={{
            name: 'tool',
            type: 'select',
            required: true,
            label: t('views.resources.tryTool.tool'),
            tip: t('tips.views.resources.tryTool.tool'),
            options: tools.map((x) => ({ value: x.name, label: x.name }))
          }}
          value={toolName}
          onChange={(v) => {
            setToolName(v);
            setArgs(JSON.stringify(argumentSkeleton(tools.find((x) => x.name === v)), null, 2));
            setDryRun(null);
            setResult(null);
          }}
        />
      </div>
      {profileId && bindings.length === 0 && <div className="warning-banner">{t('views.resources.tryTool.notBound')}</div>}
      {alias && (
        <p className="field-hint">
          {t('views.resources.tryTool.asAlias', { alias })}{' '}
          <EnumBadge group="accessMode" value={bindings[0].accessMode} tone={accessModeTone(bindings[0].accessMode)} />
        </p>
      )}
      {tool?.description && <p className="field-hint">{tool.description}</p>}
      <FormField
        id="try-args"
        field={{ name: 'arguments', type: 'json', rows: 6, label: t('views.resources.tryTool.arguments'), tip: t('tips.views.resources.tryTool.arguments') }}
        value={args}
        onChange={(v) => setArgs(v)}
        error={parsedArgs ? null : t('common.errors.invalidJson')}
      />
      <div className="panel-actions">
        <button
          type="button"
          className="button-secondary"
          onClick={() => run('dry')}
          disabled={busy || !toolName || !alias}
          data-testid="dry-run"
          {...tip(t('tips.views.resources.tryTool.dryRun'))}
        >
          {t('views.resources.tryTool.dryRun')}
        </button>
        <button
          type="button"
          className="button-primary"
          onClick={() => run('execute')}
          disabled={busy || !toolName || !alias || !dryRun}
          data-testid="execute-tool"
          {...tip(t('tips.views.resources.tryTool.execute'))}
        >
          {t('views.resources.tryTool.execute')}
        </button>
      </div>
      {error && <ErrorPresenter error={error} />}
      {dryRun && (
        <>
          <div className="section-title">{t('views.resources.tryTool.classification')}</div>
          <OutcomeView outcome={dryRun} testId="dry-run-result" />
        </>
      )}
      {result && (
        <>
          <div className="section-title">{t('views.resources.tryTool.result')}</div>
          <OutcomeView outcome={result} testId="execute-result" />
        </>
      )}
    </div>
  );
}

/** The try-a-tool panel in a modal, opened from a resource row. */
export default function TryToolModal({ resource, onClose }) {
  const { t } = useTranslation();
  return (
    <Modal
      open
      size="large"
      onClose={onClose}
      title={t('views.resources.tryTool.title', { name: resource.name })}
      testId="try-tool"
      footer={
        <button type="button" className="button-secondary" onClick={onClose} {...tip(t('tips.modal.close'))}>
          {t('common.actions.close')}
        </button>
      }
    >
      <TryToolPanel resource={resource} />
    </Modal>
  );
}
