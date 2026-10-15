import { LinkIcon } from './Icons';
import { tip } from './Tooltip';

/**
 * Deep link into Grafana (P13.15), opened in a new tab. Renders nothing when `href` is null, which is what the
 * builders in utils/grafana.js return when Grafana or the needed datasource or dashboard is not configured.
 */
export default function GrafanaLink({ href, label, tipText, testId, size = 'sm' }) {
  if (!href) return null;
  return (
    <a className={`button button-secondary${size === 'sm' ? ' button-sm' : ''}`} href={href} target="_blank" rel="noopener noreferrer" data-testid={testId} {...tip(tipText)}>
      <LinkIcon size={14} /> {label}
    </a>
  );
}
