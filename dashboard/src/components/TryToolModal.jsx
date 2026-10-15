import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import Modal from './Modal';
import CodeBlock from './CodeBlock';
import { FormField } from './FormFields';
import { EffectBadge, EnumBadge } from './StatusBadge';
import { ErrorPresenter } from './States';
import { tip } from './Tooltip';
import { useAuth } from '../context/AuthContext';
import { toolDecisionTone } from '../utils/constants';

/** Tools that operate on this resource: tools whose `resource` argument accepts the resource's alias in this profile. */
export function toolsForResource(tools, aliases) {
  const list = Array.isArray(tools) ? tools : tools?.items || tools?.tools || [];
  if (!aliases?.length) return list;
  return list.filter((tool) => {
    const schema = typeof tool.inputSchema === 'string' ? safeParse(tool.inputSchema) : tool.inputSchema || safeParse(tool.inputSchemaJson);
    const allowed = schema?.properties?.resource?.enum;
    return !Array.isArray(allowed) || allowed.some((a) => aliases.includes(a));
  });
}

function safeParse(text) {
  try {
    return text ? JSON.parse(text) : null;
  } catch {
    return null;
  }
}

/**
 * Try a tool on a resource (P8.30): choose a profile that binds the resource, pick one of the tools the profile
 * exposes for it, edit the arguments, then Dry run (classify only: effect, decision, access mode) before Execute
 * (the same invocation pipeline as every surface, so approvals and limits apply).
 */
export default function TryToolModal({ resource, onClose }) {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
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
        setProfiles(r.items || []);
        if (r.items?.length) setProfileId(r.items[0].id);
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
      const aliases = bound.map((x) => x.alias);
      const list = tl.status === 'fulfilled' ? toolsForResource(tl.value, aliases) : [];
      setTools(list);
      setToolName(list[0]?.name || '');
      setArgs(JSON.stringify({ resource: aliases[0] || resource.name }, null, 2));
    });
  }, [apiClient, profileId, resource]);

  const parsedArgs = useMemo(() => safeParse(args), [args]);
  const body = () => ({ agentProfileId: profileId, toolName, arguments: parsedArgs || {} });

  const run = async (kind) => {
    if (!parsedArgs) {
      setError({ status: 400, code: 'InvalidJson', message: t('common.errors.invalidJson') });
      return;
    }
    setBusy(true);
    setError(null);
    try {
      if (kind === 'dry') setDryRun(await apiClient.dryRunTool(body()));
      else setResult(await apiClient.invokeTool(body()));
    } catch (e) {
      setError(e);
    } finally {
      setBusy(false);
    }
  };

  const tool = tools.find((x) => x.name === toolName);

  return (
    <Modal
      open
      size="large"
      onClose={onClose}
      title={t('views.resources.tryTool.title', { name: resource.name })}
      testId="try-tool"
      footer={
        <>
          <button type="button" className="button-secondary" onClick={onClose} disabled={busy} {...tip(t('tips.modal.close'))}>
            {t('common.actions.close')}
          </button>
          <button type="button" className="button-secondary" onClick={() => run('dry')} disabled={busy || !toolName} data-testid="dry-run" {...tip(t('tips.views.resources.tryTool.dryRun'))}>
            {t('views.resources.tryTool.dryRun')}
          </button>
          <button type="button" className="button-primary" onClick={() => run('execute')} disabled={busy || !toolName || !dryRun} data-testid="execute-tool" {...tip(t('tips.views.resources.tryTool.execute'))}>
            {t('views.resources.tryTool.execute')}
          </button>
        </>
      }
    >
      <p className="text-muted">{t('views.resources.tryTool.body')}</p>
      <div className="form-grid">
        <FormField
          id="try-profile"
          field={{ name: 'profile', type: 'select', required: true, label: t('views.resources.tryTool.profile'), tip: t('tips.views.resources.tryTool.profile'), options: profiles.map((p) => ({ value: p.id, label: p.displayName || p.name })) }}
          value={profileId}
          onChange={setProfileId}
        />
        <FormField
          id="try-tool-name"
          field={{ name: 'tool', type: 'select', required: true, label: t('views.resources.tryTool.tool'), tip: t('tips.views.resources.tryTool.tool'), options: tools.map((x) => ({ value: x.name, label: x.title || x.name })) }}
          value={toolName}
          onChange={(v) => {
            setToolName(v);
            setDryRun(null);
            setResult(null);
          }}
        />
      </div>
      {profileId && bindings.length === 0 && <div className="warning-banner">{t('views.resources.tryTool.notBound')}</div>}
      {tool?.description && <p className="field-hint">{tool.description}</p>}
      <FormField id="try-args" field={{ name: 'arguments', type: 'json', rows: 8, label: t('views.resources.tryTool.arguments'), tip: t('tips.views.resources.tryTool.arguments') }} value={args} onChange={(v) => setArgs(v)} error={parsedArgs ? null : t('common.errors.invalidJson')} />
      {error && <ErrorPresenter error={error} />}
      {dryRun && (
        <div className="panel" data-testid="dry-run-result">
          <div className="section-title">{t('views.resources.tryTool.classification')}</div>
          <div className="inline-meta">
            <EffectBadge effect={dryRun.effect} />
            <EnumBadge group="toolDecision" value={dryRun.decision} tone={toolDecisionTone(dryRun.decision)} />
            {dryRun.accessMode && <EnumBadge group="accessMode" value={dryRun.accessMode} />}
          </div>
          {dryRun.effectReason && <p className="field-hint">{dryRun.effectReason}</p>}
        </div>
      )}
      {result && (
        <div className="panel" data-testid="execute-result">
          <div className="section-title">{t('views.resources.tryTool.result')}</div>
          {result.approvalId && <div className="warning-banner">{t('views.resources.tryTool.pendingApproval', { id: result.approvalId })}</div>}
          <CodeBlock value={result} prettyJson />
        </div>
      )}
    </Modal>
  );
}
