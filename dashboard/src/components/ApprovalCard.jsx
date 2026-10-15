import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import CodeBlock from './CodeBlock';
import { EffectBadge, EnumBadge } from './StatusBadge';
import { tip } from './Tooltip';
import { ShieldCheckIcon } from './Icons';
import { approvalStatusTone } from '../utils/constants';
import { formatDateTime } from '../i18n/formatters';

/**
 * A gated tool call waiting for a person (plan sections 11.4 and 18.4): the summary, the effect with the
 * classifier's reason, the exact command or statement, and Approve and Reject with an optional note. Once decided it
 * shows the outcome instead of the buttons. Used inline in chat and in the Approvals inbox.
 */
export default function ApprovalCard({ approval, onApprove, onReject, busy = false, compact = false }) {
  const { t, i18n } = useTranslation();
  const [note, setNote] = useState('');
  if (!approval) return null;
  const pending = (approval.status || 'Pending') === 'Pending';
  const noteId = `approval-note-${approval.id}`;

  return (
    <section className={`approval-card ${pending ? 'pending' : 'decided'} ${compact ? 'compact' : ''}`} data-testid="approval-card" data-status={approval.status || 'Pending'} aria-label={t('components.approvalCard.title')}>
      <header className="approval-card-head">
        <ShieldCheckIcon size={16} />
        <strong>{t('components.approvalCard.title')}</strong>
        <EffectBadge effect={approval.effect} />
        <EnumBadge group="approvalStatus" value={approval.status || 'Pending'} tone={approvalStatusTone(approval.status || 'Pending')} />
      </header>
      {approval.summary && <p className="approval-summary">{approval.summary}</p>}
      {approval.resourceAlias && (
        <div className="approval-meta">
          <span className="text-muted">{t('components.approvalCard.resource')}</span> <code>{approval.resourceAlias}</code>
          {approval.toolName && (
            <>
              {' '}
              <span className="text-muted">{t('components.approvalCard.tool')}</span> <code>{approval.toolName}</code>
            </>
          )}
        </div>
      )}
      {approval.effectReason && <div className="field-hint">{t('components.approvalCard.reason', { reason: approval.effectReason })}</div>}
      {approval.command && <CodeBlock value={approval.command} />}
      {approval.expiresUtc && pending && <div className="field-hint">{t('components.approvalCard.expires', { time: formatDateTime(approval.expiresUtc, i18n.language) })}</div>}
      {pending && (onApprove || onReject) ? (
        <div className="approval-actions">
          <label htmlFor={noteId} className="sr-only">
            {t('components.approvalCard.note')}
          </label>
          <input id={noteId} type="text" value={note} placeholder={t('components.approvalCard.notePlaceholder')} onChange={(e) => setNote(e.target.value)} disabled={busy} {...tip(t('tips.approvalCard.note'))} />
          <button type="button" className="button-danger button-sm" onClick={() => onReject?.(note)} disabled={busy} data-testid="approval-reject" {...tip(t('tips.approvalCard.reject'))}>
            {t('components.approvalCard.reject')}
          </button>
          <button type="button" className="button-primary button-sm" onClick={() => onApprove?.(note)} disabled={busy} data-testid="approval-approve" {...tip(t('tips.approvalCard.approve'))}>
            {t('components.approvalCard.approve')}
          </button>
        </div>
      ) : (
        !pending && (
          <div className="field-hint" data-testid="approval-outcome">
            {t('components.approvalCard.decided', { status: t(`enums.approvalStatus.${approval.status}`, { defaultValue: approval.status }) })}
            {approval.decidedBy ? ` ${t('components.approvalCard.by', { name: approval.decidedBy })}` : ''}
            {approval.note ? ` ${t('components.approvalCard.withNote', { note: approval.note })}` : ''}
          </div>
        )
      )}
    </section>
  );
}
