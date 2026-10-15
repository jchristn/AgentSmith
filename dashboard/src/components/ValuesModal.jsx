import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import Modal from './Modal';
import { SecretValuesForm } from './SecretField';
import { tip } from './Tooltip';
import { validateSecretValues } from '../utils/secrets';
import { errorMessage } from '../utils/errors';

/**
 * Replace a secret's values (PUT /secrets/{id}/values). Only the fields the operator fills in are sent; empty fields
 * keep their stored value, so rotating just the password of a username and password pair is one field.
 */
export default function ValuesModal({ title, kind, onSubmit, onClose }) {
  const { t } = useTranslation();
  const [values, setValues] = useState({});
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);

  const submit = async () => {
    const problems = validateSecretValues(kind, values, { partial: true });
    if (problems._form) {
      setError(t('resources.secrets.errors.noValues'));
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await onSubmit(values);
      setValues({});
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
      size="large"
      onClose={onClose}
      title={title}
      testId="secret-values"
      footer={
        <>
          <button type="button" className="button-secondary" onClick={onClose} disabled={busy} {...tip(t('tips.modal.cancelForm'))}>
            {t('common.actions.cancel')}
          </button>
          <button type="button" className="button-primary" onClick={submit} disabled={busy} data-testid="values-save" {...tip(t('tips.resources.secrets.saveValue'))}>
            {t('common.actions.save')}
          </button>
        </>
      }
    >
      {error && (
        <div className="error-banner" role="alert">
          {error}
        </div>
      )}
      <SecretValuesForm kind={kind} values={values} onChange={setValues} replace idPrefix="secret-replace" disabled={busy} />
    </Modal>
  );
}
