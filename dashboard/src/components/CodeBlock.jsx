import CopyButton from './CopyButton';
import { useTranslation } from 'react-i18next';

/** Pretty-print a value (object or JSON string) or return a raw string. */
function format(value, prettyJson) {
  if (value === null || value === undefined || value === '') return '';
  if (typeof value === 'string') {
    if (!prettyJson) return value;
    try {
      return JSON.stringify(JSON.parse(value), null, 2);
    } catch {
      return value;
    }
  }
  try {
    return JSON.stringify(value, null, 2);
  } catch {
    return String(value);
  }
}

/** Read-only code block with a copy button. */
export default function CodeBlock({ value, prettyJson = false, emptyText = '-' }) {
  const { t } = useTranslation();
  const text = format(value, prettyJson);
  if (!text) return <div className="text-muted" style={{ fontSize: 'var(--font-size-sm)' }}>{emptyText}</div>;
  return (
    <div className="code-block">
      <div className="code-block-toolbar">
        <CopyButton value={text} tipText={t('tips.copy.block')} />
      </div>
      <pre>{text}</pre>
    </div>
  );
}
