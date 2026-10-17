import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router-dom';
import { chatReturnPath } from '../utils/standaloneChat';
import { useAuth } from '../context/AuthContext';
import ApiClient from '../utils/api';
import { DEV_DEFAULTS, STORAGE_KEYS, defaultServerUrl } from '../utils/constants';
import { errorMessage } from '../utils/errors';
import useDebounce from '../hooks/useDebounce';
import LanguageSelector from './LanguageSelector';
import { EyeIcon, EyeOffIcon, SunIcon, MoonIcon } from './Icons';
import { tip } from './Tooltip';

/** True when the server URL points at this machine, where the seeded local credentials are worth showing. */
export function isLocalServer(url) {
  try {
    const host = new URL(url).hostname;
    return host === '127.0.0.1' || host === '::1' || host === '[::1]';
  } catch {
    return false;
  }
}

/**
 * Login: server URL plus email and password (with tenant discovery by email), or access key and secret key.
 * The language selector works before sign-in, and the choice survives the login.
 */
export default function Login() {
  const { t } = useTranslation();
  const [params] = useSearchParams();
  const standaloneLogin = !!chatReturnPath(params.get('returnTo'));
  const { login, theme, toggleTheme } = useAuth();
  const [tab, setTab] = useState('password');
  const [serverUrl, setServerUrl] = useState(() => (standaloneLogin ? defaultServerUrl() : localStorage.getItem(STORAGE_KEYS.serverUrl) || defaultServerUrl()));
  const [tenantId, setTenantId] = useState(() => localStorage.getItem(STORAGE_KEYS.tenant) || DEV_DEFAULTS.tenantId);
  const [tenants, setTenants] = useState(null);
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [accessKey, setAccessKey] = useState('');
  const [secretKey, setSecretKey] = useState('');
  const [reveal, setReveal] = useState(false);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const debouncedEmail = useDebounce(email, 400);
  const lookupSeq = useRef(0);

  // Tenant discovery: once the email looks complete, ask the server which tenants it belongs to. One match is used
  // silently, several show a picker, and a server without the lookup route keeps the free-text tenant field.
  useEffect(() => {
    if (tab !== 'password' || !/^\S+@\S+$/.test(debouncedEmail)) {
      setTenants(null);
      return;
    }
    const seq = ++lookupSeq.current;
    new ApiClient(serverUrl)
      .lookupTenants(debouncedEmail)
      .then((list) => {
        if (seq !== lookupSeq.current) return;
        const items = (list || []).filter((x) => x && (x.id || x.tenantId));
        setTenants(items.map((x) => ({ id: x.id || x.tenantId, name: x.name || x.tenantName || x.id })));
        if (items.length >= 1) setTenantId(items[0].id || items[0].tenantId);
      })
      .catch(() => seq === lookupSeq.current && setTenants(null));
  }, [debouncedEmail, serverUrl, tab]);

  const submit = async (e) => {
    e.preventDefault();
    setError('');
    setLoading(true);
    try {
      if (tab === 'password') await login(serverUrl, 'password', { tenantId, email, password });
      else await login(serverUrl, 'key', { accessKey, secretKey });
    } catch (err) {
      if (err?.status === 0) setError(t('login.unreachable', { url: serverUrl }));
      else if (err?.status === 401 || err?.status === 403) setError(t('login.badCredentials'));
      else setError(err?.code ? errorMessage(t, err) : t('login.errorGeneric'));
    } finally {
      setLoading(false);
    }
  };

  const secretInput = (id, value, setter, label, autoComplete, tipText) => (
    <div className="field">
      <label htmlFor={id} {...tip(tipText)}>
        {label}
      </label>
      <div className="password-wrap">
        <input
          id={id}
          {...tip(tipText)}
          type={reveal ? 'text' : 'password'}
          value={value}
          onChange={(e) => setter(e.target.value)}
          required
          disabled={loading}
          autoComplete={autoComplete}
        />
        <button
          type="button"
          className="icon-button"
          onClick={() => setReveal((v) => !v)}
          aria-label={reveal ? t('common.actions.hide') : t('common.actions.reveal')}
          {...tip(t(reveal ? 'tips.form.hide' : 'tips.form.reveal'))}
        >
          {reveal ? <EyeOffIcon /> : <EyeIcon />}
        </button>
      </div>
    </div>
  );

  const multipleTenants = tenants && tenants.length > 1;
  const singleTenant = tenants && tenants.length === 1 ? tenants[0] : null;

  return (
    <main className="login-page">
      <div className="login-card">
        <div className="login-brand">
          <img src="/logo.png" alt="" width="64" height="64" className="brand-logo" />
          <h1>{t('login.title')}</h1>
          <p>{t('login.subtitle')}</p>
        </div>

        <div className="login-tabs" role="tablist">
          <button
            type="button"
            role="tab"
            aria-selected={tab === 'password'}
            className={`login-tab ${tab === 'password' ? 'active' : ''}`}
            onClick={() => setTab('password')}
            {...tip(t('tips.login.passwordTab'))}
          >
            {t('login.tabs.password')}
          </button>
          <button
            type="button"
            role="tab"
            aria-selected={tab === 'key'}
            className={`login-tab ${tab === 'key' ? 'active' : ''}`}
            onClick={() => setTab('key')}
            {...tip(t('tips.login.keyTab'))}
          >
            {t('login.tabs.accessKey')}
          </button>
        </div>

        <form className="login-form" onSubmit={submit}>
          <div className="field">
            <label htmlFor="serverUrl" {...tip(t('tips.login.serverUrl'))}>
              {t('login.serverUrl')}
            </label>
            <input
              id="serverUrl"
              readOnly={standaloneLogin}
              {...tip(t('tips.login.serverUrl'))}
              type="url"
              value={serverUrl}
              onChange={(e) => setServerUrl(e.target.value)}
              required
              disabled={loading}
            />
          </div>
          {tab === 'password' ? (
            <>
              <div className="field">
                <label htmlFor="email" {...tip(t('tips.login.email'))}>
                  {t('login.email')}
                </label>
                <input
                  id="email"
                  {...tip(t('tips.login.email'))}
                  type="email"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  required
                  disabled={loading}
                  autoComplete="username"
                  placeholder={DEV_DEFAULTS.email}
                />
              </div>
              {multipleTenants ? (
                <div className="field">
                  <label htmlFor="tenantSelect" {...tip(t('tips.login.tenantSelect'))}>
                    {t('login.tenant')}
                  </label>
                  <select
                    id="tenantSelect"
                    value={tenantId}
                    onChange={(e) => setTenantId(e.target.value)}
                    disabled={loading}
                    {...tip(t('tips.login.tenantSelect'))}
                  >
                    {tenants.map((tn) => (
                      <option key={tn.id} value={tn.id}>
                        {tn.name}
                      </option>
                    ))}
                  </select>
                </div>
              ) : singleTenant ? (
                <div className="field-hint" data-testid="tenant-found">
                  {t('login.tenantFound', { name: singleTenant.name })}
                </div>
              ) : (
                <div className="field">
                  <label htmlFor="tenantId" {...tip(t('tips.login.tenantId'))}>
                    {t('login.tenantId')}
                  </label>
                  <input
                    id="tenantId"
                    {...tip(t('tips.login.tenantId'))}
                    type="text"
                    value={tenantId}
                    onChange={(e) => setTenantId(e.target.value)}
                    disabled={loading}
                    placeholder={DEV_DEFAULTS.tenantId}
                  />
                </div>
              )}
              {secretInput('password', password, setPassword, t('login.password'), 'current-password', t('tips.login.password'))}
            </>
          ) : (
            <>
              <div className="field">
                <label htmlFor="accessKey" {...tip(t('tips.login.accessKey'))}>
                  {t('login.accessKey')}
                </label>
                <input
                  id="accessKey"
                  {...tip(t('tips.login.accessKey'))}
                  type="text"
                  value={accessKey}
                  onChange={(e) => setAccessKey(e.target.value)}
                  required
                  disabled={loading}
                  autoComplete="username"
                  placeholder="access_..."
                />
              </div>
              {secretInput('secretKey', secretKey, setSecretKey, t('login.secretKey'), 'current-password', t('tips.login.secretKey'))}
            </>
          )}
          {error && (
            <div className="error-banner" role="alert">
              {error}
            </div>
          )}
          <button type="submit" className="button-primary" disabled={loading} data-testid="login-submit" {...tip(t('tips.login.submit'))}>
            {loading ? t('login.connecting') : t('login.submit')}
          </button>
        </form>

        {isLocalServer(serverUrl) && tab === 'password' && (
          <div className="login-hint" data-testid="default-credentials">
            <span>{t('login.defaultsHint', { email: DEV_DEFAULTS.email, password: DEV_DEFAULTS.password })}</span>
            <button
              type="button"
              className="button-ghost button-sm"
              onClick={() => {
                setEmail(DEV_DEFAULTS.email);
                setPassword(DEV_DEFAULTS.password);
                setTenantId(DEV_DEFAULTS.tenantId);
              }}
              {...tip(t('tips.login.useDefaults'))}
            >
              {t('login.useDefaults')}
            </button>
          </div>
        )}

        <div className="login-lang">
          <LanguageSelector />
          <button
            type="button"
            className="icon-button"
            onClick={toggleTheme}
            aria-label={theme === 'dark' ? t('common.theme.switchToLight') : t('common.theme.switchToDark')}
            {...tip(t(theme === 'dark' ? 'tips.topbar.themeLight' : 'tips.topbar.themeDark'))}
          >
            {theme === 'dark' ? <SunIcon /> : <MoonIcon />}
          </button>
        </div>
        <div className="login-footer">{t('login.footerHint')}</div>
      </div>
    </main>
  );
}
