import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router-dom';
import PageHeader from '../components/PageHeader';
import TableFrame from '../components/TableFrame';
import Modal from '../components/Modal';
import CopyableId from '../components/CopyableId';
import MarkdownView from '../components/MarkdownView';
import { EmptyState, ErrorBanner, LoadingState } from '../components/States';
import { tip } from '../components/Tooltip';
import { errorMessage } from '../utils/errors';
import { formatDateTime, formatDuration, formatNumber, truncate } from '../i18n/formatters';
import { DEFAULT_PAGE_SIZE } from '../utils/constants';
import './FeedbackView.css';

/** Agent-scoped feedback review with the persisted request, response, and turn telemetry. */
export default function FeedbackView({ apiClient }) {
  const { t, i18n } = useTranslation();
  const key = (name) => t(`views.feedback.${name}`);
  const [params, setParams] = useSearchParams();
  const agentId = params.get('agentProfileId') || '';
  const [profiles, setProfiles] = useState([]);
  const [profileError, setProfileError] = useState(null);
  const [profileAttempt, setProfileAttempt] = useState(0);
  const [profilesLoading, setProfilesLoading] = useState(true);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE);
  const [refresh, setRefresh] = useState(0);
  const [list, setList] = useState({ items: [], totalCount: 0 });
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(null);
  const [detailId, setDetailId] = useState(null);
  const [detail, setDetail] = useState(null);
  const [detailError, setDetailError] = useState(null);
  const [detailAttempt, setDetailAttempt] = useState(0);
  const listGeneration = useRef(0);

  useEffect(() => {
    setPage(1);
    setDetailId(null);
  }, [agentId]);

  useEffect(() => {
    let active = true;
    setProfilesLoading(true);
    setProfileError(null);
    (async () => {
      const all = [];
      let pageNumber = 1;
      while (active) {
        const result = await apiClient.listProfiles({ pageNumber, pageSize: 100 });
        all.push(...(result?.items || []));
        if (!result?.items?.length || all.length >= (result.totalCount ?? all.length)) break;
        pageNumber += 1;
      }
      if (active) setProfiles(all);
    })()
      .catch((e) => active && setProfileError(e))
      .finally(() => active && setProfilesLoading(false));
    return () => {
      active = false;
    };
  }, [apiClient, profileAttempt]);

  useEffect(() => {
    const generation = ++listGeneration.current;
    let active = true;
    setList({ items: [], totalCount: 0 });
    setError(null);
    setLoading(!!agentId);
    if (agentId) {
      apiClient
        .listFeedback({ agentProfileId: agentId, pageNumber: page, pageSize })
        .then((result) => active && generation === listGeneration.current && setList(result || { items: [], totalCount: 0 }))
        .catch((e) => active && generation === listGeneration.current && setError(e))
        .finally(() => active && generation === listGeneration.current && setLoading(false));
    }
    return () => {
      active = false;
    };
  }, [apiClient, agentId, page, pageSize, refresh]);

  useEffect(() => {
    let active = true;
    setDetail(null);
    setDetailError(null);
    if (detailId)
      apiClient
        .getFeedback(detailId)
        .then((result) => active && setDetail(result))
        .catch((e) => active && setDetailError(e));
    return () => {
      active = false;
    };
  }, [apiClient, detailId, detailAttempt]);

  const rating = (row) => (row.feedbackRating === 'Positive' ? key('positive') : row.feedbackRating === 'Negative' ? key('negative') : key('unavailable'));
  const columns = [
    { key: 'feedbackRating', label: key('rating'), pinned: true, render: rating },
    {
      key: 'feedbackComment',
      label: key('comment'),
      render: (r) => <span className="feedback-preview">{truncate(r.feedbackComment || key('noComment'), 160)}</span>
    },
    {
      key: 'requestText',
      label: key('request'),
      render: (r) => <span className="feedback-preview">{truncate(r.requestText || key('requestUnavailable'), 120)}</span>
    },
    { key: 'feedbackUpdatedUtc', label: key('updated'), render: (r) => formatDateTime(r.feedbackUpdatedUtc, i18n.language) },
    {
      key: 'actions',
      label: t('common.generic.actions'),
      render: (r) => (
        <button type="button" className="button-secondary button-sm" onClick={() => setDetailId(r.id)} {...tip(key('viewTip'))}>
          {t('common.actions.viewDetails')}
        </button>
      )
    }
  ];
  const recorded = (value) => (value === null || value === undefined || value === '' ? key('unavailable') : value);
  const metric = (value, duration = false) =>
    value === null || value === undefined ? key('unavailable') : duration ? formatDuration(value, i18n.language) : formatNumber(value, {}, i18n.language);
  const telemetry = detail
    ? [
        [
          'agent',
          <span key="agent">
            {detail.agentName && <span>{detail.agentName} </span>}
            <CopyableId value={detail.agentProfileId} max={80} />
          </span>
        ],
        ['conversation', <CopyableId key="conversation" value={detail.conversationId} max={80} />],
        ['turn', <CopyableId key="turn" value={detail.turnId} max={80} />],
        ['inputTokens', metric(detail.inputTokens)],
        ['outputTokens', metric(detail.outputTokens)],
        ['firstToken', metric(detail.timeToFirstTokenMs, true)],
        ['duration', metric(detail.durationMs, true)],
        ['model', detail.modelName],
        ['endpoint', <CopyableId key="endpoint" value={detail.modelEndpointId} max={80} />],
        ['source', detail.source],
        ['toolCalls', metric(detail.toolCallCount)],
        ['finishReason', detail.finishReason]
      ]
    : [];

  return (
    <div className="feedback-review">
      <PageHeader title={key('title')} subtitle={key('subtitle')} />
      <div className="panel feedback-agent-filter">
        <label htmlFor="feedback-agent">{key('agent')}</label>
        <select
          id="feedback-agent"
          value={agentId}
          disabled={profilesLoading}
          onChange={(e) => {
            const next = new URLSearchParams(params);
            if (e.target.value) next.set('agentProfileId', e.target.value);
            else next.delete('agentProfileId');
            setPage(1);
            setDetailId(null);
            setParams(next, { replace: true });
          }}
          {...tip(key('agentTip'))}
        >
          <option value="">{key('selectAgent')}</option>
          {agentId && !profiles.some((p) => p.id === agentId) && <option value={agentId}>{agentId}</option>}
          {profiles.map((p) => (
            <option key={p.id} value={p.id}>
              {p.displayName || p.name}
            </option>
          ))}
        </select>
      </div>
      {profileError && <ErrorBanner message={errorMessage(t, profileError)} onRetry={() => setProfileAttempt((n) => n + 1)} />}
      {error && <ErrorBanner message={errorMessage(t, error)} onRetry={() => setRefresh((n) => n + 1)} />}
      {!agentId ? (
        <EmptyState title={key('selectAgent')} body={key('selectAgentHint')} />
      ) : (
        <TableFrame
          tableId="feedback"
          columns={columns}
          items={list.items || []}
          loading={loading}
          rowId={(r) => r.id}
          emptyMessage={key('empty')}
          totalRecords={list.totalCount || 0}
          pageNumber={page}
          pageSize={pageSize}
          onRowClick={(r) => setDetailId(r.id)}
          onPageChange={setPage}
          onPageSizeChange={(size) => {
            setPageSize(size);
            setPage(1);
          }}
          onRefresh={() => setRefresh((n) => n + 1)}
        />
      )}
      <Modal
        open={!!detailId}
        onClose={() => setDetailId(null)}
        title={key('details')}
        headerMeta={<CopyableId value={detailId} max={80} />}
        size="wide"
        testId="feedback-details"
      >
        {detailError ? (
          <ErrorBanner message={errorMessage(t, detailError)} onRetry={() => setDetailAttempt((n) => n + 1)} />
        ) : !detail ? (
          <LoadingState label={key('loading')} />
        ) : (
          <div className="feedback-detail">
            <section>
              <h3>
                {key('rating')}: {rating(detail)}
              </h3>
              <p className="feedback-comment">{detail.feedbackComment || key('noComment')}</p>
              <p className="text-muted">{formatDateTime(detail.feedbackUpdatedUtc, i18n.language)}</p>
            </section>
            <div className="feedback-exchange">
              <section>
                <h3>{key('request')}</h3>
                <MarkdownView text={detail.requestText} emptyText={key('requestUnavailable')} />
              </section>
              <section>
                <h3>{key('response')}</h3>
                <MarkdownView text={detail.responseText} emptyText={key('responseUnavailable')} />
              </section>
            </div>
            <section>
              <h3>{key('telemetry')}</h3>
              <dl className="feedback-telemetry">
                {telemetry.map(([name, value]) => (
                  <div key={name}>
                    <dt>{key(name)}</dt>
                    <dd>{recorded(value)}</dd>
                  </div>
                ))}
              </dl>
            </section>
          </div>
        )}
      </Modal>
    </div>
  );
}
