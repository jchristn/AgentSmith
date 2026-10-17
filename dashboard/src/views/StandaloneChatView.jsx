import { useEffect, useMemo, useReducer, useRef, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import ApiClient from '../utils/api';
import { defaultServerUrl } from '../utils/constants';
import { fromStoredMessages, initialChatState } from '../utils/chatStream';
import { safeChatMessages, standaloneChatReducer } from '../utils/standaloneChat';
import { errorMessage } from '../utils/errors';
import ChatPanel from '../components/ChatPanel';
import LanguageSelector from '../components/LanguageSelector';
import { SunIcon, MoonIcon } from '../components/Icons';
import { tip } from '../components/Tooltip';
import './StandaloneChatView.css';

/** Link-scoped chat has no dashboard navigation and never renders internal tool output or reasoning. */
export default function StandaloneChatView() {
  const { linkId } = useParams();
  const { t } = useTranslation();
  const { isAuthenticated, isLoading, token, principal, theme, toggleTheme } = useAuth();
  // The link belongs to this deployment, independently of a previously saved dashboard server URL.
  const client = useMemo(() => new ApiClient(defaultServerUrl(), isAuthenticated ? token : null), [isAuthenticated, token]);
  const [metadata, setMetadata] = useState(null);
  const [error, setError] = useState(null);
  const [loading, setLoading] = useState(true);
  const [session, setSession] = useState(null);
  const [draft, setDraft] = useState('');
  const [starting, setStarting] = useState(false);
  const [state, dispatch] = useReducer(standaloneChatReducer, undefined, () => initialChatState());
  const controller = useRef(null);
  const generation = useRef(0);
  const storageKey = `hugo_chat_link:${linkId}:${isAuthenticated ? `authenticated:${principal?.userId || principal?.principalId || principal?.id || 'unknown'}` : 'public'}`;

  useEffect(() => {
    if (isLoading) return;
    const current = ++generation.current;
    setLoading(true);
    setMetadata(null);
    setSession(null);
    setError(null);
    dispatch({ type: 'reset' });
    client
      .getChatLink(linkId)
      .then(async (info) => {
        if (current !== generation.current) return;
        setMetadata(info);
        if (info.authenticationRequired !== false && info.accessMode !== 'Public' && !isAuthenticated) return;
        let saved;
        try {
          saved = JSON.parse(sessionStorage.getItem(storageKey) || 'null');
        } catch {
          /* Storage can be disabled. */
        }
        if (!saved?.id) return;
        try {
          const result = await client.getChatMessages(linkId, saved.id, saved.sessionToken);
          if (current !== generation.current) return;
          setSession(saved);
          dispatch({ type: 'reset', messages: safeChatMessages(fromStoredMessages(Array.isArray(result) ? result : result.items || result.messages || [])) });
        } catch {
          try {
            sessionStorage.removeItem(storageKey);
          } catch {
            /* Storage can be disabled. */
          }
        }
      })
      .catch((e) => current === generation.current && setError(e))
      .finally(() => current === generation.current && setLoading(false));
    return () => {
      generation.current = current + 1;
      controller.current?.abort();
    };
  }, [client, linkId, storageKey, isAuthenticated, isLoading]);

  const stop = () => {
    controller.current?.abort();
    controller.current = null;
    if (session) client.cancelChatMessage(linkId, session.id, session.sessionToken).catch(() => {});
  };

  const newChat = () => {
    stop();
    generation.current++;
    setSession(null);
    setDraft('');
    setError(null);
    dispatch({ type: 'reset' });
    try {
      sessionStorage.removeItem(storageKey);
    } catch {
      /* Storage can be disabled. */
    }
  };

  const send = async (text) => {
    if (state.streaming || starting) return;
    setStarting(true);
    setError(null);
    const current = generation.current;
    try {
      let active = session;
      if (!active) {
        active = await client.createChatSession(linkId);
        if (current !== generation.current) return;
        setSession(active);
        try {
          sessionStorage.setItem(storageKey, JSON.stringify({ id: active.id, sessionToken: active.sessionToken }));
        } catch {
          /* Chat still works without storage. */
        }
      }
      dispatch({ type: 'begin', text });
      setDraft('');
      const abort = new AbortController();
      controller.current = abort;
      try {
        const result = await client.sendChatMessage(linkId, active.id, active.sessionToken, { message: text }, abort.signal);
        if (current !== generation.current) return;
        dispatch({ type: 'event', evt: { event: 'assistant.delta', data: { text: result.text || '' } } });
        dispatch({ type: 'event', evt: { event: 'turn.completed', data: { tokensIn: result.tokensIn, tokensOut: result.tokensOut } } });
        dispatch({ type: 'end' });
      } catch (e) {
        if (current === generation.current)
          dispatch({ type: 'end', options: { stopped: e?.name === 'AbortError', error: e?.name !== 'AbortError' ? { message: errorMessage(t, e) } : null } });
      } finally {
        if (current === generation.current) controller.current = null;
      }
    } catch (e) {
      if (current === generation.current) setError(e);
    } finally {
      if (current === generation.current) setStarting(false);
    }
  };

  const requiresLogin = metadata && metadata.accessMode !== 'Public' && !isAuthenticated;
  return (
    <main className="standalone-chat" data-testid="standalone-chat">
      <header className="standalone-chat-header">
        <div>
          <span className="standalone-chat-brand">{t('common.appName')}</span>
          <h1>{metadata?.displayName || metadata?.name || t('standaloneChat.title')}</h1>
        </div>
        <div className="standalone-chat-actions">
          <LanguageSelector />
          <button
            type="button"
            className="icon-button"
            onClick={toggleTheme}
            aria-label={theme === 'dark' ? t('common.theme.switchToLight') : t('common.theme.switchToDark')}
            {...tip(t(theme === 'dark' ? 'tips.topbar.themeLight' : 'tips.topbar.themeDark'))}
          >
            {theme === 'dark' ? <SunIcon /> : <MoonIcon />}
          </button>
          {!requiresLogin && metadata && (
            <button type="button" className="button-secondary" onClick={newChat} disabled={starting} {...tip(t('standaloneChat.newTip'))}>
              {t('standaloneChat.new')}
            </button>
          )}
        </div>
      </header>
      {loading || isLoading ? (
        <div role="status" className="standalone-chat-notice">
          {t('common.generic.loading')}
        </div>
      ) : error && !metadata ? (
        <div role="alert" className="standalone-chat-notice error-banner">
          {error.status === 404
            ? t('standaloneChat.unavailable')
            : error.status === 403
              ? t('standaloneChat.accessDenied')
              : error.status === 0
                ? t('standaloneChat.connectionFailed')
                : t('standaloneChat.failed')}
        </div>
      ) : requiresLogin ? (
        <section className="standalone-chat-notice">
          <p>{t('standaloneChat.loginRequired')}</p>
          <Link className="button-primary" to={`/?returnTo=${encodeURIComponent(`/chat/${linkId}`)}`} {...tip(t('standaloneChat.loginTip'))}>
            {t('standaloneChat.login')}
          </Link>
        </section>
      ) : (
        <>
          {metadata?.description && <p className="standalone-chat-description">{metadata.description}</p>}
          {error && (
            <div role="alert" className="error-banner">
              {errorMessage(t, error)}
            </div>
          )}
          <ChatPanel
            messages={safeChatMessages(state.messages)}
            streaming={state.streaming}
            draft={draft}
            onDraftChange={setDraft}
            onSend={send}
            onStop={stop}
            disabled={starting}
            emptyHint={t('standaloneChat.empty')}
          />
        </>
      )}
    </main>
  );
}
