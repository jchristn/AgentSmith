import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import Modal from './Modal';
import CodeBlock from './CodeBlock';
import { LoadingState, ErrorPresenter } from './States';
import { tip } from './Tooltip';
import { formatDuration } from '../i18n/formatters';

/**
 * Runs `load()` once and shows the outcome: a success or failure banner from `success` and `message`, the latency
 * when present, and the full response as JSON. Used for Test Connection, model validation, and referrer lists.
 * `renderResult(result)` replaces the default body.
 */
export default function ResultModal({ title, load, onClose, renderResult }) {
  const { t, i18n } = useTranslation();
  const [result, setResult] = useState(null);
  const [error, setError] = useState(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let active = true;
    Promise.resolve()
      .then(load)
      .then((r) => active && setResult(r))
      .catch((e) => active && setError(e))
      .finally(() => active && setLoading(false));
    return () => {
      active = false;
    };
  }, [load]);

  const ok = result && (result.success ?? result.ok ?? result.healthy ?? true);
  return (
    <Modal
      open
      size="large"
      onClose={onClose}
      title={title}
      testId="result-modal"
      footer={
        <button type="button" className="button-primary" onClick={onClose} {...tip(t('tips.modal.close'))}>
          {t('common.actions.close')}
        </button>
      }
    >
      {loading && <LoadingState />}
      {error && <ErrorPresenter error={error} />}
      {!loading &&
        !error &&
        result !== null &&
        (renderResult ? (
          renderResult(result)
        ) : (
          <>
            <div className={ok ? 'success-banner' : 'error-banner'} data-testid="result-outcome">
              <strong>{ok ? t('components.result.succeeded') : t('components.result.failed')}</strong>
              {result?.message && <span> {result.message}</span>}
              {result?.latencyMs != null && <span className="text-muted"> {t('components.result.latency', { value: formatDuration(result.latencyMs, i18n.language) })}</span>}
            </div>
            <CodeBlock value={result} prettyJson />
          </>
        ))}
    </Modal>
  );
}
