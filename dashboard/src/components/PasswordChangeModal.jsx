import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import Modal from './Modal';
import { FormField } from './FormFields';
import { tip } from './Tooltip';
import { useAuth } from '../context/AuthContext';
import { useToast } from '../context/ToastContext';
import { errorMessage } from '../utils/errors';

/** Minimum password length the form enforces before the server checks its own policy. */
export const MIN_PASSWORD_LENGTH = 8;

/** Validate a new password pair. Returns { field: message key } or {}. */
export function validatePasswordChange({ currentPassword, newPassword, confirmPassword }) {
  const errors = {};
  if (!currentPassword) errors.currentPassword = 'common.errors.required';
  if (!newPassword || newPassword.length < MIN_PASSWORD_LENGTH) errors.newPassword = 'components.passwordChange.tooShort';
  else if (newPassword === currentPassword) errors.newPassword = 'components.passwordChange.sameAsCurrent';
  if (confirmPassword !== newPassword) errors.confirmPassword = 'components.passwordChange.mismatch';
  return errors;
}

/**
 * Forced password change after signing in with a seeded or reset password (plan section 17.3). It cannot be
 * dismissed; logging out is the only other way out.
 */
export default function PasswordChangeModal({ open }) {
  const { t } = useTranslation();
  const { apiClient, principal, passwordChanged, logout } = useAuth();
  const { addToast } = useToast();
  const [form, setForm] = useState({ currentPassword: '', newPassword: '', confirmPassword: '' });
  const [errors, setErrors] = useState({});
  const [busy, setBusy] = useState(false);

  const submit = async () => {
    const problems = validatePasswordChange(form);
    setErrors(Object.fromEntries(Object.entries(problems).map(([k, v]) => [k, t(v, { min: MIN_PASSWORD_LENGTH })])));
    if (Object.keys(problems).length) return;
    setBusy(true);
    try {
      await apiClient.changePassword(principal?.userId || principal?.principalId || 'me', {
        currentPassword: form.currentPassword,
        newPassword: form.newPassword
      });
      addToast({ type: 'success', message: t('components.passwordChange.changed') });
      passwordChanged();
    } catch (e) {
      addToast({ type: 'error', message: errorMessage(t, e) });
    } finally {
      setBusy(false);
    }
  };

  const field = (name, label, tipKey) => (
    <FormField
      id={`pwd-${name}`}
      field={{ name, type: 'password', label: t(label), required: true, tip: t(tipKey) }}
      value={form[name]}
      error={errors[name]}
      onChange={(v) => setForm((f) => ({ ...f, [name]: v }))}
    />
  );

  return (
    <Modal
      open={open}
      blocking
      size="small"
      title={t('components.passwordChange.title')}
      testId="password-change"
      footer={
        <>
          <button type="button" className="button-secondary" onClick={logout} disabled={busy} {...tip(t('tips.topbar.logout'))}>
            {t('common.actions.logout')}
          </button>
          <button type="button" className="button-primary" onClick={submit} disabled={busy} {...tip(t('tips.passwordChange.submit'))}>
            {busy ? t('common.generic.saving') : t('components.passwordChange.submit')}
          </button>
        </>
      }
    >
      <p className="text-muted">{t('components.passwordChange.body', { min: MIN_PASSWORD_LENGTH })}</p>
      <div className="form-grid single">
        {field('currentPassword', 'components.passwordChange.current', 'tips.passwordChange.current')}
        {field('newPassword', 'components.passwordChange.new', 'tips.passwordChange.new')}
        {field('confirmPassword', 'components.passwordChange.confirm', 'tips.passwordChange.confirm')}
      </div>
    </Modal>
  );
}
