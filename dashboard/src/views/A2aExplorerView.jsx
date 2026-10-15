import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import PageHeader from '../components/PageHeader';
import CodeBlock from '../components/CodeBlock';
import CopyButton from '../components/CopyButton';
import { UnavailableState, ErrorPresenter } from '../components/States';
import { FormField } from '../components/FormFields';
import { tip } from '../components/Tooltip';
import { SendIcon, RefreshIcon, ListIcon } from '../components/Icons';
import { useAuth } from '../context/AuthContext';
import { useApp } from '../context/AppContext';
import { ApiError } from '../utils/api';
import { rpcRequest, readRpcResponse, responseFor } from '../utils/jsonRpc';
import { a2aCardUrl } from '../utils/snippets';

/** Text of an A2A message or task result: every text part of the status message, history tail, and artifacts. */
export function a2aText(result) {
  if (!result) return '';
  const parts = [];
  const collect = (msg) => (msg?.parts || []).forEach((p) => (p.kind === 'text' || p.type === 'text') && p.text && parts.push(p.text));
  if (result.kind === 'message' || result.parts) collect(result);
  collect(result.status?.message);
  (result.artifacts || []).forEach(collect);
  return parts.join('\n');
}

/**
 * A2A Explorer (P8.42): fetch a profile's agent card, send a message (or stream one), and list tasks, using the
 * A2A 1.0 JSON-RPC methods at /v1.0/a2a/{tenantId}/{profileName} as the signed-in principal.
 */
export default function A2aExplorerView() {
  const { t } = useTranslation();
  const { apiClient, serverUrl } = useAuth();
  const { tenantId } = useApp();
  const [profiles, setProfiles] = useState([]);
  const [profile, setProfile] = useState('');
  const [card, setCard] = useState(null);
  const [message, setMessage] = useState('');
  const [stream, setStream] = useState(true);
  const [contextId, setContextId] = useState('');
  const [events, setEvents] = useState([]);
  const [result, setResult] = useState(null);
  const [tasks, setTasks] = useState(null);
  const [error, setError] = useState(null);
  const [missing, setMissing] = useState(false);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    apiClient
      .listProfiles({ pageNumber: 1, pageSize: 1000 })
      .then((r) => {
        const items = r.items || [];
        setProfiles(items);
        setProfile((p) => p || (items.find((x) => x.isDefault) || items[0])?.name || 'Hugo');
      })
      .catch(() => setProfile((p) => p || 'Hugo'));
  }, [apiClient]);

  const fetchCard = async () => {
    setBusy(true);
    setError(null);
    setMissing(false);
    try {
      setCard(await apiClient.getA2aCard(tenantId, profile));
    } catch (e) {
      setCard(null);
      if (e?.status === 404) setMissing(true);
      else setError(e);
    } finally {
      setBusy(false);
    }
  };

  const rpc = async (method, params, onMessage) => {
    const request = rpcRequest(method, params);
    const response = await apiClient.a2aRequest(tenantId, profile, request);
    if (!response.ok) {
      const text = await response.text().catch(() => '');
      let body = text;
      try {
        body = text ? JSON.parse(text) : response.statusText;
      } catch {
        /* text */
      }
      throw new ApiError(response.status, body);
    }
    const messages = await readRpcResponse(response, onMessage);
    const reply = responseFor(messages, request.id);
    if (reply?.error) throw new ApiError(400, { code: `JsonRpc${reply.error.code}`, message: reply.error.message });
    return reply?.result;
  };

  const sendMessage = async () => {
    if (!message.trim()) return;
    setBusy(true);
    setError(null);
    setEvents([]);
    setResult(null);
    try {
      const params = {
        message: {
          kind: 'message',
          role: 'user',
          messageId: `msg-${Date.now()}`,
          parts: [{ kind: 'text', text: message }],
          ...(contextId ? { contextId } : {})
        }
      };
      const res = await rpc(stream ? 'SendStreamingMessage' : 'SendMessage', params, stream ? (m) => setEvents((list) => [...list, m]) : undefined);
      setResult(res);
      if (res?.contextId) setContextId(res.contextId);
    } catch (e) {
      if (e?.status === 404) setMissing(true);
      else setError(e);
    } finally {
      setBusy(false);
    }
  };

  const listTasks = async () => {
    setBusy(true);
    setError(null);
    try {
      const res = await rpc('ListTasks', {});
      setTasks(res?.tasks || (Array.isArray(res) ? res : []));
    } catch (e) {
      if (e?.status === 404) setMissing(true);
      else setError(e);
    } finally {
      setBusy(false);
    }
  };

  const cardUrl = a2aCardUrl({ serverUrl, tenantId, profile });
  const streamedText = events
    .map((e) => a2aText(e.result))
    .filter(Boolean)
    .join('\n');

  return (
    <div>
      <PageHeader title={t('views.a2aExplorer.title')} subtitle={t('views.a2aExplorer.subtitle')} />
      <div className="panel explorer-connect">
        <FormField
          id="a2a-profile"
          field={{
            name: 'profile',
            type: 'select',
            required: true,
            label: t('views.a2aExplorer.profile'),
            tip: t('tips.views.a2aExplorer.profile'),
            options: (profiles.length ? profiles : [{ name: profile }]).filter((p) => p.name).map((p) => ({ value: p.name, label: p.displayName || p.name }))
          }}
          value={profile}
          onChange={(v) => {
            setProfile(v);
            setCard(null);
            setContextId('');
          }}
        />
        <button
          type="button"
          className="button-secondary"
          onClick={fetchCard}
          disabled={busy || !profile}
          data-testid="a2a-card"
          {...tip(t('tips.views.a2aExplorer.fetchCard'))}
        >
          <RefreshIcon size={14} /> {t('views.a2aExplorer.fetchCard')}
        </button>
        <button
          type="button"
          className="button-secondary"
          onClick={listTasks}
          disabled={busy || !profile}
          data-testid="a2a-tasks"
          {...tip(t('tips.views.a2aExplorer.listTasks'))}
        >
          <ListIcon size={14} /> {t('views.a2aExplorer.listTasks')}
        </button>
      </div>
      <div className="secret-row">
        <code>{cardUrl}</code>
        <CopyButton value={cardUrl} tipText={t('tips.views.profile.connect.copyCard')} />
      </div>
      {missing && (
        <UnavailableState
          feature={t('views.a2aExplorer.title')}
          purpose={t('views.a2aExplorer.purpose')}
          endpoint={`/v1.0/a2a/${tenantId}/${profile}`}
          onRetry={fetchCard}
        />
      )}
      {error && <ErrorPresenter error={error} />}
      {card && (
        <div className="panel" data-testid="a2a-card-body">
          <h3 className="section-title">{card.name || profile}</h3>
          {card.description && <p className="field-hint">{card.description}</p>}
          <CodeBlock value={card} prettyJson />
        </div>
      )}
      <div className="panel">
        <FormField
          id="a2a-message"
          field={{ name: 'message', type: 'textarea', rows: 4, label: t('views.a2aExplorer.message'), tip: t('tips.views.a2aExplorer.message') }}
          value={message}
          onChange={setMessage}
        />
        <div className="panel-actions">
          <label className="field-checkbox" {...tip(t('tips.views.a2aExplorer.stream'))}>
            <input type="checkbox" checked={stream} onChange={(e) => setStream(e.target.checked)} {...tip(t('tips.views.a2aExplorer.stream'))} />{' '}
            {t('views.a2aExplorer.stream')}
          </label>
          {contextId && (
            <span className="text-muted" {...tip(t('tips.views.a2aExplorer.context'))}>
              {t('views.a2aExplorer.context', { id: contextId })}
            </span>
          )}
          <button
            type="button"
            className="button-primary"
            onClick={sendMessage}
            disabled={busy || !message.trim() || !profile}
            data-testid="a2a-send"
            {...tip(t('tips.views.a2aExplorer.send'))}
          >
            <SendIcon size={14} /> {t('views.a2aExplorer.send')}
          </button>
        </div>
        {streamedText && <CodeBlock value={streamedText} />}
        {result && (
          <>
            <div className="section-title">{t('views.a2aExplorer.result')}</div>
            {a2aText(result) && <CodeBlock value={a2aText(result)} />}
            <CodeBlock value={result} prettyJson />
          </>
        )}
        {events.length > 0 && (
          <details>
            <summary {...tip(t('tips.views.a2aExplorer.events'))}>{t('views.a2aExplorer.events', { count: events.length })}</summary>
            <CodeBlock value={events} prettyJson />
          </details>
        )}
      </div>
      {tasks && (
        <div className="panel" data-testid="a2a-task-list">
          <h3 className="section-title">{t('views.a2aExplorer.tasks', { count: tasks.length })}</h3>
          {tasks.length === 0 ? <div className="text-muted">{t('views.a2aExplorer.noTasks')}</div> : <CodeBlock value={tasks} prettyJson />}
        </div>
      )}
    </div>
  );
}
