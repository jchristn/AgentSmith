import CopyButton from './CopyButton';
import { truncate } from '../i18n/formatters';
import { tip } from './Tooltip';

/** A monospace, non-wrapping ID with an inline copy button. */
export default function CopyableId({ value, max = 20, copyTip }) {
  if (!value) return <span className="text-muted">-</span>;
  return (
    <span className="copyable-id">
      <code className="cell-id" {...tip(value)}>
        {truncate(value, max)}
      </code>
      <CopyButton value={value} tipText={copyTip} />
    </span>
  );
}
