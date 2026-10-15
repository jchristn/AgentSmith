import { useEffect, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import MarkdownView from './MarkdownView';
import ToolActivityRow from './ToolActivityRow';
import ApprovalCard from './ApprovalCard';
import { tip } from './Tooltip';
import { SendIcon, StopIcon, ChatIcon } from './Icons';
import { formatDuration, formatNumber } from '../i18n/formatters';

/** One transcript message: thinking (collapsed), then text, tool rows, and approval cards in stream order. */
function ChatMessage({ message, onApprove, onReject, approvalBusy }) {
  const { t, i18n } = useTranslation();
  const isUser = message.role === 'user';
  const usage = message.usage || {};
  return (
    <article className={`chat-message chat-${message.role}`} data-testid={`chat-message-${message.role}`} data-status={message.status}>
      <div className="chat-message-author">{isUser ? t('components.chat.you') : t('components.chat.assistant')}</div>
      {message.thinking && (
        <details className="chat-thinking">
          <summary {...tip(t('tips.chat.thinking'))}>{t('components.chat.thinking')}</summary>
          <pre className="monospace">{message.thinking}</pre>
        </details>
      )}
      <div className="chat-message-body">
        {message.parts.map((part, i) => {
          if (part.kind === 'text')
            return isUser ? (
              <p key={i} className="chat-user-text">
                {part.text}
              </p>
            ) : (
              <MarkdownView key={i} text={part.text} />
            );
          if (part.kind === 'tool') return <ToolActivityRow key={part.tool.id || i} tool={part.tool} />;
          if (part.kind === 'approval')
            return (
              <ApprovalCard
                key={part.approval.id || i}
                approval={part.approval}
                busy={approvalBusy === part.approval.id}
                onApprove={onApprove ? (note) => onApprove(part.approval.id, note) : undefined}
                onReject={onReject ? (note) => onReject(part.approval.id, note) : undefined}
                compact
              />
            );
          return null;
        })}
        {message.status === 'streaming' && message.parts.length === 0 && (
          <div className="chat-waiting" role="status">
            <span className="pulse-dot" aria-hidden="true" /> {t('components.chat.waiting')}
          </div>
        )}
        {message.status === 'stopped' && <div className="field-hint">{t('components.chat.stopped')}</div>}
        {message.status === 'error' && <div className="error-banner">{message.error?.message || t('components.chat.streamError')}</div>}
      </div>
      {!isUser && message.status === 'done' && (usage.timeToFirstTokenMs != null || usage.outputTokens != null) && (
        <div className="chat-usage text-muted" data-testid="chat-usage">
          {usage.timeToFirstTokenMs != null && (
            <span {...tip(t('tips.chat.ttft'))}>{t('components.chat.ttft', { value: formatDuration(usage.timeToFirstTokenMs, i18n.language) })}</span>
          )}
          {usage.durationMs != null && (
            <span {...tip(t('tips.chat.duration'))}>{t('components.chat.duration', { value: formatDuration(usage.durationMs, i18n.language) })}</span>
          )}
          {usage.outputTokens != null && (
            <span {...tip(t('tips.chat.tokens'))}>
              {t('components.chat.tokens', {
                input: formatNumber(usage.inputTokens ?? 0, {}, i18n.language),
                output: formatNumber(usage.outputTokens, {}, i18n.language)
              })}
            </span>
          )}
        </div>
      )}
    </article>
  );
}

/**
 * Chat transcript and composer (P8.14). Enter sends, Shift+Enter inserts a newline, and Stop aborts the turn while
 * it streams. The draft is owned by the parent so it can be remembered per conversation.
 *
 * Props: messages (from utils/chatStream), streaming, draft, onDraftChange, onSend(text), onStop(), onApprove(id,
 * note), onReject(id, note), approvalBusy (approval id being decided), disabled, disabledReason, emptyHint.
 */
export default function ChatPanel({
  messages = [],
  streaming = false,
  draft = '',
  onDraftChange,
  onSend,
  onStop,
  onApprove,
  onReject,
  approvalBusy = null,
  disabled = false,
  disabledReason,
  emptyHint
}) {
  const { t } = useTranslation();
  const endRef = useRef(null);

  useEffect(() => {
    endRef.current?.scrollIntoView?.({ block: 'end' });
  }, [messages]);

  const send = () => {
    const text = draft.trim();
    if (!text || streaming || disabled) return;
    onSend?.(text);
  };

  return (
    <div className="chat-panel" data-testid="chat-panel">
      <div className="chat-transcript" role="log" aria-live="polite" aria-label={t('components.chat.transcript')}>
        {messages.length === 0 ? (
          <div className="chat-empty">
            <ChatIcon size={28} />
            <p>{emptyHint || t('components.chat.empty')}</p>
          </div>
        ) : (
          messages.map((m) => <ChatMessage key={m.id} message={m} onApprove={onApprove} onReject={onReject} approvalBusy={approvalBusy} />)
        )}
        <div ref={endRef} />
      </div>
      <form
        className="chat-composer"
        onSubmit={(e) => {
          e.preventDefault();
          send();
        }}
      >
        {disabled && disabledReason && <div className="field-hint chat-disabled">{disabledReason}</div>}
        <label htmlFor="chat-input" className="sr-only">
          {t('components.chat.message')}
        </label>
        <textarea
          id="chat-input"
          rows={3}
          value={draft}
          disabled={disabled}
          placeholder={t('components.chat.placeholder')}
          onChange={(e) => onDraftChange?.(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter' && !e.shiftKey && !e.nativeEvent.isComposing) {
              e.preventDefault();
              send();
            }
          }}
          data-testid="chat-input"
          {...tip(t('tips.chat.input'))}
        />
        {streaming ? (
          <button type="button" className="button-danger" onClick={onStop} data-testid="chat-stop" {...tip(t('tips.chat.stop'))}>
            <StopIcon size={14} /> {t('components.chat.stop')}
          </button>
        ) : (
          <button type="submit" className="button-primary" disabled={disabled || !draft.trim()} data-testid="chat-send" {...tip(t('tips.chat.send'))}>
            <SendIcon size={14} /> {t('components.chat.send')}
          </button>
        )}
      </form>
    </div>
  );
}
