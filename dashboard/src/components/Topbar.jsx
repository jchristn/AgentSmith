import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { useApp } from '../context/AppContext';
import { useToast } from '../context/ToastContext';
import { usePendingApprovals } from '../context/ApprovalsContext';
import { GITHUB_URL } from '../utils/constants';
import { roleKey, principalCaps } from '../utils/principal';
import { errorMessage } from '../utils/errors';
import { formatNumber } from '../i18n/formatters';
import { pathFor } from '../config/navConfig';
import LanguageSelector from './LanguageSelector';
import CopyButton from './CopyButton';
import ConfirmModal from './ConfirmModal';
import { tip } from './Tooltip';
import { MenuIcon, SunIcon, MoonIcon, GithubIcon, LogoutIcon, RefreshIcon, ShieldCheckIcon, LockIcon } from './Icons';

/**
 * Top bar. Left: sidebar toggle, server chip with health, tenant (a switcher for system administrators), principal,
 * role, and live or polling mode. Right: pending approvals, tenant lockdown (tenant administrators), language,
 * GitHub, theme, and logout.
 */
export default function Topbar({ onToggleSidebar, health, mode }) {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const { apiClient, serverUrl, principal, theme, toggleTheme, logout } = useAuth();
  const { tenantId, setTenantId, tenants, isSystemAdmin, lockdown, setLockdown } = useApp();
  const { pending } = usePendingApprovals();
  const { addToast } = useToast();
  const [lockdownConfirm, setLockdownConfirm] = useState(false);
  const [busy, setBusy] = useState(false);
  const healthState = health === true ? 'healthy' : health === false ? 'unhealthy' : 'unknown';
  const live = mode === 'live';
  const caps = principalCaps(principal);

  const toggleLockdown = async () => {
    setBusy(true);
    try {
      await apiClient.setLockdown(!lockdown);
      setLockdown(!lockdown);
      addToast({ type: lockdown ? 'success' : 'warning', message: t(lockdown ? 'topbar.lockdown.lifted' : 'topbar.lockdown.engaged') });
    } catch (e) {
      addToast({ type: 'error', message: errorMessage(t, e) });
    } finally {
      setBusy(false);
      setLockdownConfirm(false);
    }
  };

  return (
    <header className="dashboard-header" data-tour="topbar">
      <div className="topbar">
        <div className="topbar-left">
          <button
            type="button"
            className="icon-button"
            onClick={onToggleSidebar}
            aria-label={t('nav.collapseSidebar')}
            {...tip(t('tips.topbar.toggleSidebar'))}
          >
            <MenuIcon />
          </button>
          <span className="topbar-chip topbar-server" {...tip(t('tips.topbar.server', { url: serverUrl }))}>
            <span
              className={`health-dot ${healthState}`}
              role="img"
              aria-label={t(`topbar.health.${healthState}`)}
              {...tip(t(`tips.topbar.health.${healthState}`))}
            />
            <code className="topbar-truncate">{serverUrl}</code>
            <CopyButton value={serverUrl} title={t('topbar.copyServer')} tipText={t('tips.topbar.copyServer')} />
          </span>
          {isSystemAdmin && tenants.length > 0 ? (
            <label className="topbar-chip topbar-tenant" {...tip(t('tips.topbar.switchTenant'))}>
              <span className="text-muted">{t('topbar.tenant')}</span>
              <select
                value={tenantId}
                onChange={(e) => setTenantId(e.target.value)}
                aria-label={t('topbar.switchTenant')}
                {...tip(t('tips.topbar.switchTenant'))}
              >
                {tenants.map((tn) => (
                  <option key={tn.id} value={tn.id}>
                    {tn.name}
                  </option>
                ))}
              </select>
            </label>
          ) : (
            <span className="topbar-chip" {...tip(t('tips.topbar.tenant', { tenant: tenantId }))}>
              <span className="text-muted">{t('topbar.tenant')}</span>
              <code className="topbar-truncate">{tenantId}</code>
            </span>
          )}
          {principal?.principalName && (
            <span className="topbar-chip topbar-hide-narrow" {...tip(t('tips.topbar.principal', { name: principal.principalName }))}>
              <code className="topbar-truncate">{principal.principalName}</code>
            </span>
          )}
          <span className="topbar-chip role topbar-hide-mobile" {...tip(t('tips.topbar.role'))}>
            {t(roleKey(principal))}
          </span>
          <span
            className={`topbar-chip live-chip ${live ? 'on' : ''}`}
            data-testid="live-chip"
            {...tip(t(live ? 'tips.topbar.liveOn' : 'tips.topbar.liveOff'))}
          >
            <RefreshIcon size={12} />
            <span className="topbar-hide-narrow">{live ? t('topbar.live') : t('topbar.polling')}</span>
          </span>
        </div>

        <div className="topbar-right">
          {pending > 0 && (
            <button
              type="button"
              className="topbar-chip approvals-chip"
              onClick={() => navigate(pathFor('approvals', null, { status: 'Pending' }))}
              data-testid="topbar-approvals"
              aria-label={t('nav.approvalsPending', { count: pending })}
              {...tip(t('tips.topbar.approvals', { count: pending }))}
            >
              <ShieldCheckIcon size={14} />
              <span>{pending > 99 ? '99+' : formatNumber(pending, {}, i18n.language)}</span>
            </button>
          )}
          {caps.isAdminOfSomething && (
            <button
              type="button"
              className={`topbar-chip lockdown-chip ${lockdown ? 'on' : ''}`}
              onClick={() => setLockdownConfirm(true)}
              data-testid="lockdown-toggle"
              aria-pressed={lockdown}
              {...tip(t(lockdown ? 'tips.topbar.lockdownOn' : 'tips.topbar.lockdownOff'))}
            >
              <LockIcon size={14} />
              <span className="topbar-hide-narrow">{t(lockdown ? 'topbar.lockdown.on' : 'topbar.lockdown.off')}</span>
            </button>
          )}
          <LanguageSelector compact />
          <a
            className="icon-button"
            href={GITHUB_URL}
            target="_blank"
            rel="noopener noreferrer"
            aria-label={t('common.actions.openGithub')}
            {...tip(t('tips.topbar.github'))}
          >
            <GithubIcon />
          </a>
          <button
            type="button"
            className="icon-button"
            onClick={toggleTheme}
            aria-label={theme === 'dark' ? t('common.theme.switchToLight') : t('common.theme.switchToDark')}
            {...tip(t(theme === 'dark' ? 'tips.topbar.themeLight' : 'tips.topbar.themeDark'))}
            data-testid="theme-toggle"
          >
            {theme === 'dark' ? <SunIcon /> : <MoonIcon />}
          </button>
          <button
            type="button"
            className="icon-button"
            onClick={logout}
            aria-label={t('common.actions.logout')}
            {...tip(t('tips.topbar.logout'))}
            data-testid="logout"
          >
            <LogoutIcon />
          </button>
        </div>
      </div>
      <ConfirmModal
        open={lockdownConfirm}
        danger={!lockdown}
        title={t(lockdown ? 'topbar.lockdown.liftTitle' : 'topbar.lockdown.engageTitle')}
        message={t(lockdown ? 'topbar.lockdown.liftBody' : 'topbar.lockdown.engageBody')}
        confirmLabel={t(lockdown ? 'topbar.lockdown.lift' : 'topbar.lockdown.engage')}
        confirmTip={t(lockdown ? 'tips.topbar.lockdownLift' : 'tips.topbar.lockdownEngage')}
        busy={busy}
        onConfirm={toggleLockdown}
        onCancel={() => setLockdownConfirm(false)}
      />
    </header>
  );
}
