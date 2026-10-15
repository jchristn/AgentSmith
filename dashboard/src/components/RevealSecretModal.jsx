import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import Modal from './Modal';
import CopyButton from './CopyButton';
import { FormField } from './FormFields';
import { tip } from './Tooltip';
import { useAuth } from '../context/AuthContext';
import { errorMessage } from '../utils/errors';
import { REVEAL_VISIBLE_SECONDS } from '../utils/constants';
import { formatNumber } from '../i18n/formatters';

/** True when the server refused a reveal because the session needs a fresh re-authentication. */
export function needsReauth(error) {
  if (!error) return false;
  const code = String(error.code || '');
  if (/reauth/i.test(code)) return true;
  return error.status === 401 || (error.status === 403 && /reauth|recent/i.test(String(error.message || '')));
}

/**
 * Secret reveal (plan section 12.2, P8.32). The operator states a reason; if the session has not re-authenticated
 * within Secrets.RevealReauthMinutes the server refuses, and the modal asks for the password
 * (POST /v1.0/token/reauthenticate) and retries. Revealed values stay on screen for 30 seconds with copy buttons,
 * then are cleared from memory. Every reveal is audited by the server.
 */
export default function RevealSecretModal({ secret, onClose, visibleSeconds = REVEAL_VISIBLE_SECONDS }) {
  const { t, i18n } = useTranslation();
  const { apiClient } = useAuth();
  const [step, setStep] = useState('reason');
  const [reason, setReason] = useState('');
  const [password, setPassword] = useState('');
  const [values, setValues] = useState(null);
  const [remaining, setRemaining] = useState(visibleSeconds);
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (step !== 'shown') return undefined;
    setRemaining(visibleSeconds);
    const timer = setInterval(() => {
      setRemaining((r) => {
        if (r <= 1) {
          clearInterval(timer);
          setValues(null);
          setStep('hidden');
          return 0;
        }
        return r - 1;
      });
    }, 1000);
    return () => clearInterval(timer);
  }, [step, visibleSeconds]);

  // Values never outlive the modal.
  useEffect(() => () => setValues(null), []);

  const reveal = async () => {
    if (!reason.trim()) {
      setError(t('views.secrets.reveal.reasonRequired'));
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const result = await apiClient.revealSecret(secret.id, reason.trim());
      setValues(result?.values || result?.fields || (typeof result === 'object' ? result : { value: String(result ?? '') }));
      setStep('shown');
    } catch (e) {
      if (needsReauth(e)) setStep('reauth');
      else setError(errorMessage(t, e));
    } finally {
      setBusy(false);
    }
  };

  const reauthenticate = async () => {
    if (!password) {
      setError(t('common.errors.required'));
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await apiClient.reauthenticate(password);
      setPassword('');
      setStep('reason');
      await reveal();
    } catch (e) {
      setError(errorMessage(t, e));
      setBusy(false);
    }
  };

  const close = () => {
    setValues(null);
    onClose?.();
  };

  return (
    <Modal
      open
      onClose={close}
      size="medium"
      title={t('views.secrets.reveal.title', { name: secret?.name })}
      testId="reveal-modal"
      footer={
        <>
          <button type="button" className="button-secondary" onClick={close} disabled={busy} {...tip(t('tips.modal.close'))}>
            {t('common.actions.close')}
          </button>
          {step === 'reason' && (
            <button
              type="button"
              className="button-danger"
              onClick={reveal}
              disabled={busy}
              data-testid="reveal-submit"
              {...tip(t('tips.views.secrets.reveal.submit'))}
            >
              {t('views.secrets.reveal.submit')}
            </button>
          )}
          {step === 'reauth' && (
            <button
              type="button"
              className="button-primary"
              onClick={reauthenticate}
              disabled={busy}
              data-testid="reauth-submit"
              {...tip(t('tips.views.secrets.reveal.reauth'))}
            >
              {t('views.secrets.reveal.reauthSubmit')}
            </button>
          )}
          {step === 'hidden' && (
            <button type="button" className="button-secondary" onClick={() => setStep('reason')} {...tip(t('tips.views.secrets.reveal.again'))}>
              {t('views.secrets.reveal.again')}
            </button>
          )}
        </>
      }
    >
      <div className="warning-banner">{t('views.secrets.reveal.warning')}</div>
      {error && (
        <div className="error-banner" role="alert">
          {error}
        </div>
      )}
      {step === 'reason' && (
        <FormField
          id="reveal-reason"
          field={{
            name: 'reason',
            type: 'textarea',
            rows: 2,
            label: t('views.secrets.reveal.reason'),
            required: true,
            tip: t('tips.views.secrets.reveal.reason')
          }}
          value={reason}
          onChange={setReason}
          disabled={busy}
        />
      )}
      {step === 'reauth' && (
        <>
          <p>{t('views.secrets.reveal.reauthBody')}</p>
          <FormField
            id="reveal-password"
            field={{
              name: 'password',
              type: 'password',
              label: t('views.secrets.reveal.password'),
              required: true,
              tip: t('tips.views.secrets.reveal.password')
            }}
            value={password}
            onChange={setPassword}
            disabled={busy}
          />
        </>
      )}
      {step === 'shown' && values && (
        <div className="revealed-values" data-testid="revealed-values">
          <div className="field-hint" aria-live="polite">
            {t('views.secrets.reveal.hidesIn', { seconds: formatNumber(remaining, {}, i18n.language) })}
          </div>
          {Object.entries(values).map(([name, value]) => (
            <div className="field" key={name}>
              <label>{t(`components.secretField.fields.${name}`, { defaultValue: name })}</label>
              <div className="secret-row">
                <pre className="monospace revealed-value">{String(value ?? '')}</pre>
                <CopyButton value={String(value ?? '')} tipText={t('tips.views.secrets.reveal.copy')} />
              </div>
            </div>
          ))}
        </div>
      )}
      {step === 'hidden' && <p data-testid="reveal-hidden">{t('views.secrets.reveal.hidden')}</p>}
    </Modal>
  );
}
