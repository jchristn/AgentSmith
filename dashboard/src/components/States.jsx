import { useTranslation } from 'react-i18next';
import { RefreshIcon, InfoIcon, PlugIcon } from './Icons';
import CopyableId from './CopyableId';
import { tip } from './Tooltip';
import { errorMessage } from '../utils/errors';

/** Centered loading block. */
export function LoadingState({ label }) {
  const { t } = useTranslation();
  return (
    <div className="state-block" role="status">
      <div className="loading-spinner" />
      <p>{label || t('common.generic.loading')}</p>
    </div>
  );
}

/** Empty state with a title, body, and an optional call to action. */
export function EmptyState({ title, body, action, icon }) {
  return (
    <div className="state-block">
      {icon}
      <h3>{title}</h3>
      {body && <p>{body}</p>}
      {action}
    </div>
  );
}

/** Dismissible/retryable error banner. */
export function ErrorBanner({ message, onRetry, onDismiss }) {
  const { t } = useTranslation();
  return (
    <div className="error-banner" role="alert">
      <span>{message || t('common.errors.loadFailed')}</span>
      {onRetry && (
        <button type="button" className="button-secondary button-sm" onClick={onRetry} {...tip(t('tips.actions.retryLoad'))}>
          <RefreshIcon /> {t('common.actions.retry')}
        </button>
      )}
      {onDismiss && (
        <button type="button" className="button-ghost button-sm" onClick={onDismiss} {...tip(t('tips.actions.dismiss'))}>
          {t('common.actions.dismiss')}
        </button>
      )}
    </div>
  );
}

/**
 * Error presenter (P8.16): the HTTP status, the localized message, the request ID (copyable, for matching request
 * history and logs), the trace ID when present, and a retry button.
 */
export function ErrorPresenter({ error, onRetry, title }) {
  const { t } = useTranslation();
  if (!error) return null;
  const status = error.status || 0;
  return (
    <div className="error-presenter" role="alert" data-testid="error-presenter">
      <div className="error-presenter-head">
        <strong>{title || t('common.errors.title')}</strong>
        <span className="pill pill-danger" {...tip(t('tips.errors.status'))}>
          {status ? t('common.errors.status', { status }) : t('errorCodes.NetworkError')}
        </span>
      </div>
      <p>{errorMessage(t, error)}</p>
      {error.requestId && (
        <div className="error-presenter-meta" {...tip(t('tips.errors.requestId'))}>
          <span className="text-muted">{t('common.errors.requestIdLabel')}</span>
          <CopyableId value={error.requestId} max={80} copyTip={t('tips.errors.copyRequestId')} />
        </div>
      )}
      {error.traceId && (
        <div className="error-presenter-meta" {...tip(t('tips.errors.traceId'))}>
          <span className="text-muted">{t('common.errors.traceIdLabel')}</span>
          <CopyableId value={error.traceId} max={80} />
        </div>
      )}
      {onRetry && (
        <button type="button" className="button-secondary button-sm" onClick={onRetry} {...tip(t('tips.actions.retryLoad'))}>
          <RefreshIcon /> {t('common.actions.retry')}
        </button>
      )}
    </div>
  );
}

/**
 * The page's capability is not exposed by this server yet (the route answered 404). Explains what the page is for,
 * that the server does not offer it yet, and which endpoint the dashboard called, so the gap is actionable. This is
 * not a "coming soon" placeholder: the page works as soon as the server provides the route.
 */
export function UnavailableState({ feature, purpose, endpoint, onRetry }) {
  const { t } = useTranslation();
  return (
    <div className="state-block unavailable-state" data-testid="unavailable-state">
      <PlugIcon size={28} />
      <h3>{t('common.states.notExposedTitle', { feature })}</h3>
      {purpose && <p className="unavailable-purpose">{purpose}</p>}
      <p>{t('common.states.notExposedBody', { feature })}</p>
      {endpoint && (
        <p className="unavailable-endpoint">
          <InfoIcon size={14} /> <span>{t('common.states.notExposedEndpoint')}</span> <code>{endpoint}</code>
        </p>
      )}
      {onRetry && (
        <button type="button" className="button-secondary button-sm" onClick={onRetry} {...tip(t('tips.actions.retryLoad'))}>
          <RefreshIcon /> {t('common.actions.retry')}
        </button>
      )}
    </div>
  );
}
