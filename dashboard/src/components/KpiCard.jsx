import { tip as tipProps } from './Tooltip';

/** KPI card, clickable when `onClick` is set. `tip` explains what the number counts and where a click goes. */
export default function KpiCard({ label, value, note, tone, onClick, testId, tip }) {
  const Tag = onClick ? 'button' : 'div';
  return (
    <Tag type={onClick ? 'button' : undefined} className={`kpi-card ${onClick ? 'kpi-clickable' : ''} ${tone ? `kpi-${tone}` : ''}`} onClick={onClick} data-testid={testId} {...tipProps(tip)}>
      <div className="kpi-label">{label}</div>
      <div className="kpi-value">{value}</div>
      {note && <div className="kpi-note">{note}</div>}
    </Tag>
  );
}
