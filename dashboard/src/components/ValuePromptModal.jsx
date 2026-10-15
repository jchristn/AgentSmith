import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import Modal from './Modal';
import { FormField } from './FormFields';
import { tip } from './Tooltip';
import { errorMessage } from '../utils/errors';

/** Small modal that collects one value (optionally secret) and submits it. */
export default function ValuePromptModal({ title, label, tip: fieldTip, saveTip, secret = false, multiline = false, onSubmit, onClose }) {
  const { t } = useTranslation();
  const [value, setValue] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);

  const submit = async () => {
    if (!value) {
      setError(t('common.errors.required'));
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await onSubmit(value);
      onClose();
    } catch (e) {
      setError(errorMessage(t, e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal
      open
      size="small"
      onClose={onClose}
      title={title}
      footer={
        <>
          <button type="button" className="button-secondary" onClick={onClose} disabled={busy} {...tip(t('tips.modal.cancelForm'))}>
            {t('common.actions.cancel')}
          </button>
          <button type="button" className="button-primary" onClick={submit} disabled={busy} {...tip(saveTip || t('tips.modal.saveValue'))}>
            {t('common.actions.save')}
          </button>
        </>
      }
    >
      <FormField id="value-prompt" field={{ name: 'value', type: secret ? 'password' : multiline ? 'textarea' : 'text', label, required: true, tip: fieldTip }} value={value} onChange={setValue} error={error} disabled={busy} />
    </Modal>
  );
}
