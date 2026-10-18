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
import { SunIcon, MoonIcon, ChatIcon } from '../components/Icons';
import { tip } from '../components/Tooltip';
import './StandaloneChatView.css';

/** Link-scoped chat has no dashboard navigation and never renders internal tool output or reasoning. */
export default function StandaloneChatView() {
  const { linkId } = useParams();
  const { t } = useTranslation();
  const { isAuthenticated, isLoading, token, principal, theme, toggleTheme } = useAuth();
  // The link belongs to this deployment, independently of a previously saved dashboard server URL.
  const client = useMemo(() => new ApiClient(defaultServerUrl(), isAuthenticated ? token : null), [isAuthenticated, token]);
  const [logoFailed, setLogoFailed] = useState(false);
  const [metadata, setMetadata] = useState(null);
  const [error, setError] = useState(null);
  const [loading, setLoading] = useState(true);
  const [session, setSession] = useState(null);
  const [draft, setDraft] = useState('');
  const [starting, setStarting] = useState(false);
  const [remoteStreaming, setRemoteStreaming] = useState(false);
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
          setRemoteStreaming(!!result?.turnInProgress);
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

  useEffect(() => {
    if (!session || !remoteStreaming) return;
    let active = true;
    let timer;
    const refresh = async () => {
      try {
        const result = await client.getChatMessages(linkId, session.id, session.sessionToken);
        if (!active) return;
        if (!controller.current) dispatch({ type: 'reset', messages: safeChatMessages(fromStoredMessages(result.messages || result.items || [])) });
        setRemoteStreaming(!!result.turnInProgress);
        if (result.turnInProgress) timer = setTimeout(refresh, 1000);
      } catch (error) {
        if (active) { setError(error); setRemoteStreaming(false); }
      }
    };
    timer = setTimeout(refresh, 1000);
    return () => { active = false; clearTimeout(timer); };
  }, [client, linkId, session, remoteStreaming]);

  useEffect(() => {
    const original = document.title;
    document.title = metadata?.pageTitle || metadata?.title || metadata?.displayName || metadata?.name || t('standaloneChat.title');
    return () => {
      document.title = original;
    };
  }, [metadata, t]);

  useEffect(() => {
    setLogoFailed(false);
  }, [metadata?.logoUrl]);

  const submitFeedback = async (messageId, body) => {
    if (!session) return;
    return client.saveChatFeedback(linkId, session.id, session.sessionToken, messageId, body);
  };

  const stop = () => {
    controller.current?.abort();
    controller.current = null;
    if (session) client.cancelChatMessage(linkId, session.id, session.sessionToken).catch(() => {});
  };

  const newChat = () => {
    controller.current?.abort();
    controller.current = null;
    setRemoteStreaming(false);
    generation.current++;
    setSession(null);
    setStarting(false);
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
    if (state.streaming || remoteStreaming || starting) return;
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
      await new Promise((resolve) => {
        controller.current = client.streamChatMessage(
          linkId,
          active.id,
          active.sessionToken,
          { message: text },
          {
            onOpen: () => {
              if (current === generation.current) setStarting(false);
            },
            onEvent: (evt) => {
              if (current !== generation.current) return;
              // The standalone surface accepts text and final usage only, never internal model/tool events.
              if (evt.event === 'turn.completed' && typeof evt.data?.text === 'string') {
                dispatch({ type: 'final', text: evt.data.text });
              }
              if (['turn.started', 'assistant.delta', 'turn.completed', 'error'].includes(evt.event)) {
                dispatch({ type: 'event', evt });
              } else if (evt.event === 'message') {
                // Compatibility with servers that still return the safe final JSON response.
                const result = evt.data || {};
                dispatch({ type: 'event', evt: { event: 'assistant.delta', data: { text: result.text || '' } } });
                dispatch({
                  type: 'event',
                  evt: { event: 'turn.completed', data: { messageId: result.messageId, tokensIn: result.tokensIn, tokensOut: result.tokensOut } }
                });
              }
            },
            onEnd: (error, options = {}) => {
              if (current === generation.current) {
                dispatch({ type: 'end', options: { stopped: !!options.aborted, error: error ? { message: errorMessage(t, error) } : null } });
                controller.current = null;
              }
              resolve();
            }
          }
        );
      });
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
        <div className="standalone-chat-identity">
          {metadata?.logoUrl && !logoFailed ? (
            <img className="standalone-chat-logo" src={metadata.logoUrl} alt="" referrerPolicy="no-referrer" onError={() => setLogoFailed(true)} />
          ) : (
            <span className="standalone-chat-logo standalone-chat-logo-fallback" aria-hidden="true">
              <ChatIcon size={28} />
            </span>
          )}
          <h1>{metadata?.title || metadata?.displayName || metadata?.name || t('standaloneChat.title')}</h1>
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
            streaming={state.streaming || remoteStreaming}
            draft={draft}
            onDraftChange={setDraft}
            onSend={send}
            onStop={stop}
            disabled={starting}
            emptyHint={t('standaloneChat.empty')}
            compactComposer
            onFeedback={session ? submitFeedback : undefined}
          />
          <p className="standalone-chat-disclaimer">{t('standaloneChat.disclaimer')}</p>
        </>
      )}
    </main>
  );
}
