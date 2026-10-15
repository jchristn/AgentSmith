import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { formatDuration, formatNumber } from '../i18n/formatters';
import { errorMessage } from '../utils/errors';
import { EnumBadge } from './StatusBadge';
import { tip } from './Tooltip';

/**
 * A model endpoint validation result laid out for people: outcome, model, kind, latency, embedding dimensions, the
 * provider's HTTP status, a preview of the model's reply, and any error or problems. Used by the row action modal and
 * by the create and edit forms' Validate button.
 */
export default function ModelValidationView({ result }) {
  const { t, i18n } = useTranslation();
  if (!result) return null;
  const ok = !!result.success;
  const problems = Array.isArray(result.problems) ? result.problems.filter(Boolean) : [];
  return (
    <div className="model-validation" data-testid="model-validation">
      <div className={ok ? 'success-banner' : 'error-banner'} data-testid="model-validation-outcome">
        <strong>{ok ? t('resources.models.validation.succeeded') : t('resources.models.validation.failed')}</strong>
        {ok && result.latencyMs != null && (
          <span> {t('resources.models.validation.respondedIn', { value: formatDuration(result.latencyMs, i18n.language) })}</span>
        )}
      </div>
      <dl className="kv-grid">
        {result.model && (
          <>
            <dt>{t('resources.models.validation.model')}</dt>
            <dd>
              <code>{result.model}</code>
            </dd>
          </>
        )}
        {result.kind && (
          <>
            <dt>{t('resources.models.validation.kind')}</dt>
            <dd>
              <EnumBadge group="modelKind" value={result.kind} tone="info" />
            </dd>
          </>
        )}
        {result.latencyMs != null && (
          <>
            <dt>{t('resources.models.validation.latency')}</dt>
            <dd>{formatDuration(result.latencyMs, i18n.language)}</dd>
          </>
        )}
        {result.dimensions != null && (
          <>
            <dt>{t('resources.models.validation.dimensions')}</dt>
            <dd>{formatNumber(result.dimensions, {}, i18n.language)}</dd>
          </>
        )}
        {result.statusCode != null && (
          <>
            <dt>{t('resources.models.validation.statusCode')}</dt>
            <dd>
              <code>{result.statusCode}</code>
            </dd>
          </>
        )}
        {result.responsePreview && (
          <>
            <dt>{t('resources.models.validation.response')}</dt>
            <dd>
              <blockquote className="model-validation-reply">{result.responsePreview}</blockquote>
            </dd>
          </>
        )}
        {result.error && (
          <>
            <dt>{t('resources.models.validation.error')}</dt>
            <dd className="text-danger">{result.error}</dd>
          </>
        )}
        {problems.length > 0 && (
          <>
            <dt>{t('resources.models.validation.problems')}</dt>
            <dd>
              <ul className="plain-list">
                {problems.map((p) => (
                  <li key={p}>{p}</li>
                ))}
              </ul>
            </dd>
          </>
        )}
      </dl>
      {!ok && <p className="text-muted">{t('resources.models.validation.hint')}</p>}
    </div>
  );
}

/**
 * The Validate button inside the model endpoint form: sends the unsaved settings to
 * POST /v1.0/api/model-endpoints/validate (nothing is stored) and shows the result inline.
 */
export function ModelDraftValidator({ apiClient, data, toBody, disabled }) {
  const { t } = useTranslation();
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState(null);
  const [error, setError] = useState(null);
  const ready = !!(data?.provider && data?.model);

  const run = async () => {
    setBusy(true);
    setResult(null);
    setError(null);
    try {
      setResult(await apiClient.validateModelEndpointDraft(toBody(data)));
    } catch (e) {
      setError(errorMessage(t, e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="model-draft-validator" data-testid="model-draft-validator">
      <div className="inline-actions">
        <button
          type="button"
          className="button-secondary"
          onClick={run}
          disabled={disabled || busy || !ready || !apiClient}
          data-testid="model-draft-validate"
          {...tip(t('tips.resources.models.validateDraft'))}
        >
          {busy ? t('resources.models.validation.running') : t('resources.models.validation.button')}
        </button>
        {!ready && <span className="text-muted">{t('resources.models.validation.needsModel')}</span>}
      </div>
      {error && <div className="error-banner">{error}</div>}
      {result && <ModelValidationView result={result} />}
    </div>
  );
}
