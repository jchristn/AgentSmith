import { statusClass, methodClass } from '../i18n/formatters';
import { useTranslation } from 'react-i18next';
import { tip as tipProps } from './Tooltip';
import { EyeIcon, PencilIcon, AlertTriangleIcon } from './Icons';
import { effectTone } from '../utils/constants';
import { enumTip } from '../utils/tips';

/** HTTP status-code pill. */
export function StatusBadge({ code, tip }) {
  return (
    <span className={statusClass(code)} {...tipProps(tip)}>
      {code}
    </span>
  );
}

/** HTTP method pill. */
export function MethodPill({ method, tip }) {
  return (
    <span className={methodClass(method)} {...tipProps(tip)}>
      {method}
    </span>
  );
}

/** Generic labeled pill with a semantic tone. */
export function Badge({ tone = 'neutral', children, tip }) {
  return (
    <span className={`pill pill-${tone}`} {...tipProps(tip)}>
      {children}
    </span>
  );
}

/** Boolean active/inactive pill. */
export function ActiveBadge({ active, activeLabel, inactiveLabel, tip }) {
  return (
    <span className={`pill ${active ? 'pill-success' : 'pill-neutral'}`} {...tipProps(tip)}>
      {active ? activeLabel : inactiveLabel}
    </span>
  );
}

const EFFECT_ICONS = { Read: EyeIcon, Write: PencilIcon, Destructive: AlertTriangleIcon };

/**
 * Tool call effect badge: Read (neutral), Write (amber), Destructive (red). Each carries an icon and translated text,
 * so the meaning never depends on color alone (plan section 18.4).
 */
export function EffectBadge({ effect, tip }) {
  const { t } = useTranslation();
  if (!effect) return <span className="text-muted">-</span>;
  const Icon = EFFECT_ICONS[effect] || EyeIcon;
  return (
    <span className={`pill pill-${effectTone(effect)} effect-badge`} data-effect={effect} {...tipProps(tip || enumTip(t, 'effect', effect))}>
      <Icon size={12} />
      <span>{t(`enums.effect.${effect}`, { defaultValue: effect })}</span>
    </span>
  );
}

/** An enum value as a translated pill with a semantic tone and its enum tooltip. */
export function EnumBadge({ group, value, tone = 'neutral', tip }) {
  const { t } = useTranslation();
  if (!value) return <span className="text-muted">-</span>;
  return (
    <span className={`pill pill-${tone}`} data-value={value} {...tipProps(tip || enumTip(t, group, value))}>
      {t(`enums.${group}.${value}`, { defaultValue: value })}
    </span>
  );
}
