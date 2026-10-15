import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import CodeBlock from './CodeBlock';
import CopyButton from './CopyButton';
import { tip } from './Tooltip';
import { renderMarkdown } from '../utils/markdown';

/**
 * Model output rendered as sanitized GitHub-flavored Markdown with inline HTML (P5.52). Links open in a new tab.
 * With `rawToggle`, a toolbar switches between the rendered view and the raw text, and copies the raw text.
 */
export default function MarkdownView({ text, rawToggle = false, emptyText = '-', testId, className = '' }) {
  const { t } = useTranslation();
  const [raw, setRaw] = useState(false);
  const html = useMemo(() => renderMarkdown(text, { linkTip: (href) => t('tips.markdown.link', { href }), headerTip: (name) => t('tips.markdown.column', { name }) }), [text, t]);
  if (!text) return <div className="text-muted" style={{ fontSize: 'var(--font-size-sm)' }}>{emptyText}</div>;
  return (
    <div className={`markdown-view ${className}`.trim()} data-testid={testId}>
      {rawToggle && (
        <div className="markdown-toolbar">
          <button type="button" className="button-secondary button-sm" aria-pressed={raw} onClick={() => setRaw(!raw)} data-testid="markdown-raw-toggle" {...tip(raw ? t('tips.markdown.showRendered') : t('tips.markdown.showRaw'))}>
            {raw ? t('views.markdown.showRendered') : t('views.markdown.showRaw')}
          </button>
          {!raw && <CopyButton value={text} tipText={t('tips.markdown.copy')} />}
        </div>
      )}
      {raw ? <CodeBlock value={text} /> : <div className="markdown-body" dangerouslySetInnerHTML={{ __html: html }} />}
    </div>
  );
}
