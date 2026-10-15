import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { InfoIcon, XIcon } from './Icons';
import { tip } from './Tooltip';
import { STORAGE_KEYS } from '../utils/constants';

const PARAGRAPHS = ['p1', 'p2', 'p3', 'p4'];

function readHidden() {
  try {
    return JSON.parse(localStorage.getItem(STORAGE_KEYS.explainers) || '{}') || {};
  } catch {
    return {};
  }
}

function writeHidden(topic, hidden) {
  try {
    const all = readHidden();
    if (hidden) all[topic] = true;
    else delete all[topic];
    localStorage.setItem(STORAGE_KEYS.explainers, JSON.stringify(all));
  } catch {
    /* ignore */
  }
}

/**
 * "How this relates" info panel shown at the top of a page or tab. Text lives in `explainers.<topic>` (title plus
 * paragraphs p1 to p4). Dismissing it collapses it to a small "About ..." button that reopens it; the choice is
 * remembered per topic in localStorage.
 */
export default function Explainer({ topic, diagram = null }) {
  const { t, i18n } = useTranslation();
  const [hidden, setHidden] = useState(() => !!readHidden()[topic]);
  const title = t(`explainers.${topic}.title`);

  const toggle = (next) => {
    setHidden(next);
    writeHidden(topic, next);
  };

  if (hidden) {
    return (
      <button type="button" className="button-ghost button-sm explainer-reopen" onClick={() => toggle(false)} data-testid={`explainer-open-${topic}`} {...tip(t('tips.explainer.reopen', { topic: title }))}>
        <InfoIcon size={14} /> {t('components.explainer.about', { topic: title })}
      </button>
    );
  }

  return (
    <section className="explainer" aria-label={title} data-testid={`explainer-${topic}`}>
      <span className="explainer-icon" aria-hidden="true">
        <InfoIcon />
      </span>
      <div className="explainer-body">
        <div className="explainer-title">{title}</div>
        {diagram}
        {PARAGRAPHS.filter((p) => i18n.exists(`explainers.${topic}.${p}`)).map((p) => (
          <p key={p}>{t(`explainers.${topic}.${p}`)}</p>
        ))}
      </div>
      <div className="explainer-actions">
        <button type="button" className="icon-button" onClick={() => toggle(true)} aria-label={t('components.explainer.dismiss')} data-testid={`explainer-dismiss-${topic}`} {...tip(t('tips.explainer.dismiss'))}>
          <XIcon size={16} />
        </button>
      </div>
    </section>
  );
}

/** Small diagram: secrets feed resources, bindings attach resources to a profile, and every surface reaches the profile. */
export function ProfileRelationDiagram() {
  const { t } = useTranslation();
  const node = (key, primary = false) => (
    <div className={`relation-node ${primary ? 'is-primary' : ''}`} {...tip(t(`explainers.diagram.${key}.tip`))}>
      <strong>{t(`explainers.diagram.${key}.title`)}</strong>
      <span>{t(`explainers.diagram.${key}.body`)}</span>
    </div>
  );
  return (
    <div className="relation-diagram" role="img" aria-label={t('explainers.diagram.label')} data-testid="relation-diagram">
      {node('secret')}
      <span className="relation-arrow" aria-hidden="true">→</span>
      {node('resource')}
      <span className="relation-arrow" aria-hidden="true">→</span>
      {node('binding')}
      <span className="relation-arrow" aria-hidden="true">→</span>
      {node('profile', true)}
      <span className="relation-arrow" aria-hidden="true">←</span>
      {node('surfaces')}
    </div>
  );
}
