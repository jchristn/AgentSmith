import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import Modal from './Modal';
import ConfirmModal from './ConfirmModal';
import DiffViewer from './DiffViewer';
import MarkdownView from './MarkdownView';
import Tabs from './Tabs';
import { FormField } from './FormFields';
import { Badge, EnumBadge } from './StatusBadge';
import { LoadingState, ErrorPresenter, UnavailableState, EmptyState } from './States';
import { OutcomeView } from './TryToolModal';
import { tip } from './Tooltip';
import { useAuth } from '../context/AuthContext';
import { useToast } from '../context/ToastContext';
import { errorMessage, isMissingEndpoint } from '../utils/errors';
import { formatDateTime, formatDuration, formatNumber, formatPercent } from '../i18n/formatters';
import { knowledgeSourceTone, structuredResult, contextEntries, entryId, isSqlConnector, relationshipLayout, tableLabel } from '../utils/knowledge';

/** Run a read tool on the resource directly and return the parsed structured result (throws on a tool error). */
async function readTool(apiClient, resourceId, toolName, args) {
  const outcome = await apiClient.invokeResourceTool(resourceId, toolName, args);
  if (outcome?.result?.isError || outcome?.decision === 'Denied') {
    const err = new Error(outcome?.result?.text || outcome?.reason || toolName);
    err.code = outcome?.result?.errorCode || outcome?.decisionCode;
    throw err;
  }
  return { outcome, data: structuredResult(outcome) };
}

/** One knowledge entry: Markdown editor with preview, source badge, version, history with diff, save, and delete. */
function KnowledgeEntryEditor({ resourceId, entry, onSaved }) {
  const { t, i18n } = useTranslation();
  const { apiClient } = useAuth();
  const { addToast } = useToast();
  const [text, setText] = useState(entry.content || '');
  const [preview, setPreview] = useState(false);
  const [busy, setBusy] = useState(false);
  const [versions, setVersions] = useState(null);
  const [diff, setDiff] = useState(null);
  const [confirmDelete, setConfirmDelete] = useState(false);

  useEffect(() => {
    setText(entry.content || '');
    setVersions(null);
  }, [entry]);

  const loadVersions = () =>
    apiClient
      .listResourceKnowledgeVersions(resourceId, entry.scope, entry.key)
      .then((r) => setVersions(Array.isArray(r) ? r : []))
      .catch(() => setVersions([]));

  const save = async () => {
    setBusy(true);
    try {
      await apiClient.putResourceKnowledge(resourceId, entry.scope, entry.key, text);
      addToast({ type: 'success', message: t('views.knowledge.saved') });
      onSaved?.();
    } catch (e) {
      addToast({ type: 'error', message: errorMessage(t, e) });
    } finally {
      setBusy(false);
    }
  };

  const remove = async () => {
    setConfirmDelete(false);
    setBusy(true);
    try {
      await apiClient.deleteResourceKnowledge(resourceId, entry.scope, entry.key);
      addToast({ type: 'success', message: t('common.toasts.deleted') });
      onSaved?.();
    } catch (e) {
      addToast({ type: 'error', message: errorMessage(t, e) });
    } finally {
      setBusy(false);
    }
  };

  const title = entry.scope === 'database' ? t('views.knowledge.databaseContext') : entry.key ? entry.key : entry.scope;
  const editorId = `knowledge-${entryId(entry.scope, entry.key).replace(/[^A-Za-z0-9_-]/g, '-')}`;

  return (
    <section className="panel knowledge-entry" data-testid="knowledge-entry" data-scope={entry.scope} data-key={entry.key || ''}>
      <header className="panel-header">
        <h3 className="section-title">
          {title} <span className="text-muted">({entry.scope})</span>
        </h3>
        <div className="inline-meta">
          {entry.exists ? (
            <>
              <EnumBadge group="knowledgeSource" value={entry.source} tone={knowledgeSourceTone(entry.source)} />
              <span className="text-muted">v{entry.version}</span>
              {entry.updatedBy && <span className="text-muted">{t('views.knowledge.updatedBy', { name: entry.updatedBy })}</span>}
              {entry.lastUpdateUtc && <span className="text-muted">{formatDateTime(entry.lastUpdateUtc, i18n.language)}</span>}
            </>
          ) : (
            <Badge tone="warning">{t('views.knowledge.noContext')}</Badge>
          )}
        </div>
      </header>
      <div className="inline-meta">
        <button type="button" className="button-ghost button-sm" onClick={() => setPreview((p) => !p)} {...tip(t('tips.views.knowledge.preview'))}>
          {preview ? t('views.knowledge.edit') : t('views.knowledge.preview')}
        </button>
      </div>
      {preview ? (
        <MarkdownView text={text} emptyText={t('views.knowledge.empty')} />
      ) : (
        <FormField
          id={editorId}
          field={{ name: 'content', type: 'textarea', rows: 8, label: t('views.knowledge.content'), tip: t('tips.views.knowledge.content') }}
          value={text}
          onChange={setText}
        />
      )}
      <div className="panel-actions">
        {entry.exists && (
          <button
            type="button"
            className="button-ghost"
            onClick={() => (versions ? setVersions(null) : loadVersions())}
            data-testid="knowledge-history"
            {...tip(t('tips.views.knowledge.history'))}
          >
            {versions ? t('views.knowledge.hideHistory') : t('views.knowledge.history')}
          </button>
        )}
        {entry.exists && (
          <button type="button" className="button-danger" onClick={() => setConfirmDelete(true)} disabled={busy} {...tip(t('tips.views.knowledge.delete'))}>
            {t('common.actions.delete')}
          </button>
        )}
        <button
          type="button"
          className="button-primary"
          onClick={save}
          disabled={busy || !text.trim() || text === (entry.content || '')}
          data-testid="knowledge-save"
          {...tip(t('tips.views.knowledge.save'))}
        >
          {busy ? t('common.generic.saving') : t('common.actions.save')}
        </button>
      </div>
      {versions && (
        <div className="table-scroll" data-testid="knowledge-versions">
          {versions.length === 0 ? (
            <p className="text-muted">{t('views.templateEditor.noVersions')}</p>
          ) : (
            <table className="data-table">
              <thead>
                <tr>
                  <th scope="col" {...tip(t('tips.views.templateEditor.columns.version'))}>
                    {t('views.templateEditor.version')}
                  </th>
                  <th scope="col" {...tip(t('tips.views.knowledge.source'))}>
                    {t('views.knowledge.source')}
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
                    <td>
                      v{v.version} {v.deleted && <Badge tone="danger">{t('views.knowledge.deleted')}</Badge>}
                    </td>
                    <td>
                      <EnumBadge group="knowledgeSource" value={v.source} tone={knowledgeSourceTone(v.source)} />
                    </td>
                    <td className="cell-wrap">{v.updatedBy || '-'}</td>
                    <td>{formatDateTime(v.createdUtc, i18n.language)}</td>
                    <td className="actions-cell">
                      <button type="button" className="button-ghost button-sm" onClick={() => setDiff(v)} {...tip(t('tips.views.templateEditor.diff'))}>
                        {t('views.templateEditor.diff')}
                      </button>
                      <button type="button" className="button-ghost button-sm" onClick={() => setText(v.content || '')} {...tip(t('tips.layeredEditor.load'))}>
                        {t('components.layeredEditor.load')}
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      )}
      <Modal open={!!diff} size="xlarge" onClose={() => setDiff(null)} title={t('views.templateEditor.diffTitle', { version: diff?.version })}>
        {diff && <DiffViewer before={diff.content} after={text} beforeLabel={`v${diff.version}`} afterLabel={t('views.templateEditor.current')} />}
      </Modal>
      <ConfirmModal
        open={confirmDelete}
        danger
        title={t('views.knowledge.deleteTitle')}
        message={t('views.knowledge.deleteBody', { name: title })}
        confirmLabel={t('common.actions.delete')}
        confirmTip={t('tips.confirm.delete')}
        onConfirm={remove}
        onCancel={() => setConfirmDelete(false)}
      />
    </section>
  );
}

/** Database and table context (and any other scope): a list of entries and the editor for the selected one. */
function ContextEditor({ resource, isSql, reloadKey, onChanged }) {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [stored, setStored] = useState(null);
  const [error, setError] = useState(null);
  const [tablesWithoutContext, setTablesWithoutContext] = useState([]);
  const [tablesLoading, setTablesLoading] = useState(false);
  const [tablesError, setTablesError] = useState(null);
  const [selected, setSelected] = useState(null);
  const [newEntry, setNewEntry] = useState(null);
  const [extra, setExtra] = useState([]);

  const load = useCallback(() => {
    apiClient
      .listResourceKnowledge(resource.id)
      .then((r) => {
        setStored(Array.isArray(r) ? r : []);
        setError(null);
      })
      .catch((e) => {
        setStored([]);
        setError(e);
      });
  }, [apiClient, resource.id]);

  useEffect(load, [load, reloadKey]);

  const loadTables = async () => {
    setTablesLoading(true);
    setTablesError(null);
    try {
      const { data } = await readTool(apiClient, resource.id, 'sql_get_context', { listTables: true, includeEmpty: true, pageSize: 1000 });
      setTablesWithoutContext(data?.tablesWithoutContext || []);
    } catch (e) {
      setTablesError(e);
    } finally {
      setTablesLoading(false);
    }
  };

  const entries = useMemo(() => contextEntries(stored, { isSql, tablesWithoutContext, extra }), [stored, isSql, tablesWithoutContext, extra]);
  const current = newEntry || entries.find((e) => entryId(e.scope, e.key) === selected) || entries[0] || null;

  if (isMissingEndpoint(error))
    return (
      <UnavailableState
        feature={t('views.knowledge.title')}
        purpose={t('views.knowledge.purpose')}
        endpoint="/v1.0/api/resources/{id}/knowledge"
        onRetry={load}
      />
    );
  if (error) return <ErrorPresenter error={error} onRetry={load} />;
  if (stored === null) return <LoadingState />;

  return (
    <div className="knowledge-layout" data-testid="knowledge-context">
      <aside className="panel knowledge-list" aria-label={t('views.knowledge.entries')}>
        <h3 className="section-title">{t('views.knowledge.entries')}</h3>
        {entries.length === 0 && <p className="text-muted">{t('views.knowledge.noEntries')}</p>}
        <ul className="plain-list bare-list">
          {entries.map((e) => {
            const id = entryId(e.scope, e.key);
            const active = current && !newEntry && entryId(current.scope, current.key) === id;
            return (
              <li key={id}>
                <button
                  type="button"
                  className={`knowledge-item ${active ? 'active' : ''}`}
                  onClick={() => {
                    setNewEntry(null);
                    setSelected(id);
                  }}
                  data-testid="knowledge-item"
                  {...tip(t('tips.views.knowledge.select'))}
                >
                  <span className="knowledge-item-name">{e.scope === 'database' ? t('views.knowledge.databaseContext') : e.key || e.scope}</span>{' '}
                  {e.exists ? (
                    <EnumBadge group="knowledgeSource" value={e.source} tone={knowledgeSourceTone(e.source)} />
                  ) : (
                    <span className="text-muted">{t('views.knowledge.missing')}</span>
                  )}
                </button>
              </li>
            );
          })}
        </ul>
        <div className="panel-actions">
          {isSql && (
            <button
              type="button"
              className="button-ghost button-sm"
              onClick={loadTables}
              disabled={tablesLoading}
              data-testid="knowledge-load-tables"
              {...tip(t('tips.views.knowledge.loadTables'))}
            >
              {tablesLoading ? t('common.generic.loading') : t('views.knowledge.loadTables')}
            </button>
          )}
          <button
            type="button"
            className="button-secondary button-sm"
            onClick={() => setNewEntry({ scope: isSql ? 'table' : 'notes', key: '', exists: false, isNew: true })}
            {...tip(t('tips.views.knowledge.add'))}
          >
            {t('views.knowledge.add')}
          </button>
        </div>
        {tablesError && <div className="error-banner">{errorMessage(t, tablesError)}</div>}
      </aside>
      <div className="knowledge-editor">
        {newEntry ? (
          <NewEntryForm
            entry={newEntry}
            onChange={setNewEntry}
            onCancel={() => setNewEntry(null)}
            onCreate={(e) => {
              setNewEntry(null);
              setExtra((list) => [...list, e]);
              setSelected(entryId(e.scope, e.key));
            }}
          />
        ) : current ? (
          <KnowledgeEntryEditor
            resourceId={resource.id}
            entry={current}
            onSaved={() => {
              load();
              onChanged?.();
            }}
          />
        ) : (
          <EmptyState title={t('views.knowledge.emptyTitle')} body={t('views.knowledge.emptyBody')} />
        )}
      </div>
    </div>
  );
}

/** Name a new entry (scope and key) before editing it. */
function NewEntryForm({ entry, onChange, onCancel, onCreate }) {
  const { t } = useTranslation();
  const valid = /^[A-Za-z0-9_.-]{1,128}$/.test(entry.scope) && (entry.scope === 'database' || entry.key.trim().length > 0);
  return (
    <section className="panel" data-testid="knowledge-new">
      <h3 className="section-title">{t('views.knowledge.add')}</h3>
      <div className="form-grid">
        <FormField
          id="knowledge-new-scope"
          field={{ name: 'scope', type: 'text', required: true, label: t('views.knowledge.scope'), tip: t('tips.views.knowledge.scope') }}
          value={entry.scope}
          onChange={(v) => onChange({ ...entry, scope: v })}
        />
        <FormField
          id="knowledge-new-key"
          field={{ name: 'key', type: 'text', label: t('views.knowledge.key'), tip: t('tips.views.knowledge.key') }}
          value={entry.key}
          onChange={(v) => onChange({ ...entry, key: v })}
        />
      </div>
      <div className="panel-actions">
        <button type="button" className="button-secondary" onClick={onCancel} {...tip(t('tips.modal.cancelForm'))}>
          {t('common.actions.cancel')}
        </button>
        <button
          type="button"
          className="button-primary"
          disabled={!valid}
          onClick={() => onCreate({ scope: entry.scope.trim(), key: entry.key.trim() })}
          {...tip(t('tips.views.knowledge.create'))}
        >
          {t('common.actions.next')}
        </button>
      </div>
    </section>
  );
}

/** Domain intelligence from sql_get_intelligence: quality, entities, metrics, filters, freshness, ambiguities, gaps. */
export function IntelligenceView({ data }) {
  const { t, i18n } = useTranslation();
  const lng = i18n.language;
  if (!data) return null;
  const quality = data.contextQuality || {};
  const domain = data.domain || {};
  const list = (items) => (items && items.length ? items.join(', ') : '-');
  return (
    <div className="intelligence" data-testid="knowledge-intelligence">
      <div className="kpi-grid">
        <div className="panel kpi-lite" {...tip(t('tips.views.knowledge.quality'))}>
          <div className="text-muted">{t('views.knowledge.quality')}</div>
          <strong data-testid="quality-score">{quality.score != null ? formatNumber(quality.score, {}, lng) : '-'} / 100</strong>{' '}
          {quality.label && <Badge tone={quality.score >= 75 ? 'success' : quality.score >= 40 ? 'warning' : 'danger'}>{quality.label}</Badge>}
        </div>
        <div className="panel kpi-lite">
          <div className="text-muted">{t('views.knowledge.tablesWithContext')}</div>
          <strong>
            {formatNumber(quality.tablesWithContext ?? 0, {}, lng)} / {formatNumber(quality.totalTables ?? 0, {}, lng)}
          </strong>
        </div>
        <div className="panel kpi-lite">
          <div className="text-muted">{t('views.knowledge.relationshipCounts')}</div>
          <strong>
            {t('views.knowledge.declaredInferred', { declared: quality.declaredRelationships ?? 0, inferred: quality.inferredRelationships ?? 0 })}
          </strong>
        </div>
      </div>
      {domain.summary && <p>{domain.summary}</p>}
      {data.crawledUtc && <p className="field-hint">{t('views.knowledge.crawled', { time: formatDateTime(data.crawledUtc, lng) })}</p>}

      {(quality.signals || []).length > 0 && (
        <>
          <h3 className="section-title">{t('views.knowledge.gaps')}</h3>
          <ul className="plain-list bare-list" data-testid="quality-signals">
            {quality.signals.map((s) => (
              <li key={s.key}>
                <Badge tone={s.severity === 'warning' ? 'warning' : s.severity === 'error' ? 'danger' : 'neutral'}>
                  {t(`views.knowledge.severity.${s.severity}`, { defaultValue: s.severity })}
                </Badge>{' '}
                {s.message}
                {s.recommendation && <div className="field-hint">{s.recommendation}</div>}
              </li>
            ))}
          </ul>
        </>
      )}

      <h3 className="section-title">{t('views.knowledge.entities')}</h3>
      <div className="table-scroll">
        <table className="data-table" data-testid="intelligence-entities">
          <thead>
            <tr>
              {['table', 'role', 'rows', 'keyColumns', 'context'].map((c) => (
                <th key={c} scope="col" {...tip(t(`tips.views.knowledge.columns.${c}`))}>
                  {t(`views.knowledge.columns.${c}`)}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {(domain.entities || []).map((e) => (
              <tr key={`${e.schemaName}.${e.tableName}`}>
                <td>
                  <code>{tableLabel(e.schemaName, e.tableName, data.schema)}</code>
                </td>
                <td>{e.role || '-'}</td>
                <td className="right">{e.rowEstimate != null ? formatNumber(e.rowEstimate, {}, lng) : '-'}</td>
                <td className="cell-wrap">{list(e.keyColumns)}</td>
                <td>{e.hasContext ? <Badge tone="success">{t('common.boolean.yes')}</Badge> : <Badge>{t('common.boolean.no')}</Badge>}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div className="detail-sections">
        <dl className="kv-grid">
          <dt>{t('views.knowledge.metrics')}</dt>
          <dd>
            {(domain.metricDetails || []).length === 0
              ? '-'
              : domain.metricDetails.map((m) => (
                  <div key={m.column}>
                    <code>{m.column}</code> <span className="text-muted">{(m.aggregations || []).join(', ')}</span>
                  </div>
                ))}
          </dd>
          <dt>{t('views.knowledge.commonFilters')}</dt>
          <dd className="cell-wrap">{list(domain.commonFilters)}</dd>
          <dt>{t('views.knowledge.freshness')}</dt>
          <dd className="cell-wrap">{list(domain.freshnessColumns)}</dd>
          <dt>{t('views.knowledge.tenantColumns')}</dt>
          <dd className="cell-wrap">{list(domain.tenantColumns)}</dd>
          <dt>{t('views.knowledge.softDelete')}</dt>
          <dd className="cell-wrap">{list(domain.softDeleteColumns)}</dd>
        </dl>
      </div>

      {(data.ambiguities || []).length > 0 && (
        <>
          <h3 className="section-title">{t('views.knowledge.ambiguities')}</h3>
          <ul className="plain-list bare-list" data-testid="intelligence-ambiguities">
            {data.ambiguities.map((a) => (
              <li key={a.term}>
                <strong>{a.term}</strong>: {a.question}
                {(a.candidates || []).length > 0 && <div className="field-hint">{a.candidates.join('; ')}</div>}
              </li>
            ))}
          </ul>
        </>
      )}
      {(data.suggestedQuestions || []).length > 0 && (
        <>
          <h3 className="section-title">{t('views.knowledge.questions')}</h3>
          <ul className="plain-list bare-list">
            {data.suggestedQuestions.map((q) => (
              <li key={q}>{q}</li>
            ))}
          </ul>
        </>
      )}
    </div>
  );
}

/** The relationship graph: tables on a circle, declared edges solid, inferred edges dashed with their confidence. */
export function RelationshipGraph({ relationships, defaultSchema }) {
  const { t, i18n } = useTranslation();
  const width = 640;
  const height = 360;
  const { nodes, edges } = useMemo(() => relationshipLayout(relationships, { width, height, defaultSchema }), [relationships, defaultSchema]);
  const at = (id) => nodes.find((n) => n.id === id);
  if (!nodes.length) return null;
  return (
    <svg
      className="relationship-graph"
      viewBox={`0 0 ${width} ${height}`}
      role="img"
      aria-label={t('views.knowledge.graphLabel', { tables: nodes.length, edges: edges.length })}
      data-testid="relationship-graph"
    >
      <defs>
        <marker id="rel-arrow" viewBox="0 0 10 10" refX="10" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
          <path d="M 0 0 L 10 5 L 0 10 z" className="rel-arrow" />
        </marker>
      </defs>
      {edges.map((e, i) => {
        const a = at(e.from);
        const b = at(e.to);
        if (!a || !b) return null;
        const mx = (a.x + b.x) / 2;
        const my = (a.y + b.y) / 2;
        return (
          <g key={i} className={`rel-edge ${e.declared ? 'declared' : 'inferred'}`} data-declared={e.declared ? 'true' : 'false'}>
            <line x1={a.x} y1={a.y} x2={b.x} y2={b.y} markerEnd="url(#rel-arrow)">
              <title>{e.label}</title>
            </line>
            {!e.declared && e.confidence != null && (
              <text x={mx} y={my - 4} textAnchor="middle" className="rel-confidence">
                {formatPercent(e.confidence, i18n.language)}
              </text>
            )}
          </g>
        );
      })}
      {nodes.map((n) => (
        <g key={n.id} className="rel-node">
          <circle cx={n.x} cy={n.y} r="6" />
          <text x={n.x} y={n.y + 20} textAnchor="middle">
            {n.label}
          </text>
        </g>
      ))}
    </svg>
  );
}

/** Relationships from sql_list_relationships: the graph plus a table of edges, declared and inferred. */
export function RelationshipsView({ data }) {
  const { t, i18n } = useTranslation();
  const items = data?.items || data?.relationships || [];
  if (!items.length) return <EmptyState title={t('views.knowledge.noRelationshipsTitle')} body={t('views.knowledge.noRelationshipsBody')} />;
  return (
    <div data-testid="knowledge-relationships">
      <div className="panel">
        <RelationshipGraph relationships={items} defaultSchema={data?.schema} />
        <div className="rel-legend text-muted">
          <span className="rel-legend-declared">{t('views.knowledge.declared')}</span>{' '}
          <span className="rel-legend-inferred">{t('views.knowledge.inferred')}</span>
        </div>
      </div>
      <div className="table-scroll">
        <table className="data-table" data-testid="relationship-table">
          <thead>
            <tr>
              {['from', 'to', 'kind', 'confidence', 'evidence'].map((c) => (
                <th key={c} scope="col" {...tip(t(`tips.views.knowledge.columns.${c}`))}>
                  {t(`views.knowledge.columns.${c}`)}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {items.map((r, i) => (
              <tr key={i} data-declared={r.isDeclared ? 'true' : 'false'}>
                <td>
                  <code>
                    {tableLabel(r.fromSchema, r.fromTable, data?.schema)}.{r.fromColumn}
                  </code>
                </td>
                <td>
                  <code>
                    {tableLabel(r.toSchema, r.toTable, data?.schema)}.{r.toColumn}
                  </code>
                </td>
                <td>
                  {r.isDeclared ? (
                    <Badge tone="success" tip={t('tips.views.knowledge.declared')}>
                      {t('views.knowledge.declared')}
                    </Badge>
                  ) : (
                    <Badge tone="warning" tip={t('tips.views.knowledge.inferred')}>
                      {t('views.knowledge.inferred')}
                    </Badge>
                  )}
                  {r.constraintName && <span className="text-muted"> {r.constraintName}</span>}
                </td>
                <td className="right">{r.confidence != null ? formatPercent(r.confidence, i18n.language) : '-'}</td>
                <td className="cell-wrap">{(r.evidence || []).join(' ')}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

/** A panel that runs one read tool when first shown, with Refresh (recrawl). */
function ToolBackedPanel({ resource, toolName, render, testId }) {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [state, setState] = useState({ loading: true, data: null, error: null });

  const run = useCallback(
    (refresh) => {
      setState((s) => ({ ...s, loading: true, error: null }));
      readTool(apiClient, resource.id, toolName, refresh ? { refresh: true } : {})
        .then(({ data }) => setState({ loading: false, data, error: null }))
        .catch((error) => setState({ loading: false, data: null, error }));
    },
    [apiClient, resource.id, toolName]
  );

  useEffect(() => run(false), [run]);

  let body;
  if (state.loading) body = <LoadingState />;
  else if (state.error) body = <ErrorPresenter error={state.error} onRetry={() => run(false)} />;
  else body = render(state.data);

  return (
    <div data-testid={testId}>
      <div className="panel-actions">
        <span className="text-muted">{t('views.knowledge.viaTool', { tool: toolName })}</span>
        <button type="button" className="button-ghost button-sm" onClick={() => run(true)} disabled={state.loading} {...tip(t('tips.views.knowledge.refresh'))}>
          {t('views.knowledge.refresh')}
        </button>
      </div>
      {body}
    </div>
  );
}

/**
 * Generate database and table context with sql_generate_context, run as a profile that binds the resource and has
 * an inference endpoint (the tool uses the profile's model). The call can take minutes; the modal shows elapsed time.
 */
function GenerateContextModal({ resource, onClose, onDone }) {
  const { t, i18n } = useTranslation();
  const { apiClient } = useAuth();
  const [candidates, setCandidates] = useState(null);
  const [choice, setChoice] = useState('');
  const [scope, setScope] = useState('all');
  const [overwrite, setOverwrite] = useState(false);
  const [instructions, setInstructions] = useState('');
  const [running, setRunning] = useState(false);
  const [elapsed, setElapsed] = useState(0);
  const [outcome, setOutcome] = useState(null);
  const [error, setError] = useState(null);
  const timer = useRef(null);

  useEffect(() => {
    let active = true;
    apiClient
      .getResourceReferrers(resource.id)
      .then(async (refs) => {
        const list = Array.isArray(refs) ? refs.filter((r) => r.enabled !== false) : [];
        const withModel = await Promise.all(
          list.map((r) =>
            apiClient
              .getProfileTools(r.agentProfileId)
              .then((c) => ({ ...r, hasModel: !!c?.hasModel }))
              .catch(() => ({ ...r, hasModel: false }))
          )
        );
        if (!active) return;
        setCandidates(withModel);
        const first = withModel.find((r) => r.hasModel);
        if (first) setChoice(first.bindingId);
      })
      .catch((e) => active && (setCandidates([]), setError(e)));
    return () => {
      active = false;
      clearInterval(timer.current);
    };
  }, [apiClient, resource.id]);

  const chosen = (candidates || []).find((c) => c.bindingId === choice);

  const run = async () => {
    if (!chosen) return;
    setRunning(true);
    setError(null);
    setOutcome(null);
    setElapsed(0);
    const started = Date.now();
    timer.current = setInterval(() => setElapsed(Date.now() - started), 1000);
    try {
      const args = { scope, overwrite };
      if (instructions.trim()) args.instructions = instructions.trim();
      const result = await apiClient.invokeTool({
        profileId: chosen.agentProfileId,
        resource: chosen.alias,
        toolName: 'sql_generate_context',
        arguments: args
      });
      setOutcome(result);
      if (result?.status === 'Succeeded') onDone?.();
    } catch (e) {
      setError(e);
    } finally {
      clearInterval(timer.current);
      setRunning(false);
    }
  };

  return (
    <Modal
      open
      size="large"
      onClose={() => !running && onClose()}
      title={t('views.knowledge.generateTitle', { name: resource.name })}
      testId="generate-context"
      footer={
        <>
          <button type="button" className="button-secondary" onClick={onClose} disabled={running} {...tip(t('tips.modal.close'))}>
            {t('common.actions.close')}
          </button>
          <button
            type="button"
            className="button-primary"
            onClick={run}
            disabled={running || !chosen?.hasModel}
            data-testid="generate-run"
            {...tip(t('tips.views.knowledge.generate'))}
          >
            {running ? t('views.knowledge.generating') : t('views.knowledge.generate')}
          </button>
        </>
      }
    >
      <p className="text-muted">{t('views.knowledge.generateBody')}</p>
      {candidates === null ? (
        <LoadingState />
      ) : candidates.length === 0 ? (
        <div className="warning-banner">{t('views.knowledge.generateNoBinding')}</div>
      ) : (
        <>
          <FormField
            id="generate-profile"
            field={{
              name: 'profile',
              type: 'select',
              required: true,
              label: t('views.resources.tryTool.profile'),
              tip: t('tips.views.knowledge.generateProfile'),
              options: candidates.map((c) => ({
                value: c.bindingId,
                label: `${c.agentProfileName || c.agentProfileId} / ${c.alias}${c.hasModel ? '' : ` (${t('views.knowledge.noModel')})`}`
              }))
            }}
            value={choice}
            onChange={setChoice}
          />
          {chosen && !chosen.hasModel && <div className="warning-banner">{t('views.knowledge.generateNoModel')}</div>}
          <div className="form-grid">
            <FormField
              id="generate-scope"
              field={{
                name: 'scope',
                type: 'select',
                required: true,
                label: t('views.knowledge.generateScope'),
                tip: t('tips.views.knowledge.generateScope'),
                options: ['all', 'database', 'tables'].map((v) => ({ value: v, label: t(`views.knowledge.generateScopes.${v}`) }))
              }}
              value={scope}
              onChange={setScope}
            />
            <FormField
              id="generate-overwrite"
              field={{ name: 'overwrite', type: 'checkbox', label: t('views.knowledge.overwrite'), tip: t('tips.views.knowledge.overwrite') }}
              value={overwrite}
              onChange={setOverwrite}
            />
          </div>
          <FormField
            id="generate-instructions"
            field={{
              name: 'instructions',
              type: 'textarea',
              rows: 3,
              label: t('views.knowledge.generateInstructions'),
              tip: t('tips.views.knowledge.generateInstructions')
            }}
            value={instructions}
            onChange={setInstructions}
          />
        </>
      )}
      {running && (
        <div className="info-banner" role="status" data-testid="generate-progress">
          <span className="spinner" aria-hidden="true" /> {t('views.knowledge.generateProgress', { elapsed: formatDuration(elapsed, i18n.language) })}
        </div>
      )}
      {error && <ErrorPresenter error={error} />}
      {outcome && <OutcomeView outcome={outcome} testId="generate-outcome" />}
    </Modal>
  );
}

const SQL_VIEWS = ['context', 'intelligence', 'relationships'];

/**
 * A resource's Knowledge tab (P8.53): database and table context with source badges and version history, and for SQL
 * resources Generate Context (sql_generate_context as a profile with a model), the intelligence view
 * (sql_get_intelligence), and relationships as a list and a graph (sql_list_relationships), declared against inferred
 * with confidence. Read tools run on the resource directly (Execute on Resource) and are recorded as invocations.
 */
export default function ResourceKnowledge({ resource, connector }) {
  const { t } = useTranslation();
  const sql = isSqlConnector(connector);
  const canGenerate = sql && (connector?.tools || []).some((x) => x.name === 'sql_generate_context');
  const [view, setView] = useState('context');
  const [generateOpen, setGenerateOpen] = useState(false);
  const [reloadKey, setReloadKey] = useState(0);

  return (
    <div className="resource-knowledge" data-testid="resource-knowledge">
      <div className="panel-header">
        <p className="text-muted">{t(sql ? 'views.knowledge.sqlBody' : 'views.knowledge.body')}</p>
        {canGenerate && (
          <button
            type="button"
            className="button-primary"
            onClick={() => setGenerateOpen(true)}
            data-testid="generate-context-open"
            {...tip(t('tips.views.knowledge.generateOpen'))}
          >
            {t('views.knowledge.generateOpen')}
          </button>
        )}
      </div>
      {sql && (
        <Tabs
          label={t('views.knowledge.title')}
          active={view}
          onChange={setView}
          tabs={SQL_VIEWS.map((id) => ({ id, label: t(`views.knowledge.views.${id}`), tip: t(`tips.views.knowledge.views.${id}`) }))}
        />
      )}
      {view === 'context' && <ContextEditor resource={resource} isSql={sql} reloadKey={reloadKey} />}
      {sql && view === 'intelligence' && (
        <ToolBackedPanel resource={resource} toolName="sql_get_intelligence" testId="intelligence-panel" render={(data) => <IntelligenceView data={data} />} />
      )}
      {sql && view === 'relationships' && (
        <ToolBackedPanel
          resource={resource}
          toolName="sql_list_relationships"
          testId="relationships-panel"
          render={(data) => <RelationshipsView data={data} />}
        />
      )}
      {generateOpen && <GenerateContextModal resource={resource} onClose={() => setGenerateOpen(false)} onDone={() => setReloadKey((k) => k + 1)} />}
    </div>
  );
}
