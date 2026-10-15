import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { copyToClipboard } from '../utils/clipboard';
import { CopyIcon, CheckIcon } from './Icons';
import { tip } from './Tooltip';

/** Reusable copy-to-clipboard icon button with a checkmark success state. */
export default function CopyButton({ value, size = 14, className = '', title, tipText }) {
  const { t } = useTranslation();
  const [copied, setCopied] = useState(false);

  const handle = async (e) => {
    e.stopPropagation();
    const ok = await copyToClipboard(value);
    if (ok) {
      setCopied(true);
      setTimeout(() => setCopied(false), 1600);
    }
  };

  const label = copied ? t('common.actions.copied') : title || t('common.actions.copy');

  return (
    <button
      type="button"
      className={`copy-btn ${copied ? 'is-copied' : ''} ${className}`}
      onClick={handle}
      aria-label={label}
      {...tip(copied ? t('tips.copy.done') : tipText || t('tips.copy.value'))}
    >
      {copied ? <CheckIcon size={size} /> : <CopyIcon size={size} />}
    </button>
  );
}
