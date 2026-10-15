import { useCallback, useEffect, useMemo, useReducer, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import PageHeader from '../components/PageHeader';
import ChatPanel from '../components/ChatPanel';
import ApprovalCard from '../components/ApprovalCard';
import { LoadingState, UnavailableState, ErrorPresenter, EmptyState } from '../components/States';
import { EnumBadge } from '../components/StatusBadge';
import { tip } from '../components/Tooltip';
import { PlusIcon, DownloadIcon, InfoIcon } from '../components/Icons';
import { useAuth } from '../context/AuthContext';
import { useToast } from '../context/ToastContext';
import { useServerEvent } from '../context/EventsContext';
import useDebounce from '../hooks/useDebounce';
import { pathFor } from '../config/navConfig';
import { STORAGE_KEYS, accessModeTone } from '../utils/constants';
import { errorMessage, isMissingEndpoint } from '../utils/errors';
import { downloadText } from '../utils/exporters';
import { formatNumber, formatRelativeTime, formatDateTime } from '../i18n/formatters';
import { initialChatState, beginTurn, applyChatEvent, endTurn, fromStoredMessages, pendingApprovals } from '../utils/chatStream';

function chatReducer(state, action) {
  switch (action.type) {
    case 'reset':
      return initialChatState(action.messages || []);
    case 'begin':
      return beginTurn(state, action.text);
    case 'event':
      return applyChatEvent(state, action.evt);
    case 'end':
      return endTurn(state, action.options);
    default:
      return state;
  }
}

function readDrafts() {
  try {
    return JSON.parse(localStorage.getItem(STORAGE_KEYS.chatDrafts) || '{}') || {};
  } catch {
    return {};
  }
}

function writeDraft(key, text) {
  try {
    const all = readDrafts();
    if (text) all[key] = text;
    else delete all[key];
    localStorage.setItem(STORAGE_KEYS.chatDrafts, JSON.stringify(all));
  } catch {
    /* ignore */
  }
}

/**
 * Chat with Agent Smith (plan section 18.4): conversation list on the left, the streaming transcript in the center,
 * and a collapsible context drawer on the right (profile, bound resources with access modes, model, token usage,
 * pending approvals). /dashboard/chat starts a new conversation; /dashboard/chat/:conversationId continues one.
 */
export default function ChatView({ recordId }) {
  const { t, i18n } = useTranslation();
  const { apiClient } = useAuth();
  const { addToast } = useToast();
  const navigate = useNavigate();
  const [state, dispatch] = useReducer(chatReducer, undefined, () => initialChatState());
  const [profiles, setProfiles] = useState(null);
  const [profilesError, setProfilesError] = useState(null);
  const [conversations, setConversations] = useState([]);
  const [listError, setListError] = useState(null);
  const [search, setSearch] = useState('');
  const debouncedSearch = useDebounce(search, 350);
  const [profileId, setProfileId] = useState(() => localStorage.getItem(STORAGE_KEYS.chatProfile) || '');
  const [conversation, setConversation] = useState(null);
  const [loadingConversation, setLoadingConversation] = useState(!!recordId);
  const [draft, setDraft] = useState(() => readDrafts()[recordId || 'new'] || '');
  const [drawerOpen, setDrawerOpen] = useState(() => localStorage.getItem(STORAGE_KEYS.chatDrawer) !== 'closed');
  const [context, setContext] = useState({ profile: null, bindings: [], model: null });
  const [approvalBusy, setApprovalBusy] = useState(null);
  const controllerRef = useRef(null);
  const conversationIdRef = useRef(recordId || null);

  // Profiles for the picker. A server without agent profiles yet means chat is not available.
  useEffect(() => {
    if (!apiClient) return;
    apiClient
      .listProfiles({ pageNumber: 1, pageSize: 1000 })
      .then((r) => {
        const items = r.items || [];
        setProfiles(items);
        setProfileId((current) => (current && items.some((p) => p.id === current) ? current : (items.find((p) => p.isDefault) || items[0])?.id || ''));
      })
      .catch((e) => {
        setProfiles([]);
        setProfilesError(e);
      });
  }, [apiClient]);

  const loadConversations = useCallback(() => {
    if (!apiClient) return;
    apiClient
      .listConversations({ pageNumber: 1, pageSize: 100, search: debouncedSearch || undefined, source: 'Dashboard' })
      .then((r) => {
        setConversations(r.items || []);
        setListError(null);
      })
      .catch(setListError);
  }, [apiClient, debouncedSearch]);

  useEffect(loadConversations, [loadConversations]);
  useServerEvent(['conversation.updated'], loadConversations);

  // The open conversation and its messages.
  useEffect(() => {
    conversationIdRef.current = recordId || null;
    if (!apiClient || !recordId) {
      setConversation(null);
      dispatch({ type: 'reset' });
      setLoadingConversation(false);
      return;
    }
    setLoadingConversation(true);
    Promise.all([apiClient.getConversation(recordId), apiClient.listMessages(recordId)])
      .then(([conv, messages]) => {
        setConversation(conv);
        if (conv?.agentProfileId) setProfileId(conv.agentProfileId);
        dispatch({ type: 'reset', messages: fromStoredMessages(messages.items || []) });
      })
      .catch((e) => {
        setConversation(null);
        addToast({ type: 'error', message: errorMessage(t, e) });
      })
      .finally(() => setLoadingConversation(false));
  }, [apiClient, recordId, addToast, t]);

  // Context drawer: the profile, its bindings, and its inference endpoint.
  useEffect(() => {
    if (!apiClient || !profileId) {
      setContext({ profile: null, bindings: [], model: null });
      return;
    }
    let active = true;
    Promise.allSettled([apiClient.getProfile(profileId), apiClient.listBindings(profileId, { pageNumber: 1, pageSize: 1000 })]).then(([p, b]) => {
      if (!active) return;
      const profile = p.status === 'fulfilled' ? p.value : (profiles || []).find((x) => x.id === profileId) || null;
      const bindings = b.status === 'fulfilled' ? b.value.items || [] : [];
      setContext({ profile, bindings, model: null });
      if (profile?.inferenceEndpointId) {
        apiClient
          .getModelEndpoint(profile.inferenceEndpointId)
          .then((m) => active && setContext((c) => ({ ...c, model: m })))
          .catch(() => {});
      }
    });
    return () => {
      active = false;
    };
  }, [apiClient, profileId, profiles]);

  // Stop streaming when the page goes away.
  const mountedRef = useRef(true);
  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
      controllerRef.current?.abort();
    };
  }, []);

  const updateDraft = (text) => {
    setDraft(text);
    writeDraft(recordId || 'new', text);
  };

  const selectedProfile = useMemo(() => (profiles || []).find((p) => p.id === profileId) || context.profile, [profiles, profileId, context.profile]);
  const noModel = selectedProfile && !selectedProfile.inferenceEndpointId;

  const send = async (text) => {
    let id = conversationIdRef.current;
    try {
      if (!id) {
        const created = await apiClient.createConversation({ agentProfileId: profileId || undefined });
        id = created?.id;
        conversationIdRef.current = id;
        setConversation(created);
      }
    } catch (e) {
      addToast({ type: 'error', message: errorMessage(t, e) });
      return;
    }
    dispatch({ type: 'begin', text });
    updateDraft('');
    writeDraft('new', '');
    controllerRef.current = apiClient.streamMessage(
      id,
      { content: text },
      {
        onEvent: (evt) => dispatch({ type: 'event', evt }),
        onEnd: (error, info) => {
          controllerRef.current = null;
          dispatch({ type: 'end', options: { stopped: !!info?.aborted, error: error ? { code: error.code, message: errorMessage(t, error) } : null } });
          loadConversations();
          // A new conversation gets its own URL once the first turn ends (navigating earlier would remount the page
          // and cut the stream).
          if (!recordId && id && mountedRef.current) navigate(pathFor('chat', id), { replace: true });
        }
      }
    );
  };

  const stop = () => {
    const id = conversationIdRef.current;
    controllerRef.current?.abort();
    if (id) apiClient.cancelConversation(id).catch(() => {});
  };

  const decide = async (approvalId, approve, note) => {
    setApprovalBusy(approvalId);
    try {
      if (approve) await apiClient.approveApproval(approvalId, note);
      else await apiClient.rejectApproval(approvalId, note);
      dispatch({ type: 'event', evt: { event: 'approval.resolved', data: { approvalId, status: approve ? 'Approved' : 'Rejected', note } } });
    } catch (e) {
      addToast({ type: 'error', message: errorMessage(t, e) });
    } finally {
      setApprovalBusy(null);
    }
  };

  const exportConversation = async () => {
    const id = conversationIdRef.current;
    if (!id) return;
    try {
      const text = await apiClient.exportConversation(id, 'md');
      downloadText(`conversation-${id}.md`, typeof text === 'string' ? text : JSON.stringify(text, null, 2), 'text/markdown');
    } catch (e) {
      addToast({ type: 'error', message: errorMessage(t, e) });
    }
  };

  const toggleDrawer = () => {
    setDrawerOpen((v) => {
      localStorage.setItem(STORAGE_KEYS.chatDrawer, v ? 'closed' : 'open');
      return !v;
    });
  };

  if (profiles === null) return <LoadingState />;

  const chatMissing = isMissingEndpoint(profilesError) || isMissingEndpoint(listError);
  if (chatMissing) {
    return (
      <div>
        <PageHeader title={t('views.chat.title')} subtitle={t('views.chat.subtitle')} />
        <UnavailableState feature={t('views.chat.title')} purpose={t('views.chat.purpose')} endpoint="/v1.0/api/conversations" />
      </div>
    );
  }

  const pending = pendingApprovals(state);
  const totals = state.messages.reduce((acc, m) => ({ input: acc.input + (m.usage?.inputTokens || 0), output: acc.output + (m.usage?.outputTokens || 0) }), {
    input: conversation?.totalInputTokens || 0,
    output: conversation?.totalOutputTokens || 0
  });

  return (
    <div className="chat-view">
      <PageHeader
        title={conversation?.title || t('views.chat.title')}
        subtitle={t('views.chat.subtitle')}
        actions={
          <>
            {recordId && (
              <button type="button" className="button-secondary" onClick={exportConversation} {...tip(t('tips.views.chat.export'))}>
                <DownloadIcon size={14} /> {t('views.chat.export')}
              </button>
            )}
            <button type="button" className="button-secondary" onClick={toggleDrawer} aria-pressed={drawerOpen} {...tip(t('tips.views.chat.drawer'))}>
              <InfoIcon size={14} /> {t('views.chat.context')}
            </button>
            <button
              type="button"
              className="button-primary"
              onClick={() => navigate(pathFor('chat'))}
              data-testid="new-conversation"
              {...tip(t('tips.views.chat.new'))}
            >
              <PlusIcon size={14} /> {t('views.chat.new')}
            </button>
          </>
        }
      />
      {profilesError && !chatMissing && <ErrorPresenter error={profilesError} />}
      <div className={`chat-layout ${drawerOpen ? 'with-drawer' : ''}`}>
        <aside className="chat-list panel" aria-label={t('views.chat.conversations')}>
          <input
            type="search"
            className="filter-search"
            value={search}
            placeholder={t('views.chat.search')}
            aria-label={t('views.chat.search')}
            onChange={(e) => setSearch(e.target.value)}
            {...tip(t('tips.views.chat.search'))}
          />
          {listError && <ErrorPresenter error={listError} onRetry={loadConversations} />}
          {conversations.length === 0 ? (
            <div className="text-muted chat-list-empty">{t('views.chat.noConversations')}</div>
          ) : (
            <ul className="chat-list-items">
              {conversations.map((c) => (
                <li key={c.id}>
                  <a
                    href={pathFor('chat', c.id)}
                    className={`chat-list-item ${c.id === recordId ? 'active' : ''}`}
                    aria-current={c.id === recordId ? 'page' : undefined}
                    onClick={(e) => {
                      e.preventDefault();
                      navigate(pathFor('chat', c.id));
                    }}
                    {...tip(formatDateTime(c.lastMessageUtc || c.createdUtc, i18n.language))}
                  >
                    <span className="chat-list-title">{c.title || t('views.chat.untitled')}</span>
                    <span className="chat-list-meta text-muted">{formatRelativeTime(c.lastMessageUtc || c.createdUtc, i18n.language)}</span>
                  </a>
                </li>
              ))}
            </ul>
          )}
        </aside>

        <section className="chat-main">
          <div className="chat-toolbar">
            <label className="filter-select" {...tip(t('tips.views.chat.profile'))}>
              <span>{t('views.chat.profile')}</span>
              <select
                value={profileId}
                onChange={(e) => {
                  setProfileId(e.target.value);
                  localStorage.setItem(STORAGE_KEYS.chatProfile, e.target.value);
                }}
                disabled={!!recordId || state.streaming}
                aria-label={t('views.chat.profile')}
                data-testid="chat-profile"
                {...tip(t('tips.views.chat.profile'))}
              >
                {(profiles || []).map((p) => (
                  <option key={p.id} value={p.id}>
                    {p.displayName || p.name}
                  </option>
                ))}
              </select>
            </label>
            {context.model && (
              <span className="topbar-chip" {...tip(t('tips.views.chat.model'))}>
                {t('views.chat.model', { model: context.model.model || context.model.name })}
              </span>
            )}
          </div>
          {profiles.length === 0 ? (
            <EmptyState
              title={t('views.chat.noProfilesTitle')}
              body={t('views.chat.noProfilesBody')}
              action={
                <button type="button" className="button-primary" onClick={() => navigate(pathFor('profiles'))} {...tip(t('tips.views.chat.goProfiles'))}>
                  {t('views.chat.goProfiles')}
                </button>
              }
            />
          ) : loadingConversation ? (
            <LoadingState />
          ) : (
            <ChatPanel
              messages={state.messages}
              streaming={state.streaming}
              draft={draft}
              onDraftChange={updateDraft}
              onSend={send}
              onStop={stop}
              onApprove={(id, note) => decide(id, true, note)}
              onReject={(id, note) => decide(id, false, note)}
              approvalBusy={approvalBusy}
              disabled={!!noModel}
              disabledReason={noModel ? t('views.chat.noModel') : null}
              emptyHint={t('views.chat.emptyHint', { persona: selectedProfile?.personaName || 'Agent Smith' })}
            />
          )}
          {noModel && (
            <div className="info-banner" data-testid="no-model">
              <span>{t('views.chat.noModel')}</span>
              <button type="button" className="button-primary button-sm" onClick={() => navigate(pathFor('models'))} {...tip(t('tips.views.chat.goModels'))}>
                {t('views.chat.goModels')}
              </button>
            </div>
          )}
        </section>

        {drawerOpen && (
          <aside className="chat-drawer panel" aria-label={t('views.chat.context')} data-testid="chat-drawer">
            <h3 className="section-title">{t('views.chat.profile')}</h3>
            {selectedProfile ? (
              <dl className="kv-grid">
                <dt>{t('views.chat.persona')}</dt>
                <dd>{selectedProfile.personaName || 'Agent Smith'}</dd>
                <dt>{t('views.chat.addresses')}</dt>
                <dd>{selectedProfile.userAddress || 'Mr. Anderson'}</dd>
                <dt>{t('views.chat.modelLabel')}</dt>
                <dd>{context.model ? context.model.model || context.model.name : t('common.generic.none')}</dd>
              </dl>
            ) : (
              <div className="text-muted">{t('common.generic.none')}</div>
            )}
            <h3 className="section-title">{t('views.chat.resources')}</h3>
            {context.bindings.length === 0 ? (
              <div className="text-muted">{t('views.chat.noResources')}</div>
            ) : (
              <ul className="plain-list chat-bindings">
                {context.bindings.map((b) => (
                  <li key={b.id}>
                    <code>{b.alias}</code> <EnumBadge group="accessMode" value={b.accessMode} tone={accessModeTone(b.accessMode)} />
                  </li>
                ))}
              </ul>
            )}
            <h3 className="section-title">{t('views.chat.usage')}</h3>
            <div {...tip(t('tips.chat.tokens'))}>
              {t('components.chat.tokens', { input: formatNumber(totals.input, {}, i18n.language), output: formatNumber(totals.output, {}, i18n.language) })}
            </div>
            <h3 className="section-title">{t('views.chat.pending')}</h3>
            {pending.length === 0 ? (
              <div className="text-muted">{t('views.chat.noPending')}</div>
            ) : (
              pending.map((a) => (
                <ApprovalCard
                  key={a.id}
                  approval={a}
                  compact
                  busy={approvalBusy === a.id}
                  onApprove={(note) => decide(a.id, true, note)}
                  onReject={(note) => decide(a.id, false, note)}
                />
              ))
            )}
          </aside>
        )}
      </div>
    </div>
  );
}
