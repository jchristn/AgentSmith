import { useCallback, useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate, useSearchParams } from 'react-router-dom';
import PageHeader from '../components/PageHeader';
import FormFields, { FormField } from '../components/FormFields';
import Tabs from '../components/Tabs';
import Modal from '../components/Modal';
import ConfirmModal from '../components/ConfirmModal';
import JsonViewerModal from '../components/JsonViewerModal';
import CodeBlock from '../components/CodeBlock';
import CopyButton from '../components/CopyButton';
import ProfileChatLink from '../components/ProfileChatLink';
import RowActions from '../components/RowActions';
import { LoadingState, ErrorPresenter, UnavailableState, EmptyState } from '../components/States';
import { Badge, EffectBadge, EnumBadge } from '../components/StatusBadge';
import { tip } from '../components/Tooltip';
import { PlusIcon } from '../components/Icons';
import ProfileDocuments from '../components/ProfileDocuments';
import { useAuth } from '../context/AuthContext';
import { useApp } from '../context/AppContext';
import { useToast } from '../context/ToastContext';
import { pathFor } from '../config/navConfig';
import { profilesConfig, profileToForm } from './hugoConfigs';
import CompiledInstructionsView from '../components/CompiledInstructionsView';
import { ToolFlags } from './ConnectorDetailView';
import { accessModeTone } from '../utils/constants';
import { enumOptions } from '../utils/enums';
import { errorMessage, isMissingEndpoint } from '../utils/errors';
import { resourceTip } from '../utils/tips';
import { mcpConfigSnippet, claudeCliSnippet, stdioSnippet, a2aCardUrl, openAiBaseUrl } from '../utils/snippets';

const TABS = ['general', 'bindings', 'instructions', 'tools', 'connect'];

/** Blank binding for the add binding modal. */
export function blankBinding() {
  return {
    resourceId: '',
    alias: '',
    accessMode: 'ReadOnly',
    requireApprovalForDestructive: true,
    instructionMode: 'Inherit',
    instructionText: '',
    maxOutputBytes: null,
    maxRows: null,
    enabled: true,
    allowKnowledgeUpdates: true,
    denyPatterns: []
  };
}

/** Validate a binding: a resource, a unique alias (letters, digits, dot, dash, underscore), and an access mode. */
export function validateBinding(t, binding, existing = []) {
  const e = {};
  if (!binding.resourceId) e.resourceId = t('common.errors.required');
  if (!binding.alias) e.alias = t('common.errors.required');
  else if (!/^[A-Za-z0-9][A-Za-z0-9_.-]*$/.test(binding.alias)) e.alias = t('views.profile.bindings.aliasInvalid');
  else if (existing.some((b) => b.alias === binding.alias && b.id !== binding.id)) e.alias = t('views.profile.bindings.aliasTaken');
  if (binding.instructionMode !== 'Inherit' && !binding.instructionText) e.instructionText = t('common.errors.required');
  return e;
}

function bindingFields(t, resources, binding) {
  const f = (name, type, extra = {}) => ({ name, type, label: t(`views.profile.bindings.fields.${name}`), tip: resourceTip(t, 'bindings', name), ...extra });
  const resourceOptions = resources.map((r) => ({ value: r.id, label: `${r.name} (${r.connectorType})` }));
  if (binding?.resourceId && !resourceOptions.some((r) => r.value === binding.resourceId)) {
    resourceOptions.push({ value: binding.resourceId, label: binding.resourceName || binding.resourceId });
  }
  return [
    f('resourceId', 'select', { required: true, readOnly: !!binding?.id, options: resourceOptions }),
    f('alias', 'text', { required: true }),
    f('accessMode', 'select', { required: true, options: enumOptions(t, 'accessMode') }),
    f('requireApprovalForDestructive', 'checkbox', { showIf: (d) => d.accessMode === 'ReadWrite' }),
    f('instructionMode', 'select', { required: true, options: enumOptions(t, 'instructionMode') }),
    f('instructionText', 'textarea', { rows: 4, showIf: (d) => d.instructionMode !== 'Inherit' }),
    f('maxOutputBytes', 'number', { min: 1 }),
    f('maxRows', 'number', { min: 1 }),
    f('denyPatterns', 'stringList'),
    f('allowKnowledgeUpdates', 'checkbox'),
    f('enabled', 'checkbox')
  ];
}

/** Tool rules editor for one binding: Allow, Deny, or RequireApproval per tool (unset follows the access mode). */
function ToolRulesModal({ profileId, binding, tools, onClose }) {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const { addToast } = useToast();
  const [rules, setRules] = useState(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    apiClient
      .getToolRules(profileId, binding.id)
      .then((r) => setRules(Object.fromEntries((Array.isArray(r) ? r : r?.rules || r?.items || []).map((x) => [x.toolName, x.rule]))))
      .catch(() => setRules({}));
  }, [apiClient, profileId, binding.id]);

  const save = async () => {
    setBusy(true);
    try {
      await apiClient.updateToolRules(
        profileId,
        binding.id,
        Object.entries(rules || {})
          .filter(([, rule]) => rule)
          .map(([toolName, rule]) => ({ toolName, rule }))
      );
      addToast({ type: 'success', message: t('common.toasts.saved') });
      onClose();
    } catch (e) {
      addToast({ type: 'error', message: errorMessage(t, e) });
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal
      open
      size="large"
      onClose={onClose}
      title={t('views.profile.toolRules.title', { alias: binding.alias })}
      testId="tool-rules"
      footer={
        <>
          <button type="button" className="button-secondary" onClick={onClose} disabled={busy} {...tip(t('tips.modal.cancelForm'))}>
            {t('common.actions.cancel')}
          </button>
          <button type="button" className="button-primary" onClick={save} disabled={busy || rules === null} {...tip(t('tips.views.profile.toolRules.save'))}>
            {t('common.actions.save')}
          </button>
        </>
      }
    >
      <p className="text-muted">{t('views.profile.toolRules.body')}</p>
      {rules === null ? (
        <LoadingState />
      ) : tools.length === 0 ? (
        <div className="text-muted">{t('views.profile.toolRules.noTools')}</div>
      ) : (
        <div className="table-scroll">
          <table className="data-table">
            <thead>
              <tr>
                <th scope="col" {...tip(t('tips.views.profile.tools.name'))}>
                  {t('views.profile.tools.name')}
                </th>
                <th scope="col" {...tip(t('tips.views.profile.tools.effect'))}>
                  {t('views.profile.tools.effect')}
                </th>
                <th scope="col" {...tip(t('tips.views.profile.toolRules.rule'))}>
                  {t('views.profile.toolRules.rule')}
                </th>
              </tr>
            </thead>
            <tbody>
              {tools.map((tool) => (
                <tr key={tool.name}>
                  <td>
                    <code>{tool.name}</code>
                  </td>
                  <td>
                    <EffectBadge effect={tool.baseEffect || tool.effect} />
                  </td>
                  <td>
                    <select
                      value={rules[tool.name] || ''}
                      onChange={(e) => setRules((r) => ({ ...r, [tool.name]: e.target.value }))}
                      aria-label={t('views.profile.toolRules.ruleFor', { tool: tool.name })}
                      {...tip(t('tips.views.profile.toolRules.rule'))}
                    >
                      <option value="">{t('views.profile.toolRules.inherit')}</option>
                      {enumOptions(t, 'toolRule').map((o) => (
                        <option key={o.value} value={o.value}>
                          {o.label}
                        </option>
                      ))}
                    </select>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Modal>
  );
}

/** One copyable snippet with a title and a short explanation. */
function Snippet({ title, body, value, testId }) {
  return (
    <section className="panel snippet" data-testid={testId}>
      <h3 className="section-title">{title}</h3>
      {body && <p className="field-hint">{body}</p>}
      <CodeBlock value={value} />
    </section>
  );
}

/**
 * Agent profile detail (P8.22 to P8.25): General (persona, address, endpoints, limits, exposure), Bindings (add a
 * resource with an alias and access mode, tool rules), Instructions (the compiled instructions with their source
 * map), Tools (as a caller sees them), and Connect (MCP, A2A, and OpenAI-compatible snippets).
 */
export default function ProfileDetailView({ recordId }) {
  const { t } = useTranslation();
  const { apiClient, serverUrl } = useAuth();
  const { tenantId } = useApp();
  const { addToast } = useToast();
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const tab = TABS.includes(searchParams.get('tab')) ? searchParams.get('tab') : 'general';
  const [profile, setProfile] = useState(null);
  const [form, setForm] = useState(null);
  const [error, setError] = useState(null);
  const [errors, setErrors] = useState({});
  const [busy, setBusy] = useState(false);
  const [options, setOptions] = useState({});
  const [bindings, setBindings] = useState(null);
  const [bindingsError, setBindingsError] = useState(null);
  const [resources, setResources] = useState([]);
  const [editingBinding, setEditingBinding] = useState(null);
  const [bindingErrors, setBindingErrors] = useState({});
  const [deleteBinding, setDeleteBinding] = useState(null);
  const [rulesFor, setRulesFor] = useState(null);
  const [instructions, setInstructions] = useState(null);
  const [instructionsError, setInstructionsError] = useState(null);
  const [surface, setSurface] = useState('Chat');
  const [tools, setTools] = useState(null);
  const [catalog, setCatalog] = useState(null);
  const [toolsError, setToolsError] = useState(null);
  const [card, setCard] = useState(null);
  const [jsonOpen, setJsonOpen] = useState(false);

  const load = useCallback(() => {
    apiClient
      .getProfile(recordId)
      .then((p) => {
        setProfile(p);
        setForm({ ...profilesConfig.blank(), ...profileToForm(p) });
        setError(null);
      })
      .catch(setError);
  }, [apiClient, recordId]);

  const loadBindings = useCallback(() => {
    apiClient
      .listBindings(recordId, { pageNumber: 1, pageSize: 1000 })
      .then((r) => {
        setBindings(r.items || []);
        setBindingsError(null);
      })
      .catch((e) => {
        setBindings([]);
        setBindingsError(e);
      });
  }, [apiClient, recordId]);

  useEffect(load, [load]);
  useEffect(loadBindings, [loadBindings]);
  useEffect(() => {
    profilesConfig
      .loadOptions({ apiClient })
      .then(setOptions)
      .catch(() => setOptions({}));
    apiClient
      .listResources({ pageNumber: 1, pageSize: 1000 })
      .then((r) => setResources(r.items || []))
      .catch(() => setResources([]));
  }, [apiClient]);

  useEffect(() => {
    if (tab !== 'instructions') return;
    setInstructions(null);
    apiClient
      .getCompiledInstructions(recordId, { surface })
      .then((r) => {
        setInstructions(typeof r === 'string' ? { text: r, segments: [] } : r);
        setInstructionsError(null);
      })
      .catch(setInstructionsError);
  }, [apiClient, recordId, surface, tab]);

  useEffect(() => {
    if (tab !== 'tools' && tab !== 'bindings') return;
    apiClient
      .getProfileTools(recordId)
      .then((r) => {
        setTools(Array.isArray(r) ? r : [...(r?.tools || r?.items || []), ...(r?.metaTools || [])]);
        setCatalog(Array.isArray(r) ? null : r);
        setToolsError(null);
      })
      .catch((e) => {
        setTools([]);
        setToolsError(e);
      });
  }, [apiClient, recordId, tab]);

  useEffect(() => {
    if (tab !== 'connect') return;
    apiClient
      .getProfileCard(recordId)
      .then(setCard)
      .catch(() => setCard(null));
  }, [apiClient, recordId, tab]);

  const fields = useMemo(() => (form ? profilesConfig.fields(form, { t, options, mode: 'edit' }) : []), [form, t, options]);

  const save = async () => {
    const problems = profilesConfig.validate(form, t);
    setErrors(problems);
    if (Object.keys(problems).length) return;
    setBusy(true);
    try {
      await apiClient.updateProfile(recordId, profilesConfig.toBody(form));
      addToast({ type: 'success', message: t('common.toasts.saved') });
      load();
    } catch (e) {
      addToast({ type: 'error', message: errorMessage(t, e) });
    } finally {
      setBusy(false);
    }
  };

  const saveBinding = async () => {
    const b = editingBinding;
    const problems = validateBinding(t, b, bindings || []);
    setBindingErrors(problems);
    if (Object.keys(problems).length) return;
    setBusy(true);
    try {
      const body = {
        ...(!b.id ? { resourceId: b.resourceId } : {}),
        alias: b.alias,
        accessMode: b.accessMode,
        requireApprovalForDestructive: !!b.requireApprovalForDestructive,
        instructionMode: b.instructionMode,
        instructionText: b.instructionMode === 'Inherit' ? null : b.instructionText,
        maxOutputBytes: b.maxOutputBytes ?? null,
        maxRows: b.maxRows ?? null,
        enabled: !!b.enabled,
        allowKnowledgeUpdates: b.allowKnowledgeUpdates !== false,
        denyPatterns: (b.denyPatterns || []).map((x) => String(x).trim()).filter(Boolean)
      };
      if (b.id) await apiClient.updateBinding(recordId, b.id, body);
      else await apiClient.createBinding(recordId, body);
      addToast({ type: 'success', message: t(b.id ? 'common.toasts.saved' : 'common.toasts.created') });
      setEditingBinding(null);
      loadBindings();
    } catch (e) {
      addToast({ type: 'error', message: errorMessage(t, e) });
    } finally {
      setBusy(false);
    }
  };

  const removeBinding = async () => {
    try {
      await apiClient.deleteBinding(recordId, deleteBinding.id);
      addToast({ type: 'success', message: t('common.toasts.deleted') });
      loadBindings();
    } catch (e) {
      addToast({ type: 'error', message: errorMessage(t, e) });
    } finally {
      setDeleteBinding(null);
    }
  };

  if (error && isMissingEndpoint(error)) {
    return (
      <div>
        <PageHeader title={t('views.profile.title')} subtitle={t('resources.profiles.subtitle')} />
        <UnavailableState feature={t('resources.profiles.title')} purpose={t('resources.profiles.purpose')} endpoint="/v1.0/api/agent-profiles/{id}" />
      </div>
    );
  }
  if (error) return <ErrorPresenter error={error} onRetry={load} />;
  if (!profile || !form) return <LoadingState />;

  const resourceName = (id) => resources.find((r) => r.id === id)?.name || id;
  const tabs = TABS.map((id) => ({ id, label: t(`views.profile.tabs.${id}`), tip: t(`tips.views.profile.tabs.${id}`) }));
  const mcpProfile = profile.name;

  return (
    <div className="profile-detail">
      <PageHeader
        title={profile.displayName || profile.name}
        subtitle={t('views.profile.subtitle', { persona: profile.personaName || 'Agent Smith', address: profile.userAddress || 'Mr. Anderson' })}
        actions={
          <>
            <button type="button" className="button-secondary" onClick={() => navigate(pathFor('profiles'))} {...tip(t('tips.views.profile.back'))}>
              {t('common.actions.back')}
            </button>
            <button type="button" className="button-secondary" onClick={() => setJsonOpen(true)} {...tip(t('tips.actions.viewJson'))}>
              {t('common.actions.viewJson')}
            </button>
            <button type="button" className="button-primary" onClick={() => navigate(pathFor('chat'))} {...tip(t('tips.views.profile.chat'))}>
              {t('resources.profiles.actions.chat')}
            </button>
          </>
        }
      />
      <Tabs label={t('views.profile.title')} active={tab} onChange={(id) => setSearchParams(new URLSearchParams({ tab: id }), { replace: true })} tabs={tabs} />

      {tab === 'general' && (
        <div className="panel" data-testid="profile-general">
          {Object.keys(errors).length > 0 && <div className="error-banner">{t('common.errors.fixFields')}</div>}
          <FormFields fields={fields} data={form} errors={errors} onChange={setForm} idPrefix="profile" />
          <div className="panel-actions">
            <button type="button" className="button-primary" onClick={save} disabled={busy} data-testid="profile-save" {...tip(t('tips.modal.saveEdit'))}>
              {busy ? t('common.generic.saving') : t('common.actions.save')}
            </button>
          </div>
        </div>
      )}

      {tab === 'bindings' && (
        <div className="panel" data-testid="profile-bindings">
          <div className="panel-header">
            <p className="text-muted">{t('views.profile.bindings.body')}</p>
            {!isMissingEndpoint(bindingsError) && (
              <button
                type="button"
                className="button-primary"
                onClick={() => {
                  setBindingErrors({});
                  setEditingBinding(blankBinding());
                }}
                data-testid="add-binding"
                {...tip(t('tips.views.profile.bindings.add'))}
              >
                <PlusIcon size={14} /> {t('views.profile.bindings.add')}
              </button>
            )}
          </div>
          {isMissingEndpoint(bindingsError) ? (
            <UnavailableState
              feature={t('views.profile.tabs.bindings')}
              purpose={t('views.profile.bindings.purpose')}
              endpoint="/v1.0/api/agent-profiles/{id}/bindings"
            />
          ) : bindingsError ? (
            <ErrorPresenter error={bindingsError} onRetry={loadBindings} />
          ) : bindings === null ? (
            <LoadingState />
          ) : bindings.length === 0 ? (
            <EmptyState
              title={t('views.profile.bindings.emptyTitle')}
              body={t('views.profile.bindings.emptyBody')}
              action={
                <button
                  type="button"
                  className="button-secondary"
                  onClick={() => navigate(pathFor('resources'))}
                  {...tip(t('tips.views.profile.bindings.goResources'))}
                >
                  {t('views.profile.bindings.goResources')}
                </button>
              }
            />
          ) : (
            <div className="table-scroll">
              <table className="data-table">
                <thead>
                  <tr>
                    {['alias', 'resource', 'accessMode', 'approval', 'enabled'].map((c) => (
                      <th key={c} scope="col" {...tip(t(`tips.views.profile.bindings.columns.${c}`))}>
                        {t(`views.profile.bindings.columns.${c}`)}
                      </th>
                    ))}
                    <th scope="col" className="right" {...tip(t('tips.table.actionsColumn'))}>
                      {t('common.generic.actions')}
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {bindings.map((b) => (
                    <tr
                      key={b.id}
                      className="clickable"
                      onClick={(e) => !e.target.closest('[data-ignore-row-click], button, a, select, input') && setEditingBinding({ ...blankBinding(), ...b })}
                    >
                      <td>
                        <code>{b.alias}</code>
                      </td>
                      <td>{b.resourceName || resourceName(b.resourceId)}</td>
                      <td>
                        <EnumBadge group="accessMode" value={b.accessMode} tone={accessModeTone(b.accessMode)} />
                      </td>
                      <td>
                        {b.accessMode === 'ReadWriteWithApproval' || (b.accessMode === 'ReadWrite' && b.requireApprovalForDestructive !== false) ? (
                          <Badge tone="warning">{t('views.profile.bindings.approvalOn')}</Badge>
                        ) : (
                          '-'
                        )}
                      </td>
                      <td>
                        {b.enabled === false ? <Badge>{t('common.generic.disabled')}</Badge> : <Badge tone="success">{t('common.generic.enabled')}</Badge>}
                      </td>
                      <td className="actions-cell">
                        <RowActions
                          label={b.alias}
                          items={[
                            { label: t('common.actions.edit'), tip: t('tips.actions.edit'), onClick: () => setEditingBinding({ ...blankBinding(), ...b }) },
                            { label: t('views.profile.toolRules.open'), tip: t('tips.views.profile.toolRules.open'), onClick: () => setRulesFor(b) },
                            { label: t('common.actions.delete'), tip: t('tips.actions.delete'), danger: true, onClick: () => setDeleteBinding(b) }
                          ]}
                        />
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}

      {tab === 'instructions' && (
        <div className="panel" data-testid="profile-instructions">
          <ProfileDocuments
            apiClient={apiClient}
            profileId={recordId}
            onChanged={() => apiClient.getCompiledInstructions(recordId, { surface }).then(setInstructions).catch(setInstructionsError)}
          />
          <div className="panel-header">
            <p className="text-muted">{t('views.profile.instructions.body')}</p>
            <FormField
              id="instructions-surface"
              field={{
                name: 'surface',
                type: 'select',
                required: true,
                label: t('views.profile.instructions.surface'),
                tip: t('tips.views.profile.instructions.surface'),
                options: enumOptions(t, 'surface')
              }}
              value={surface}
              onChange={setSurface}
            />
          </div>
          {isMissingEndpoint(instructionsError) ? (
            <UnavailableState
              feature={t('views.profile.tabs.instructions')}
              purpose={t('views.profile.instructions.purpose')}
              endpoint="/v1.0/api/agent-profiles/{id}/compiled-instructions"
            />
          ) : instructionsError ? (
            <ErrorPresenter error={instructionsError} />
          ) : !instructions ? (
            <LoadingState />
          ) : (
            <CompiledInstructionsView compiled={instructions} />
          )}
        </div>
      )}

      {tab === 'tools' && (
        <div className="panel" data-testid="profile-tools">
          <p className="text-muted">{t('views.profile.tools.body')}</p>
          {catalog && (
            <div className="inline-meta" data-testid="profile-tools-meta">
              <Badge tone={catalog.hasModel ? 'success' : 'warning'} tip={t('tips.views.profile.tools.hasModel')}>
                {catalog.hasModel ? t('views.profile.tools.hasModel') : t('views.profile.tools.noModel')}
              </Badge>
              <span className="text-muted">{t('views.profile.tools.resourceCount', { count: catalog.resourceCount ?? 0 })}</span>
            </div>
          )}
          {isMissingEndpoint(toolsError) ? (
            <UnavailableState
              feature={t('views.profile.tabs.tools')}
              purpose={t('views.profile.tools.purpose')}
              endpoint="/v1.0/api/agent-profiles/{id}/tools"
            />
          ) : toolsError ? (
            <ErrorPresenter error={toolsError} />
          ) : tools === null ? (
            <LoadingState />
          ) : tools.length === 0 ? (
            <div className="text-muted">{t('views.profile.tools.empty')}</div>
          ) : (
            <div className="table-scroll">
              <table className="data-table">
                <thead>
                  <tr>
                    <th scope="col" {...tip(t('tips.views.profile.tools.name'))}>
                      {t('views.profile.tools.name')}
                    </th>
                    <th scope="col" {...tip(t('tips.views.profile.tools.effect'))}>
                      {t('views.profile.tools.effect')}
                    </th>
                    <th scope="col" {...tip(t('tips.views.profile.tools.resources'))}>
                      {t('views.profile.tools.resources')}
                    </th>
                    <th scope="col" {...tip(t('tips.views.profile.tools.description'))}>
                      {t('views.profile.tools.description')}
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {tools.map((tool) => (
                    <tr key={tool.name}>
                      <td>
                        <code>{tool.name}</code>
                        {tool.isMetaTool && (
                          <>
                            {' '}
                            <Badge tip={t('tips.views.profile.tools.meta')}>{t('views.profile.tools.meta')}</Badge>
                          </>
                        )}
                      </td>
                      <td>
                        <EffectBadge effect={tool.knownEffect || tool.baseEffect || tool.effect} />
                        {tool.classifiedPerCall && <span className="text-muted"> {t('views.profile.tools.perCall')}</span>}
                        <ToolFlags tool={tool} />
                      </td>
                      <td>
                        {(tool.resources || []).length === 0
                          ? '-'
                          : tool.resources.map((r) => (
                              <span key={r.alias} className="inline-meta">
                                <code>{r.alias}</code> <EnumBadge group="accessMode" value={r.accessMode} tone={accessModeTone(r.accessMode)} />
                                {r.rule && <EnumBadge group="toolRule" value={r.rule} />}
                              </span>
                            ))}
                      </td>
                      <td className="cell-wrap">{tool.description || '-'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}

      {tab === 'connect' && (
        <div className="connect-grid" data-testid="profile-connect">
          <ProfileChatLink profile={profile} onSaved={load} />
          <Snippet
            title={t('views.profile.connect.claudeCode')}
            body={t('views.profile.connect.claudeCodeBody')}
            value={mcpConfigSnippet({ serverUrl, profile: mcpProfile })}
            testId="snippet-mcp"
          />
          <Snippet
            title={t('views.profile.connect.claudeCli')}
            body={t('views.profile.connect.claudeCliBody')}
            value={claudeCliSnippet({ serverUrl, profile: mcpProfile })}
          />
          <Snippet
            title={t('views.profile.connect.stdio')}
            body={t('views.profile.connect.stdioBody')}
            value={stdioSnippet({ serverUrl, profile: mcpProfile })}
          />
          <section className="panel snippet">
            <h3 className="section-title">{t('views.profile.connect.a2a')}</h3>
            <p className="field-hint">
              {(profile.exposeA2A ?? profile.exposeA2a) === false ? t('views.profile.connect.a2aOff') : t('views.profile.connect.a2aBody')}
            </p>
            <div className="secret-row">
              <code>{a2aCardUrl({ serverUrl, tenantId, profile: profile.name })}</code>
              <CopyButton value={a2aCardUrl({ serverUrl, tenantId, profile: profile.name })} tipText={t('tips.views.profile.connect.copyCard')} />
            </div>
            {card && <CodeBlock value={card} prettyJson />}
          </section>
          <section className="panel snippet">
            <h3 className="section-title">{t('views.profile.connect.openai')}</h3>
            <p className="field-hint">
              {(profile.exposeOpenAI ?? profile.exposeOpenai) === false
                ? t('views.profile.connect.openaiOff')
                : t('views.profile.connect.openaiBody', { model: profile.name })}
            </p>
            <div className="secret-row">
              <code>{openAiBaseUrl({ serverUrl })}</code>
              <CopyButton value={openAiBaseUrl({ serverUrl })} tipText={t('tips.views.profile.connect.copyOpenai')} />
            </div>
          </section>
          <section className="panel snippet">
            <h3 className="section-title">{t('views.profile.connect.credential')}</h3>
            <p className="field-hint">{t('views.profile.connect.credentialBody')}</p>
            <button
              type="button"
              className="button-primary"
              onClick={() => navigate(pathFor('credentials'))}
              {...tip(t('tips.views.profile.connect.credential'))}
            >
              {t('views.profile.connect.credentialAction')}
            </button>
          </section>
        </div>
      )}

      <Modal
        open={!!editingBinding}
        onClose={() => !busy && setEditingBinding(null)}
        size="large"
        title={t(editingBinding?.id ? 'views.profile.bindings.edit' : 'views.profile.bindings.add')}
        testId="binding-modal"
        footer={
          <>
            <button type="button" className="button-secondary" onClick={() => setEditingBinding(null)} disabled={busy} {...tip(t('tips.modal.cancelForm'))}>
              {t('common.actions.cancel')}
            </button>
            <button
              type="button"
              className="button-primary"
              onClick={saveBinding}
              disabled={busy}
              data-testid="binding-save"
              {...tip(t('tips.modal.saveCreate'))}
            >
              {t('common.actions.save')}
            </button>
          </>
        }
      >
        {editingBinding && (
          <>
            {resources.length === 0 && <div className="warning-banner">{t('views.profile.bindings.noResources')}</div>}
            <FormFields
              fields={bindingFields(t, resources, editingBinding)}
              data={editingBinding}
              errors={bindingErrors}
              idPrefix="binding"
              onChange={(next) => {
                // Default the alias to the resource's name when a resource is picked first.
                if (next.resourceId !== editingBinding.resourceId && !editingBinding.alias) next = { ...next, alias: resourceName(next.resourceId) };
                setEditingBinding(next);
              }}
            />
          </>
        )}
      </Modal>
      <ConfirmModal
        open={!!deleteBinding}
        danger
        title={t('views.profile.bindings.deleteTitle')}
        message={t('views.profile.bindings.deleteBody', { alias: deleteBinding?.alias })}
        confirmLabel={t('common.actions.delete')}
        confirmTip={t('tips.confirm.delete')}
        onConfirm={removeBinding}
        onCancel={() => setDeleteBinding(null)}
      />
      {rulesFor && <ToolRulesModal profileId={recordId} binding={rulesFor} tools={tools || []} onClose={() => setRulesFor(null)} />}
      <JsonViewerModal open={jsonOpen} value={profile} onClose={() => setJsonOpen(false)} />
    </div>
  );
}
