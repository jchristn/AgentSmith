import { useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import PageHeader from '../components/PageHeader';
import SchemaForm from '../components/SchemaForm';
import CodeBlock from '../components/CodeBlock';
import MarkdownView from '../components/MarkdownView';
import { UnavailableState, ErrorPresenter } from '../components/States';
import { FormField } from '../components/FormFields';
import { tip } from '../components/Tooltip';
import { PlayIcon, RefreshIcon } from '../components/Icons';
import { useAuth } from '../context/AuthContext';
import { APP_VERSION } from '../utils/constants';
import { ApiError } from '../utils/api';
import { rpcRequest, readRpcResponse, responseFor, groupToolsByFamily, jsonSchemaToDescriptors, argumentsFromValues } from '../utils/jsonRpc';
import { errorMessage } from '../utils/errors';

const PROTOCOL_VERSION = '2025-06-18';

/**
 * MCP Explorer (P8.41): connect to /mcp (or /mcp/{profile}) as the signed-in principal, see the compiled instructions
 * the server returns from initialize, list tools grouped by family, call one with a form generated from its input
 * schema, and read the raw JSON-RPC exchange.
 */
export default function McpExplorerView() {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [profiles, setProfiles] = useState([]);
  const [profile, setProfile] = useState('');
  const [session, setSession] = useState(null);
  const [init, setInit] = useState(null);
  const [tools, setTools] = useState([]);
  const [selected, setSelected] = useState(null);
  const [values, setValues] = useState({});
  const [result, setResult] = useState(null);
  const [log, setLog] = useState([]);
  const [error, setError] = useState(null);
  const [missing, setMissing] = useState(false);
  const [busy, setBusy] = useState(false);
  const sessionRef = useRef(null);

  useEffect(() => {
    apiClient
      .listProfiles({ pageNumber: 1, pageSize: 1000 })
      .then((r) => setProfiles(r.items || []))
      .catch(() => setProfiles([]));
  }, [apiClient]);

  const send = async (method, params, options = {}) => {
    const message = rpcRequest(method, params, options);
    setLog((l) => [...l, { direction: 'request', message }]);
    const response = await apiClient.mcpRequest(profile || null, message, { sessionId: sessionRef.current });
    const sid = response.headers?.get?.('mcp-session-id');
    if (sid) {
      sessionRef.current = sid;
      setSession(sid);
    }
    if (!response.ok && response.status !== 202) {
      const text = await response.text().catch(() => '');
      let body = text;
      try {
        body = text ? JSON.parse(text) : response.statusText;
      } catch {
        /* text */
      }
      throw new ApiError(response.status, body);
    }
    if (options.notification) return null;
    const messages = await readRpcResponse(response, (m) => setLog((l) => [...l, { direction: 'response', message: m }]));
    const reply = responseFor(messages, message.id);
    if (reply?.error) throw new ApiError(400, { code: `JsonRpc${reply.error.code}`, message: reply.error.message });
    return reply?.result;
  };

  const connect = async () => {
    setBusy(true);
    setError(null);
    setMissing(false);
    setSelected(null);
    setResult(null);
    sessionRef.current = null;
    setSession(null);
    try {
      const initResult = await send('initialize', { protocolVersion: PROTOCOL_VERSION, capabilities: {}, clientInfo: { name: 'hugo-dashboard', version: APP_VERSION } });
      setInit(initResult);
      await send('notifications/initialized', undefined, { notification: true });
      const list = await send('tools/list', {});
      setTools(list?.tools || []);
    } catch (e) {
      if (e?.status === 404) setMissing(true);
      else setError(e);
    } finally {
      setBusy(false);
    }
  };

  const descriptors = useMemo(() => (selected ? jsonSchemaToDescriptors(selected.inputSchema) : []), [selected]);

  const call = async () => {
    let args;
    try {
      args = argumentsFromValues(descriptors, values);
    } catch (e) {
      setError(new ApiError(400, { code: 'InvalidJson', message: t('views.mcpExplorer.invalidJson', { field: e.message }) }));
      return;
    }
    setBusy(true);
    setError(null);
    setResult(null);
    try {
      setResult(await send('tools/call', { name: selected.name, arguments: args }));
    } catch (e) {
      setError(e);
    } finally {
      setBusy(false);
    }
  };

  const groups = groupToolsByFamily(tools);
  const resultText = (result?.content || [])
    .filter((c) => c.type === 'text')
    .map((c) => c.text)
    .join('\n');

  return (
    <div>
      <PageHeader title={t('views.mcpExplorer.title')} subtitle={t('views.mcpExplorer.subtitle')} />
      <div className="panel explorer-connect">
        <FormField
          id="mcp-profile"
          field={{ name: 'profile', type: 'select', label: t('views.mcpExplorer.profile'), tip: t('tips.views.mcpExplorer.profile'), noneLabel: t('views.mcpExplorer.defaultProfile'), options: profiles.map((p) => ({ value: p.name, label: p.displayName || p.name })) }}
          value={profile}
          onChange={(v) => setProfile(v || '')}
        />
        <button type="button" className="button-primary" onClick={connect} disabled={busy} data-testid="mcp-connect" {...tip(t('tips.views.mcpExplorer.connect'))}>
          <RefreshIcon size={14} /> {session || init ? t('views.mcpExplorer.reconnect') : t('views.mcpExplorer.connect')}
        </button>
        {session && (
          <span className="text-muted" {...tip(t('tips.views.mcpExplorer.session'))}>
            {t('views.mcpExplorer.session', { id: session })}
          </span>
        )}
      </div>
      {missing && <UnavailableState feature={t('views.mcpExplorer.title')} purpose={t('views.mcpExplorer.purpose')} endpoint={profile ? `/mcp/${profile}` : '/mcp'} onRetry={connect} />}
      {error && <ErrorPresenter error={error} title={errorMessage(t, error) ? t('views.mcpExplorer.failed') : undefined} />}
      {init && (
        <div className="explorer-layout">
          <aside className="panel explorer-ops" aria-label={t('views.mcpExplorer.tools')}>
            <h3 className="section-title">{t('views.mcpExplorer.toolsCount', { count: tools.length })}</h3>
            {groups.length === 0 && <div className="text-muted">{t('views.mcpExplorer.noTools')}</div>}
            {groups.map((g) => (
              <div key={g.family} className="explorer-group">
                <div className="sidebar-group-label">{g.family}</div>
                {g.tools.map((tool) => (
                  <button
                    key={tool.name}
                    type="button"
                    className={`explorer-op ${selected?.name === tool.name ? 'active' : ''}`}
                    onClick={() => {
                      setSelected(tool);
                      setValues({});
                      setResult(null);
                    }}
                    data-tool={tool.name}
                    {...tip(tool.description || tool.name)}
                  >
                    <code>{tool.name}</code>
                  </button>
                ))}
              </div>
            ))}
          </aside>
          <section className="explorer-main">
            <div className="panel">
              <h3 className="section-title">{t('views.mcpExplorer.serverInfo')}</h3>
              <p>
                <code>{init.serverInfo?.name || '-'}</code> {init.serverInfo?.version || ''} <span className="text-muted">{init.protocolVersion}</span>
              </p>
              <details open>
                <summary {...tip(t('tips.views.mcpExplorer.instructions'))}>{t('views.mcpExplorer.instructions')}</summary>
                <MarkdownView text={init.instructions || ''} emptyText={t('views.mcpExplorer.noInstructions')} />
              </details>
            </div>
            {selected && (
              <div className="panel" data-testid="mcp-tool">
                <h3 className="section-title">
                  <code>{selected.name}</code>
                </h3>
                {selected.description && <p className="field-hint">{selected.description}</p>}
                <SchemaForm schema={descriptors} values={values} onChange={setValues} idPrefix="mcp-arg" />
                <div className="panel-actions">
                  <button type="button" className="button-primary" onClick={call} disabled={busy} data-testid="mcp-call" {...tip(t('tips.views.mcpExplorer.call'))}>
                    <PlayIcon size={14} /> {t('views.mcpExplorer.call')}
                  </button>
                </div>
                {result && (
                  <>
                    <div className={result.isError ? 'error-banner' : 'success-banner'}>{result.isError ? t('views.mcpExplorer.toolError') : t('views.mcpExplorer.toolOk')}</div>
                    {resultText && <CodeBlock value={resultText} />}
                    <CodeBlock value={result} prettyJson />
                  </>
                )}
              </div>
            )}
          </section>
        </div>
      )}
      {log.length > 0 && (
        <details className="panel" data-testid="mcp-log">
          <summary {...tip(t('tips.views.mcpExplorer.log'))}>{t('views.mcpExplorer.log', { count: log.length })}</summary>
          <CodeBlock value={log} prettyJson />
        </details>
      )}
    </div>
  );
}
