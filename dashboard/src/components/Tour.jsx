import { useState, useLayoutEffect } from 'react';
import { createPortal } from 'react-dom';
import { useTranslation } from 'react-i18next';
import { useApp } from '../context/AppContext';
import { STORAGE_KEYS } from '../utils/constants';
import { tip } from './Tooltip';
import { popupStartLeft } from '../utils/direction';

export const TOUR_STEPS = [
  { id: 'sidebar', selector: '[data-tour="sidebar"]' },
  { id: 'topbar', selector: '[data-tour="topbar"]' },
  { id: 'kpis', selector: '[data-tour="kpis"]' },
  { id: 'chat', selector: '[data-nav="chat"]' },
  { id: 'connect', selector: '[data-nav="connections"]' },
  { id: 'profiles', selector: '[data-nav="agents"]' },
  { id: 'approvals', selector: '[data-testid="topbar-approvals"]' },
  { id: 'observe', selector: '[data-nav="activity"]' }
];
const STEPS = TOUR_STEPS;

/** Guided tour (P8.18) highlighting the main areas. Completion is persisted; replay from the sidebar footer. */
export default function Tour() {
  const { t } = useTranslation();
  const { tourOpen, setTourOpen } = useApp();
  const [index, setIndex] = useState(0);
  const [rect, setRect] = useState(null);

  useLayoutEffect(() => {
    if (!tourOpen) return undefined;
    const update = () => {
      const el = document.querySelector(STEPS[index].selector);
      setRect(el ? el.getBoundingClientRect() : null);
    };
    update();
    window.addEventListener('resize', update);
    return () => window.removeEventListener('resize', update);
  }, [tourOpen, index]);

  if (!tourOpen) return null;
  const finish = () => {
    localStorage.setItem(STORAGE_KEYS.tourDone, 'true');
    setTourOpen(false);
    setIndex(0);
  };
  const step = STEPS[index];
  const top = rect ? Math.min(window.innerHeight - 200, rect.bottom + 12) : window.innerHeight / 2 - 80;
  const left = rect ? popupStartLeft(rect, Math.min(340, window.innerWidth - 32), 12) : window.innerWidth / 2 - 160;

  return createPortal(
    <div className="tour-layer" role="dialog" aria-modal="true" aria-label={t('tour.title')}>
      {rect && <div className="tour-highlight" style={{ top: rect.top - 4, left: rect.left - 4, width: rect.width + 8, height: rect.height + 8 }} />}
      <div className="tour-card" style={{ top, left }}>
        <div className="tour-progress">{t('wizard.stepOf', { step: index + 1, total: STEPS.length })}</div>
        <h3>{t(`tour.steps.${step.id}.title`)}</h3>
        <p>{t(`tour.steps.${step.id}.body`)}</p>
        <div className="tour-actions">
          <button type="button" className="button-ghost button-sm" onClick={finish} {...tip(t('tips.tour.skip'))}>
            {t('tour.skip')}
          </button>
          {index > 0 && (
            <button type="button" className="button-secondary button-sm" onClick={() => setIndex((i) => i - 1)} {...tip(t('tips.tour.back'))}>
              {t('common.actions.back')}
            </button>
          )}
          {index < STEPS.length - 1 ? (
            <button type="button" className="button-primary button-sm" onClick={() => setIndex((i) => i + 1)} {...tip(t('tips.tour.next'))}>
              {t('common.actions.next')}
            </button>
          ) : (
            <button type="button" className="button-primary button-sm" onClick={finish} {...tip(t('tips.tour.done'))}>
              {t('tour.done')}
            </button>
          )}
        </div>
      </div>
    </div>,
    document.body
  );
}
