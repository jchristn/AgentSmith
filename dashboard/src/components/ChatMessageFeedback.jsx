import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import Modal from './Modal';
import { tip } from './Tooltip';
import './ChatMessageFeedback.css';

function FeedbackIcon({ negative = false, comment = false }) {
  return (
    <svg
      width="16"
      height="16"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.8"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      {comment ? (
        <path d="M21 11.5a8.5 8.5 0 0 1-8.5 8.5H4l-3 3V11.5a10 10 0 0 1 20 0Z" />
      ) : (
        <g transform={negative ? 'rotate(180 12 12)' : undefined}>
          <path d="M7 10 11 2c3 0 3 2 2 7h6a2 2 0 0 1 2 2l-2 9H7Z" />
          <path d="M3 10h4v11H3Z" />
        </g>
      )}
    </svg>
  );
}

/** Per-answer rating and optional written feedback, saved through the owning chat session. */
export default function ChatMessageFeedback({ messageId, feedback, onFeedback }) {
  const { t } = useTranslation();
  const initial = feedback || { rating: null, comment: '' };
  const [saved, setSaved] = useState(initial);
  const [draft, setDraft] = useState(initial);
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(false);
  const [submitted, setSubmitted] = useState(false);
  const key = (name) => t(`components.chat.feedback.${name}`);
  if (!onFeedback && !saved.rating && !saved.comment) return null;

  const save = async (value) => {
    if (busy || !onFeedback) return;
    setBusy(true);
    setError(false);
    try {
      const result = await onFeedback(messageId, value);
      setSaved(result || value);
      setSubmitted(true);
      setOpen(false);
    } catch {
      setError(true);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="chat-feedback">
      {onFeedback ? (
        <div className="chat-feedback-actions" role="group" aria-label={key('title')}>
          {['Positive', 'Negative'].map((rating) => (
            <button
              key={rating}
              type="button"
              className={`icon-button${saved.rating === rating ? ' selected' : ''}`}
              disabled={busy}
              aria-label={key(rating === 'Positive' ? 'helpful' : 'unhelpful')}
              aria-pressed={saved.rating === rating}
              onClick={() => save({ ...saved, rating: saved.rating === rating ? null : rating })}
              {...tip(key(rating === 'Positive' ? 'helpful' : 'unhelpful'))}
            >
              <FeedbackIcon negative={rating === 'Negative'} />
            </button>
          ))}
          <button
            type="button"
            className={`icon-button${saved.comment ? ' selected' : ''}`}
            disabled={busy}
            aria-label={key('comment')}
            onClick={() => {
              setDraft(saved);
              setError(false);
              setOpen(true);
            }}
            {...tip(key('comment'))}
          >
            <FeedbackIcon comment />
          </button>
          {submitted && !error && (
            <span className="field-hint" role="status">
              {key('saved')}
            </span>
          )}
        </div>
      ) : (
        saved.rating && (
          <div className="field-hint">
            {key('title')}: {key(saved.rating === 'Positive' ? 'positive' : 'negative')}
          </div>
        )
      )}
      {saved.comment && <p className="chat-feedback-comment">{saved.comment}</p>}
      {error && !open && (
        <div role="alert" className="error-banner">
          {key('failed')}
        </div>
      )}
      <Modal
        open={open}
        onClose={() => {
          if (!busy) setOpen(false);
        }}
        blocking={busy}
        title={key('title')}
        size="small"
        footer={
          <>
            <button type="button" className="button-secondary" disabled={busy} onClick={() => setOpen(false)}>
              {t('common.actions.cancel')}
            </button>
            <button type="button" className="button-primary" disabled={busy} onClick={() => save(draft)}>
              {key('save')}
            </button>
          </>
        }
      >
        <div className="chat-feedback-form">
          <label>
            {key('rating')}
            <select value={draft.rating || ''} onChange={(event) => setDraft({ ...draft, rating: event.target.value || null })} disabled={busy}>
              <option value="">{key('noRating')}</option>
              <option value="Positive">{key('positive')}</option>
              <option value="Negative">{key('negative')}</option>
            </select>
          </label>
          <label>
            {key('commentLabel')}
            <textarea
              rows={4}
              maxLength={4000}
              value={draft.comment}
              placeholder={key('commentPlaceholder')}
              disabled={busy}
              onChange={(event) => setDraft({ ...draft, comment: event.target.value })}
            />
          </label>
          {error && (
            <div role="alert" className="error-banner">
              {key('failed')}
            </div>
          )}
        </div>
      </Modal>
    </div>
  );
}
