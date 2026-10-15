import { useCallback, useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import PromptEditor from './PromptEditor';
import DiffViewer from './DiffViewer';
import Modal from './Modal';
import ConfirmModal from './ConfirmModal';
import Tabs from './Tabs';
import CodeBlock from './CodeBlock';
import { Badge, EnumBadge } from './StatusBadge';
import { LoadingState, ErrorPresenter, UnavailableState } from './States';
import { tip } from './Tooltip';
import { useAuth } from '../context/AuthContext';
import { useToast } from '../context/ToastContext';
import { errorMessage, isMissingEndpoint } from '../utils/errors';
import { formatDateTime } from '../i18n/formatters';

/** Editable layers, top first (plan section 10.2). Built-in ships with the server and is read-only. */
export const EDITABLE_LAYERS = ['Tenant', 'Global'];

/** The state of one layer from a template view: { exists, content, version, customized, defaultUpdatedSinceCustomized }. */
export function layerState(view, layer) {
  if (!view) return null;
  if (layer === 'Tenant') return view.tenant || null;
  if (layer === 'Global') return view.global || null;
  return { exists: true, content: view.builtInContent ?? '', version: 0 };
}

/** The text a layer falls back to: Tenant falls back to Global (when it exists), Global to Built-in. */
export function contentBelow(view, layer) {
  if (!view) return '';
  if (layer === 'Tenant' && view.global?.exists) return view.global.content ?? '';
  return view.builtInContent ?? '';
}

/**
 * The customized layer whose default changed underneath it: Global when the shipped Built-in changed since it was
 * customized, else Tenant when the Global default changed since the tenant override was saved. Null when current.
 */
export function outdatedLayerOf(view) {
  if (!view) return null;
  if (view.global?.exists && view.global.customized && view.global.defaultUpdatedSinceCustomized) return 'Global';
  if (view.tenant?.exists && view.tenant.defaultUpdatedSinceCustomized) return 'Tenant';
  return null;
}

/** What a layer's editor starts with: its own content when it exists, else the text it would override. */
export function initialDraft(view, layer) {
  const state = layerState(view, layer);
  return state?.exists ? (state.content ?? '') : contentBelow(view, layer);
}

/**
 * One prompt template key across its layers (P8.33, P8.35): the layer in effect, a tab per layer (Tenant, Global,
 * Built-in), the PromptEditor with server preview for the selected layer, save with a change note, Reset to Default
 * per layer (Tenant resets to Global, Global to the Built-in), the version history of the layer with a diff against
 * the editor and Load, the server's diff of the layer against the one below, and the "built-in updated since you
 * customized this" notice with a one-click adopt. Global is editable by system administrators only.
 */
export default function LayeredTemplateEditor({ templateKey, onChanged, testId = 'layered-editor' }) {
  const { t, i18n } = useTranslation();
  const { apiClient, principal } = useAuth();
  const { addToast } = useToast();
  const [view, setView] = useState(null);
  const [error, setError] = useState(null);
  const [layer, setLayer] = useState('Tenant');
  const [draft, setDraft] = useState('');
  const [note, setNote] = useState('');
  const [versions, setVersions] = useState([]);
  const [busy, setBusy] = useState(false);
  const [confirmReset, setConfirmReset] = useState(false);
  const [diffVersion, setDiffVersion] = useState(null);
  const [serverDiff, setServerDiff] = useState(null);
  const [noticeDiff, setNoticeDiff] = useState(false);
  const canEditGlobal = !!principal?.isAdmin;
  const editable = layer === 'Tenant' || (layer === 'Global' && canEditGlobal);

  const applyView = useCallback((next, selected) => {
    setView(next);
    setDraft(initialDraft(next, selected));
    setNote('');
  }, []);

  const load = useCallback(() => {
    if (!apiClient || !templateKey) return;
    setError(null);
    apiClient
      .getPromptLayers(templateKey)
      .then((v) => applyView(v, layer))
      .catch(setError);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [apiClient, templateKey, applyView]);

  useEffect(load, [load]);

  const loadVersions = useCallback(() => {
    if (!apiClient || !templateKey || layer === 'BuiltIn') {
      setVersions([]);
      return;
    }
    apiClient
      .listPromptLayerVersions(templateKey, layer)
      .then((r) => setVersions(Array.isArray(r) ? r : r?.items || r?.objects || []))
      .catch(() => setVersions([]));
  }, [apiClient, templateKey, layer]);

  useEffect(loadVersions, [loadVersions, view]);

  const selectLayer = (next) => {
    setLayer(next);
    setDraft(initialDraft(view, next));
    setNote('');
  };

  const renderPreview = useCallback((content, context) => apiClient.renderPromptContent(content, context), [apiClient]);

  const after = (next, message) => {
    applyView(next, layer);
    addToast({ type: 'success', message });
    onChanged?.(next);
  };

  const save = async () => {
    setBusy(true);
    try {
      const next = await apiClient.savePromptLayer(templateKey, layer, draft, note);
      after(next, t('components.layeredEditor.saved', { layer: t(`enums.promptLayer.${layer}`) }));
    } catch (e) {
      addToast({ type: 'error', message: errorMessage(t, e) });
    } finally {
      setBusy(false);
    }
  };

  const reset = async () => {
    setConfirmReset(false);
    setBusy(true);
    try {
      const next = await apiClient.resetPromptLayer(templateKey, layer);
      after(next, t('components.layeredEditor.resetDone', { layer: t(`enums.promptLayer.${layer}`) }));
    } catch (e) {
      addToast({ type: 'error', message: errorMessage(t, e) });
    } finally {
      setBusy(false);
    }
  };

  const showServerDiff = async () => {
    try {
      const lines = await apiClient.getPromptLayerDiff(templateKey, layer);
      setServerDiff(Array.isArray(lines) ? lines : []);
    } catch (e) {
      addToast({ type: 'error', message: errorMessage(t, e) });
    }
  };

  const state = layerState(view, layer);
  const below = contentBelow(view, layer);
  const belowLabel = layer === 'Tenant' && view?.global?.exists ? t('enums.promptLayer.Global') : t('enums.promptLayer.BuiltIn');
  const dirty = draft !== initialDraft(view, layer);
  const outdatedLayer = outdatedLayerOf(view);

  const tabs = useMemo(
    () =>
      ['Tenant', 'Global', 'BuiltIn'].map((id) => ({
        id,
        label: `${t(`enums.promptLayer.${id}`)}${view && layerState(view, id)?.customized ? ` (${t('components.layeredEditor.customized')})` : ''}`,
        tip: t(`tips.layeredEditor.layers.${id}`)
      })),
    [t, view]
  );

  if (isMissingEndpoint(error))
    return (
      <UnavailableState
        feature={t('components.layeredEditor.title')}
        purpose={t('components.layeredEditor.purpose')}
        endpoint="/v1.0/api/prompt-templates/{key}"
        onRetry={load}
      />
    );
  if (error) return <ErrorPresenter error={error} onRetry={load} />;
  if (!view) return <LoadingState />;

  return (
    <div className="layered-editor" data-testid={testId}>
      <div className="panel">
        <div className="inline-meta">
          <code>{view.key}</code>
          <span className="text-muted">{t('components.layeredEditor.inEffect')}</span>
          <EnumBadge group="promptLayer" value={view.activeLayer} tone={view.activeLayer === 'BuiltIn' ? 'neutral' : 'info'} />
          {view.source && <Badge tip={t('tips.layeredEditor.source')}>{view.source}</Badge>}
        </div>
        {view.description && <p className="text-muted">{view.description}</p>}
      </div>

      {outdatedLayer && (
        <div className="warning-banner" role="status" data-testid="builtin-updated">
          <span>{t(`components.layeredEditor.outdated.${outdatedLayer}`)}</span>{' '}
          <button type="button" className="button-secondary button-sm" onClick={() => setNoticeDiff(true)} {...tip(t('tips.layeredEditor.showChange'))}>
            {t('components.layeredEditor.showChange')}
          </button>{' '}
          {(outdatedLayer === 'Tenant' || canEditGlobal) && (
            <button
              type="button"
              className="button-primary button-sm"
              onClick={() => {
                selectLayer(outdatedLayer);
                setConfirmReset(true);
              }}
              data-testid="adopt-default"
              {...tip(t('tips.layeredEditor.adopt'))}
            >
              {t('components.layeredEditor.adopt')}
            </button>
          )}
        </div>
      )}

      <Tabs label={t('components.layeredEditor.layers')} active={layer} onChange={selectLayer} tabs={tabs} />

      {layer === 'BuiltIn' ? (
        <div className="panel" data-testid="layer-builtin">
          <p className="text-muted">{t('components.layeredEditor.builtInBody')}</p>
          <CodeBlock value={view.builtInContent || ''} emptyText={t('components.layeredEditor.noBuiltIn')} />
        </div>
      ) : (
        <>
          <div className="panel layer-status" data-testid={`layer-${layer}`} data-exists={state?.exists ? 'true' : 'false'}>
            {state?.exists ? (
              <span>
                {t('components.layeredEditor.layerVersion', { version: state.version })}{' '}
                {state.lastUpdateUtc && <span className="text-muted">{formatDateTime(state.lastUpdateUtc, i18n.language)}</span>}
              </span>
            ) : (
              <span className="text-muted">{t(`components.layeredEditor.notCustomized.${layer}`)}</span>
            )}
            {!editable && <div className="info-banner">{t('components.layeredEditor.globalReadOnly')}</div>}
          </div>
          <PromptEditor
            id={`layer-${layer}-content`}
            value={draft}
            readOnly={!editable}
            onChange={setDraft}
            render={renderPreview}
            baseline={{ label: belowLabel, text: below }}
            rows={16}
          />
          {editable && (
            <div className="panel layer-actions">
              <div className="field">
                <label htmlFor={`layer-${layer}-note`} {...tip(t('tips.views.templateEditor.changeNote'))}>
                  {t('views.templateEditor.changeNote')}
                </label>
                <input id={`layer-${layer}-note`} value={note} onChange={(e) => setNote(e.target.value)} {...tip(t('tips.views.templateEditor.changeNote'))} />
              </div>
              <div className="panel-actions">
                {state?.exists && (
                  <button type="button" className="button-ghost" onClick={showServerDiff} data-testid="layer-diff" {...tip(t('tips.layeredEditor.diffBelow'))}>
                    {t('components.layeredEditor.diffBelow', { layer: belowLabel })}
                  </button>
                )}
                {state?.exists && (
                  <button
                    type="button"
                    className="button-secondary"
                    onClick={() => setConfirmReset(true)}
                    disabled={busy}
                    data-testid="layer-reset"
                    {...tip(t(`tips.layeredEditor.reset.${layer}`))}
                  >
                    {t('views.templateEditor.reset')}
                  </button>
                )}
                <button
                  type="button"
                  className="button-primary"
                  onClick={save}
                  disabled={busy || (!dirty && state?.exists)}
                  data-testid="layer-save"
                  {...tip(t(`tips.layeredEditor.save.${layer}`))}
                >
                  {busy ? t('common.generic.saving') : t('components.layeredEditor.save', { layer: t(`enums.promptLayer.${layer}`) })}
                </button>
              </div>
            </div>
          )}

          <h3 className="section-title">{t('views.templateEditor.versions', { count: versions.length })}</h3>
          <div className="panel" data-testid="layer-versions">
            {versions.length === 0 ? (
              <div className="text-muted">{t('views.templateEditor.noVersions')}</div>
            ) : (
              <div className="table-scroll">
                <table className="data-table">
                  <thead>
                    <tr>
                      <th scope="col" {...tip(t('tips.views.templateEditor.columns.version'))}>
                        {t('views.templateEditor.version')}
                      </th>
                      <th scope="col" {...tip(t('tips.views.templateEditor.columns.note'))}>
                        {t('views.templateEditor.note')}
                      </th>
                      <th scope="col" {...tip(t('tips.views.templateEditor.columns.author'))}>
                        {t('views.templateEditor.author')}
                      </th>
                      <th scope="col" {...tip(t('tips.views.templateEditor.columns.saved'))}>
                        {t('resources.promptTemplates.columns.lastUpdateUtc')}
                      </th>
                      <th scope="col" className="right" {...tip(t('tips.table.actionsColumn'))}>
                        {t('common.generic.actions')}
                      </th>
                    </tr>
                  </thead>
                  <tbody>
                    {versions.map((v) => (
                      <tr key={v.id || v.version}>
                        <td>v{v.version}</td>
                        <td className="cell-wrap">{v.changeNote || '-'}</td>
                        <td>{v.createdBy || '-'}</td>
                        <td>{formatDateTime(v.createdUtc, i18n.language)}</td>
                        <td className="actions-cell">
                          <button
                            type="button"
                            className="button-ghost button-sm"
                            onClick={() => setDiffVersion(v)}
                            {...tip(t('tips.views.templateEditor.diff'))}
                          >
                            {t('views.templateEditor.diff')}
                          </button>
                          {editable && (
                            <button
                              type="button"
                              className="button-ghost button-sm"
                              onClick={() => {
                                setDraft(v.content || '');
                                setNote(t('components.layeredEditor.restoreNote', { version: v.version }));
                              }}
                              {...tip(t('tips.layeredEditor.load'))}
                            >
                              {t('components.layeredEditor.load')}
                            </button>
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        </>
      )}

      <Modal
        open={!!diffVersion}
        size="xlarge"
        onClose={() => setDiffVersion(null)}
        title={t('views.templateEditor.diffTitle', { version: diffVersion?.version })}
      >
        {diffVersion && (
          <DiffViewer before={diffVersion.content} after={draft} beforeLabel={`v${diffVersion.version}`} afterLabel={t('views.templateEditor.current')} />
        )}
      </Modal>
      <Modal
        open={!!serverDiff}
        size="xlarge"
        onClose={() => setServerDiff(null)}
        title={t('components.layeredEditor.diffBelow', { layer: belowLabel })}
        testId="server-diff"
      >
        {serverDiff && <ServerDiff lines={serverDiff} beforeLabel={belowLabel} afterLabel={t(`enums.promptLayer.${layer}`)} />}
      </Modal>
      <Modal open={noticeDiff} size="xlarge" onClose={() => setNoticeDiff(false)} title={t('components.layeredEditor.showChange')}>
        {noticeDiff && outdatedLayer && (
          <DiffViewer
            before={layerState(view, outdatedLayer)?.content || ''}
            after={contentBelow(view, outdatedLayer)}
            beforeLabel={t(`enums.promptLayer.${outdatedLayer}`)}
            afterLabel={outdatedLayer === 'Tenant' && view.global?.exists ? t('enums.promptLayer.Global') : t('enums.promptLayer.BuiltIn')}
          />
        )}
      </Modal>
      <ConfirmModal
        open={confirmReset}
        danger
        title={t('components.layeredEditor.resetTitle', { layer: t(`enums.promptLayer.${layer}`) })}
        message={t(`components.layeredEditor.resetBody.${layer === 'Global' ? 'Global' : 'Tenant'}`)}
        confirmLabel={t('views.templateEditor.reset')}
        confirmTip={t(`tips.layeredEditor.reset.${layer === 'Global' ? 'Global' : 'Tenant'}`)}
        onConfirm={reset}
        onCancel={() => setConfirmReset(false)}
      />
    </div>
  );
}

/** The server's line diff ([{ op: same|add|remove, text }]) in the DiffViewer's styling. */
export function ServerDiff({ lines, beforeLabel, afterLabel }) {
  const { t } = useTranslation();
  const changed = lines.some((l) => l.op !== 'same');
  return (
    <div className="diff-viewer" data-testid="server-diff-lines">
      <div className="diff-legend">
        <span className="diff-del">- {beforeLabel}</span>
        <span className="diff-add">+ {afterLabel}</span>
      </div>
      {!changed && <p className="text-muted">{t('components.layeredEditor.noDifferences')}</p>}
      <pre className="diff-body monospace">
        {lines.map((l, i) => {
          const type = l.op === 'add' ? 'add' : l.op === 'remove' || l.op === 'del' ? 'del' : 'same';
          return (
            <div key={i} className={`diff-line diff-${type}`}>
              <span className="diff-gutter" aria-hidden="true">
                {type === 'add' ? '+' : type === 'del' ? '-' : ' '}
              </span>
              {l.text || ' '}
            </div>
          );
        })}
      </pre>
    </div>
  );
}
