import { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import Modal from './Modal';
import CodeBlock from './CodeBlock';
import CopyableId from './CopyableId';
import { StatusBadge, MethodPill } from './StatusBadge';
import { LoadingState } from './States';
import { formatDateTime, formatDuration, formatBytes } from '../i18n/formatters';

function DetailItem({ label, children, span }) {
  return (
    <div className={`detail-item ${span ? 'detail-item-span' : ''}`}>
      <div className="detail-item-label">{label}</div>
      <div className="detail-item-value">{children ?? '-'}</div>
    </div>
  );
}

function Section({ title, meta, children }) {
  return (
    <div className="details-section">
      <div className="details-section-title">
        <span>{title}</span>
        {meta}
      </div>
      {children}
    </div>
  );
}

/**
 * Request inspector. Fetches the full entry (list endpoint omits bodies) and
 * renders metadata + headers/bodies + raw JSON, each with copy support.
 */
export default function RequestDetailsModal({ open, onClose, apiClient, requestId }) {
  const { t, i18n } = useTranslation();
  const [entry, setEntry] = useState(null);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    if (!open || !requestId) return undefined;
    let cancelled = false;
    setLoading(true);
    setEntry(null);
    apiClient
      .getRequestHistoryEntry(requestId)
      .then((e) => !cancelled && setEntry(e))
      .catch(() => !cancelled && setEntry(null))
      .finally(() => !cancelled && setLoading(false));
    return () => {
      cancelled = true;
    };
  }, [open, requestId, apiClient]);

  const f = (key) => t(`components.requestDetails.fields.${key}`);

  return (
    <Modal
      open={open}
      onClose={onClose}
      size="full"
      title={t('components.requestDetails.title')}
      headerMeta={entry ? <CopyableId value={entry.id} max={40} /> : null}
    >
      {loading || !entry ? (
        <LoadingState />
      ) : (
        <div>
          <Section title={t('components.requestDetails.metadata')}>
            <div className="detail-grid">
              <DetailItem label={f('method')}>
                <MethodPill method={entry.method} />
              </DetailItem>
              <DetailItem label={f('status')}>
                <StatusBadge code={entry.statusCode} />
              </DetailItem>
              <DetailItem label={f('duration')}>{formatDuration(entry.durationMs, i18n.language)}</DetailItem>
              <DetailItem label={f('sourceIp')}>{entry.sourceIp || '-'}</DetailItem>
              <DetailItem label={f('principal')}>{entry.principalName || '-'}</DetailItem>
              <DetailItem label={f('tenant')}>
                {entry.tenantId ? <CopyableId value={entry.tenantId} /> : '-'}
              </DetailItem>
              <DetailItem label={f('user')}>
                {entry.userId ? <CopyableId value={entry.userId} /> : '-'}
              </DetailItem>
              <DetailItem label={f('createdUtc')}>{formatDateTime(entry.createdUtc, i18n.language)}</DetailItem>
              <DetailItem label={f('completedUtc')}>
                {entry.completedUtc ? formatDateTime(entry.completedUtc, i18n.language) : '-'}
              </DetailItem>
              <DetailItem label={f('path')} span>
                <code className="monospace">{entry.path}</code>
              </DetailItem>
              <DetailItem label={f('url')} span>
                <code className="monospace">{entry.url}</code>
              </DetailItem>
            </div>
          </Section>

          <div className="request-details-grid">
            <div className="request-details-col">
              <Section title={t('components.requestDetails.requestHeaders')}>
                <CodeBlock value={entry.requestHeaders} prettyJson emptyText={t('components.requestDetails.empty')} />
              </Section>

              <Section
                title={t('components.requestDetails.requestBody')}
                meta={
                  entry.requestBodyTruncated ? (
                    <span className="pill pill-warning">
                      {t('components.requestDetails.truncated', { bytes: formatBytes(entry.requestBodyBytes) })}
                    </span>
                  ) : null
                }
              >
                <CodeBlock value={entry.requestBody} prettyJson emptyText={t('components.requestDetails.empty')} />
              </Section>
            </div>

            <div className="request-details-col">
              <Section title={t('components.requestDetails.responseHeaders')}>
                <CodeBlock value={entry.responseHeaders} prettyJson emptyText={t('components.requestDetails.empty')} />
              </Section>

              <Section
                title={t('components.requestDetails.responseBody')}
                meta={
                  entry.responseBodyTruncated ? (
                    <span className="pill pill-warning">
                      {t('components.requestDetails.truncated', { bytes: formatBytes(entry.responseBodyBytes) })}
                    </span>
                  ) : null
                }
              >
                <CodeBlock value={entry.responseBody} prettyJson emptyText={t('components.requestDetails.empty')} />
              </Section>
            </div>
          </div>

          <Section title={t('components.requestDetails.rawJson')}>
            <CodeBlock value={entry} prettyJson />
          </Section>
        </div>
      )}
    </Modal>
  );
}
