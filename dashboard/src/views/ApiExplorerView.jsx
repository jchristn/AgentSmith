import { useState, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import PageHeader from '../components/PageHeader';
import CopyButton from '../components/CopyButton';
import CodeBlock from '../components/CodeBlock';
import ConfirmModal from '../components/ConfirmModal';
import { MethodPill, StatusBadge } from '../components/StatusBadge';
import { LoadingState, EmptyState } from '../components/States';
import useApiExplorer from '../hooks/useApiExplorer';
import { isDestructive, buildCodeSnippets } from '../utils/openApi';
import { formatDuration, formatBytes, truncate } from '../i18n/formatters';
import { tip } from '../components/Tooltip';

function ParamInputs({ params, location, values, onChange, requiredLabel }) {
  const { t } = useTranslation();
  const list = params.filter((p) => p.in === location);
  if (list.length === 0) return null;
  return (
    <div className="modal-form">
      {list.map((p) => (
        <div className="field" key={p.name}>
          <label {...tip(t(`tips.views.explorer.${location}Param`, { name: p.name, type: p.schema?.type || 'string', description: p.description || '' }).trim())}>
            {p.name}
            {p.required ? ` (${requiredLabel})` : ''}
          </label>
          <input
            {...tip(t(`tips.views.explorer.${location}Param`, { name: p.name, type: p.schema?.type || 'string', description: p.description || '' }).trim())}
            value={values[p.name] ?? ''}
            placeholder={p.schema?.type || 'string'}
            onChange={(e) => onChange({ ...values, [p.name]: e.target.value })}
          />
        </div>
      ))}
    </div>
  );
}

export default function ApiExplorerView({ apiClient }) {
  const { t, i18n } = useTranslation();
  const ex = useApiExplorer(apiClient);
  const [snippetTab, setSnippetTab] = useState('curl');
  const [confirmOpen, setConfirmOpen] = useState(false);

  const snippets = useMemo(() => {
    if (!ex.operation) return { curl: '', fetch: '', csharp: '' };
    const hasBody = !['GET', 'HEAD'].includes(ex.operation.method) && ex.body;
    return buildCodeSnippets({
      method: ex.operation.method,
      url: ex.resolvedUrl,
      headers: ex.headers,
      body: hasBody ? ex.body : ''
    });
  }, [ex.operation, ex.resolvedUrl, ex.headers, ex.body]);

  const requestExecution = () => {
    if (ex.operation && isDestructive(ex.operation)) setConfirmOpen(true);
    else ex.execute();
  };

  const bodyAllowed = ex.operation && !['GET', 'HEAD'].includes(ex.operation.method);

  return (
    <div>
      <PageHeader title={t('views.explorer.title')} subtitle={t('views.explorer.subtitle')} />

      {ex.loadingSpec ? (
        <LoadingState />
      ) : ex.specError ? (
        <div className="panel">
          <EmptyState title={t('views.explorer.noSpecTitle')} body={t('views.explorer.noSpecBody')} />
        </div>
      ) : (
        <div className="explorer-stack">
          {/* Full-width operation picker, grouped by OpenAPI tag */}
          <div className="explorer-panel explorer-op-picker">
            <label className="field" {...tip(t('tips.views.explorer.operations'))}>
              <span className="explorer-op-picker-label">{t('views.explorer.operations')}</span>
              <select
                className="explorer-op-select"
                value={ex.operationId || ''}
                onChange={(e) => ex.setOperationId(e.target.value || null)}
                aria-label={t('views.explorer.operations')}
                {...tip(t('tips.views.explorer.operations'))}
              >
                <option value="">{t('views.explorer.selectOperationOption')}</option>
                {Object.entries(ex.grouped).map(([tag, ops]) => (
                  <optgroup key={tag} label={tag}>
                    {ops.map((op) => (
                      <option key={op.id} value={op.id}>
                        {op.method} {op.path}
                        {op.summary ? `: ${op.summary}` : ''}
                      </option>
                    ))}
                  </optgroup>
                ))}
              </select>
            </label>
          </div>

          {/* Request builder + response */}
          <div className="explorer-detail">
            {!ex.operation ? (
              <div className="explorer-panel">
                <EmptyState title={t('views.explorer.title')} body={t('views.explorer.selectOperation')} />
              </div>
            ) : (
              <>
                <div className="explorer-panel">
                  <div className="flex items-center gap-sm" style={{ marginBottom: 'var(--spacing-sm)' }}>
                    <MethodPill method={ex.operation.method} />
                    <code className="monospace">{ex.operation.path}</code>
                  </div>
                  {ex.operation.summary && <p className="text-muted" style={{ fontSize: 'var(--font-size-sm)' }}>{ex.operation.summary}</p>}

                  <h3 style={{ marginTop: 'var(--spacing-md)' }}>{t('views.explorer.resolvedUrl')}</h3>
                  <div className="explorer-url">
                    <span style={{ flex: 1 }}>{ex.resolvedUrl}</span>
                    <CopyButton value={ex.resolvedUrl} title={t('views.explorer.copyUrl')} tipText={t('tips.views.explorer.copyUrl')} />
                  </div>

                  {ex.operation.parameters.some((p) => p.in === 'path') && (
                    <>
                      <h3 style={{ marginTop: 'var(--spacing-md)' }}>{t('views.explorer.pathParams')}</h3>
                      <ParamInputs params={ex.operation.parameters} location="path" values={ex.pathParams} onChange={ex.setPathParams} requiredLabel={t('views.explorer.required')} />
                    </>
                  )}
                  {ex.operation.parameters.some((p) => p.in === 'query') && (
                    <>
                      <h3 style={{ marginTop: 'var(--spacing-md)' }}>{t('views.explorer.queryParams')}</h3>
                      <ParamInputs params={ex.operation.parameters} location="query" values={ex.queryParams} onChange={ex.setQueryParams} requiredLabel={t('views.explorer.required')} />
                    </>
                  )}

                  <h3 style={{ marginTop: 'var(--spacing-md)' }}>{t('views.explorer.headers')}</h3>
                  <div className="modal-form">
                    {Object.entries(ex.headers).map(([k, v]) => (
                      <div className="explorer-kv-row" key={k}>
                        <input value={k} readOnly aria-label={t('views.explorer.headerName')} {...tip(t('tips.views.explorer.headerName'))} />
                        <input value={v} aria-label={t('views.explorer.headerValue', { name: k })} {...tip(t('tips.views.explorer.headerValue'))} onChange={(e) => ex.setHeaders({ ...ex.headers, [k]: e.target.value })} />
                        <button type="button" className="button-ghost button-sm" aria-label={t('views.explorer.removeHeader', { name: k })} {...tip(t('tips.views.explorer.removeHeader'))} onClick={() => {
                          const next = { ...ex.headers };
                          delete next[k];
                          ex.setHeaders(next);
                        }}>×</button>
                      </div>
                    ))}
                    <button
                      type="button"
                      className="button-secondary button-sm"
                      onClick={() => ex.setHeaders({ ...ex.headers, '': '' })}
                      {...tip(t('tips.views.explorer.addHeader'))}
                    >
                      {t('views.explorer.addHeader')}
                    </button>
                  </div>

                  {bodyAllowed && (
                    <>
                      <h3 style={{ marginTop: 'var(--spacing-md)' }}>{t('views.explorer.body')}</h3>
                      <textarea rows={8} value={ex.body} aria-label={t('views.explorer.body')} {...tip(t('tips.views.explorer.body'))} onChange={(e) => ex.setBody(e.target.value)} />
                    </>
                  )}

                  <div style={{ marginTop: 'var(--spacing-md)' }}>
                    <button type="button" className="button-primary" onClick={requestExecution} disabled={ex.running} data-testid="explorer-execute" {...tip(t('tips.views.explorer.execute'))}>
                      {ex.running ? t('views.explorer.executing') : t('views.explorer.execute')}
                    </button>
                    {ex.running && (
                      <button type="button" className="button-secondary" onClick={ex.stop} {...tip(t('tips.views.explorer.stop'))}>
                        {t('views.explorer.stop')}
                      </button>
                    )}
                    {ex.streaming && <span className="pill pill-info">{t('views.explorer.streaming')}</span>}
                  </div>
                </div>

                {/* Response */}
                {ex.response && (
                  <div className="explorer-panel">
                    <h3>{t('views.explorer.response')}</h3>
                    <div className="flex items-center gap-md" style={{ marginBottom: 'var(--spacing-sm)' }}>
                      {typeof ex.response.status === 'number' ? <StatusBadge code={ex.response.status} /> : <span className="pill pill-danger">{ex.response.status}</span>}
                      <span className="text-muted">{formatDuration(ex.response.durationMs, i18n.language)}</span>
                      <span className="text-muted">{formatBytes(ex.response.size || 0)}</span>
                    </div>
                    <h3>{t('views.explorer.responseBody')}</h3>
                    <CodeBlock value={ex.response.body} prettyJson />
                    <h3 style={{ marginTop: 'var(--spacing-md)' }}>{t('views.explorer.responseHeaders')}</h3>
                    <CodeBlock value={ex.response.headers} prettyJson />
                  </div>
                )}

                {/* Code snippets */}
                <div className="explorer-panel">
                  <h3>{t('views.explorer.snippets')}</h3>
                  <div className="snippet-tabs">
                    {['curl', 'fetch', 'csharp'].map((s) => (
                      <button key={s} type="button" className={`snippet-tab ${snippetTab === s ? 'active' : ''}`} onClick={() => setSnippetTab(s)} {...tip(t(`tips.views.explorer.snippets.${s}`))}>
                        {s === 'csharp' ? 'C#' : s}
                      </button>
                    ))}
                  </div>
                  <CodeBlock value={snippets[snippetTab]} />
                </div>

                {/* History */}
                <div className="explorer-panel">
                  <div className="flex items-center justify-between" style={{ marginBottom: 'var(--spacing-sm)' }}>
                    <h3 style={{ margin: 0 }}>{t('views.explorer.history')}</h3>
                    {ex.history.length > 0 && (
                      <button type="button" className="button-ghost button-sm" onClick={ex.clearHistory} {...tip(t('tips.views.explorer.clearHistory'))}>
                        {t('views.explorer.clearHistory')}
                      </button>
                    )}
                  </div>
                  {ex.history.length === 0 ? (
                    <p className="text-muted" style={{ fontSize: 'var(--font-size-sm)' }}>{t('views.explorer.historyEmpty')}</p>
                  ) : (
                    ex.history.map((h, i) => (
                      <div className="explorer-history-item" key={i}>
                        <span className="flex items-center gap-sm">
                          <MethodPill method={h.method} />
                          <code {...tip(h.path)}>{truncate(h.path, 32)}</code>
                          {typeof h.status === 'number' && <StatusBadge code={h.status} />}
                        </span>
                        <span className="flex items-center gap-sm">
                          <button type="button" className="button-ghost button-sm" onClick={() => ex.loadFromHistory(h)} {...tip(t('tips.views.explorer.loadHistory'))}>
                            {t('views.explorer.loadHistory')}
                          </button>
                          <button type="button" className="button-ghost button-sm" onClick={() => ex.deleteHistory(i)} {...tip(t('tips.views.explorer.deleteHistory'))}>
                            {t('views.explorer.deleteHistory')}
                          </button>
                        </span>
                      </div>
                    ))
                  )}
                </div>
              </>
            )}
          </div>
        </div>
      )}

      <ConfirmModal
        open={confirmOpen}
        danger
        title={t('views.explorer.confirmTitle')}
        message={t('views.explorer.confirmMessage', { method: ex.operation?.method })}
        confirmLabel={t('views.explorer.execute')}
        confirmTip={t('tips.views.explorer.confirmExecute')}
        onConfirm={() => {
          setConfirmOpen(false);
          ex.execute();
        }}
        onCancel={() => setConfirmOpen(false)}
      />
    </div>
  );
}
