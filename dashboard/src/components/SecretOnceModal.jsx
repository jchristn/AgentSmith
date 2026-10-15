import { useTranslation } from 'react-i18next';
import Modal from './Modal';
import CopyButton from './CopyButton';
import { tip } from './Tooltip';
import CodeBlock from './CodeBlock';

/** Shows a newly issued secret exactly once, with copy controls, a warning, and an optional MCP configuration snippet. */
export default function SecretOnceModal({ accessKey, secretKey, snippet, onClose }) {
  const { t } = useTranslation();
  return (
    <Modal
      open
      onClose={onClose}
      title={t('components.secretOnce.title')}
      footer={
        <button type="button" className="button-primary" onClick={onClose} {...tip(t('tips.secretOnce.done'))}>
          {t('components.secretOnce.done')}
        </button>
      }
    >
      <div className="warning-banner">{t('components.secretOnce.warning')}</div>
      {accessKey && (
        <div className="field">
          <label {...tip(t('tips.secretOnce.accessKey'))}>{t('components.secretOnce.accessKey')}</label>
          <div className="secret-row">
            <code>{accessKey}</code>
            <CopyButton value={accessKey} />
          </div>
        </div>
      )}
      <div className="field">
        <label {...tip(t('tips.secretOnce.secretKey'))}>{t('components.secretOnce.secretKey')}</label>
        <div className="secret-row">
          <code data-testid="secret-value">{secretKey || '-'}</code>
          {secretKey && <CopyButton value={secretKey} />}
        </div>
      </div>
      {snippet && (
        <div className="field">
          <label {...tip(t('tips.secretOnce.snippet'))}>{t('components.secretOnce.snippet')}</label>
          <p className="field-hint">{t('components.secretOnce.snippetHint')}</p>
          <CodeBlock value={snippet} />
        </div>
      )}
    </Modal>
  );
}
