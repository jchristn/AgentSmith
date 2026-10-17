import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { useApp } from '../context/AppContext';
import { useToast } from '../context/ToastContext';
import CopyButton from './CopyButton';
import { tip } from './Tooltip';
import { errorMessage } from '../utils/errors';
import '../views/StandaloneChatView.css';

/** Configure one revocable standalone link on an existing profile. Credentials remain server-side. */
export default function ProfileChatLink({ profile, onSaved }) {
  const { t } = useTranslation();
  const { apiClient, principal } = useAuth();
  const { tenantId } = useApp();
  const { addToast } = useToast();
  const [enabled, setEnabled] = useState(!!profile.chatEnabled);
  const [mode, setMode] = useState(profile.chatAccessMode || 'Authenticated');
  const [chatTitle, setChatTitle] = useState(profile.chatTitle || '');
  const [logoUrl, setLogoUrl] = useState(profile.chatLogoUrl || '');
  const [pageTitle, setPageTitle] = useState(profile.chatPageTitle || '');
  const [credential, setCredential] = useState(profile.chatCredentialId || '');
  const [credentials, setCredentials] = useState([]);
  const [busy, setBusy] = useState(false);
  const [credentialError, setCredentialError] = useState(false);
  useEffect(() => {
    let active = true;
    apiClient
      .listCredentials(tenantId, { pageSize: 1000 })
      .then((result) => {
        const owner = principal?.userId || principal?.principalId || principal?.id;
        if (active)
          setCredentials(
            (result.items || []).filter(
              (c) =>
                c.active !== false &&
                c.enabled !== false &&
                c.authMode !== 'SessionExchange' &&
                !c.isSessionExchange &&
                !c.sessionExchange &&
                (!c.expiresUtc || Date.parse(c.expiresUtc) > Date.now()) &&
                c.userId === owner
            )
          );
      })
      .catch(() => active && setCredentialError(true));
    return () => {
      active = false;
    };
  }, [apiClient, tenantId, principal]);
  useEffect(() => {
    setEnabled(!!profile.chatEnabled);
    setMode(profile.chatAccessMode || 'Authenticated');
    setCredential(profile.chatCredentialId || '');
    setChatTitle(profile.chatTitle || '');
    setLogoUrl(profile.chatLogoUrl || '');
    setPageTitle(profile.chatPageTitle || '');
  }, [profile]);
  const url = profile.chatLinkId ? `${window.location.origin}/chat/${encodeURIComponent(profile.chatLinkId)}` : '';
  const save = async (rotate = false) => {
    setBusy(true);
    try {
      await apiClient.updateProfile(profile.id, {
        chatEnabled: enabled,
        chatAccessMode: mode,
        chatCredentialId: credential || '',
        rotateChatLink: rotate,
        chatTitle,
        chatLogoUrl: logoUrl,
        chatPageTitle: pageTitle
      });
      addToast({ type: 'success', message: t('common.toasts.saved') });
      onSaved();
    } catch (e) {
      addToast({ type: 'error', message: errorMessage(t, e) });
    } finally {
      setBusy(false);
    }
  };
  return (
    <section className="panel snippet chat-link-controls" data-testid="profile-chat-link">
      <h3 className="section-title">{t('standaloneChat.manage.title')}</h3>
      <p className="field-hint">{t('standaloneChat.manage.description')}</p>
      <label className="checkbox-label">
        <input
          type="checkbox"
          checked={enabled}
          onChange={(e) => setEnabled(e.target.checked)}
          disabled={busy}
          {...tip(t('standaloneChat.manage.enableTip'))}
        />
        {t('standaloneChat.manage.enabled')}
      </label>
      <label>
        {t('standaloneChat.manage.mode')}
        <select
          aria-label={t('standaloneChat.manage.mode')}
          value={mode}
          onChange={(e) => setMode(e.target.value)}
          disabled={busy}
          {...tip(t('standaloneChat.manage.modeTip'))}
        >
          <option value="Authenticated">{t('standaloneChat.manage.authenticated')}</option>
          <option value="Public">{t('standaloneChat.manage.public')}</option>
        </select>
      </label>
      {mode === 'Public' && (
        <>
          <p className="field-hint">{t('standaloneChat.manage.publicNotice')}</p>
          <label>
            {t('standaloneChat.manage.credential')}
            <select
              aria-label={t('standaloneChat.manage.credential')}
              value={credential}
              onChange={(e) => setCredential(e.target.value)}
              disabled={busy}
              {...tip(t('standaloneChat.manage.credentialTip'))}
            >
              <option value="">{t('standaloneChat.manage.selectCredential')}</option>
              {credentials.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name || c.description || c.id}
                </option>
              ))}
            </select>
          </label>
          {credentialError && (
            <p role="alert" className="error-banner">
              {t('standaloneChat.manage.credentialsUnavailable')}
            </p>
          )}
        </>
      )}
      <label>
        {t('standaloneChat.manage.chatTitle')}
        <input
          type="text"
          value={chatTitle}
          maxLength={120}
          onChange={(e) => setChatTitle(e.target.value)}
          disabled={busy}
          {...tip(t('standaloneChat.manage.chatTitleTip'))}
        />
      </label>
      <label>
        {t('standaloneChat.manage.logoUrl')}
        <input
          type="text"
          value={logoUrl}
          maxLength={2048}
          onChange={(e) => setLogoUrl(e.target.value)}
          disabled={busy}
          {...tip(t('standaloneChat.manage.logoUrlTip'))}
        />
      </label>
      <label>
        {t('standaloneChat.manage.pageTitle')}
        <input
          type="text"
          value={pageTitle}
          maxLength={200}
          onChange={(e) => setPageTitle(e.target.value)}
          disabled={busy}
          {...tip(t('standaloneChat.manage.pageTitleTip'))}
        />
      </label>
      <div className="form-actions">
        <button
          type="button"
          className="button-primary"
          onClick={() => save()}
          disabled={busy || (enabled && mode === 'Public' && !credential)}
          {...tip(t('standaloneChat.manage.saveTip'))}
        >
          {t('common.actions.save')}
        </button>
      </div>
      {profile.chatEnabled && url && (
        <>
          <div className="secret-row">
            <code>{url}</code>
            <CopyButton value={url} tipText={t('standaloneChat.manage.copyTip')} />
          </div>
          <div className="form-actions">
            <a href={url} target="_blank" rel="noopener noreferrer" className="button button-secondary" {...tip(t('standaloneChat.manage.openTip'))}>
              {t('standaloneChat.manage.open')}
            </a>
            <button type="button" className="button-secondary" onClick={() => save(true)} disabled={busy} {...tip(t('standaloneChat.manage.rotateTip'))}>
              {t('standaloneChat.manage.rotate')}
            </button>
          </div>
          <p className="field-hint">{t('standaloneChat.manage.rotateNotice')}</p>
        </>
      )}
    </section>
  );
}
