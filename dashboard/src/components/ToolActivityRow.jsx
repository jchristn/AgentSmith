import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import CodeBlock from './CodeBlock';
import { EffectBadge } from './StatusBadge';
import { tip } from './Tooltip';
import { ChevronDownIcon, ChevronRightIcon, TerminalIcon } from './Icons';
import { formatDuration } from '../i18n/formatters';
import { toolStatusTone } from '../utils/constants';

const LIVE_STATUSES = { proposed: 'info', running: 'info', awaitingApproval: 'warning' };

/**
 * One tool call inline in a chat transcript (plan section 18.4): the tool, the resource alias, the effect badge, the
 * status, and the duration. Expanding it shows the redacted arguments and the output preview (or the error).
 */
export default function ToolActivityRow({ tool, defaultOpen = false }) {
  const { t, i18n } = useTranslation();
  const [open, setOpen] = useState(defaultOpen);
  if (!tool) return null;
  const status = tool.status || 'proposed';
  const tone = LIVE_STATUSES[status] || toolStatusTone(status);
  const statusLabel = t(`components.toolActivity.status.${status}`, { defaultValue: t(`enums.toolStatus.${status}`, { defaultValue: status }) });
  const panelId = `tool-${tool.id || tool.toolName}-detail`;

  return (
    <div className={`tool-row tool-${status}`} data-testid="tool-row" data-status={status}>
      <button
        type="button"
        className="tool-row-head"
        aria-expanded={open}
        aria-controls={panelId}
        onClick={() => setOpen((v) => !v)}
        {...tip(t('tips.toolActivity.toggle'))}
      >
        {open ? <ChevronDownIcon size={14} /> : <ChevronRightIcon size={14} />}
        <TerminalIcon size={14} />
        <code className="tool-name">{tool.toolName || '-'}</code>
        {tool.resourceAlias && (
          <span className="tool-resource" {...tip(t('tips.toolActivity.resource'))}>
            {tool.resourceAlias}
          </span>
        )}
        <EffectBadge effect={tool.effect} />
        <span className={`pill pill-${tone}`} {...tip(t('tips.toolActivity.status'))}>
          {(status === 'running' || status === 'proposed') && <span className="pulse-dot" aria-hidden="true" />}
          {statusLabel}
        </span>
        {tool.durationMs !== null && tool.durationMs !== undefined && (
          <span className="tool-duration text-muted" {...tip(t('tips.toolActivity.duration'))}>
            {formatDuration(tool.durationMs, i18n.language)}
          </span>
        )}
      </button>
      {open && (
        <div className="tool-row-body" id={panelId}>
          {tool.effectReason && <div className="field-hint">{t('components.toolActivity.effectReason', { reason: tool.effectReason })}</div>}
          <div className="section-title">{t('components.toolActivity.arguments')}</div>
          <CodeBlock value={tool.arguments} prettyJson emptyText={t('components.toolActivity.noArguments')} />
          <div className="section-title">{tool.error ? t('components.toolActivity.error') : t('components.toolActivity.output')}</div>
          {tool.error ? (
            <div className="error-banner">{typeof tool.error === 'string' ? tool.error : tool.error.message || JSON.stringify(tool.error)}</div>
          ) : (
            <CodeBlock value={tool.outputPreview} emptyText={t('components.toolActivity.noOutput')} />
          )}
          {tool.outputTruncated && <div className="field-hint">{t('components.toolActivity.truncated')}</div>}
        </div>
      )}
    </div>
  );
}
