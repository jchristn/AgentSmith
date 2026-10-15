import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import Modal from './Modal';
import CodeBlock from './CodeBlock';
import { LoadingState, ErrorBanner } from './States';
import { Badge } from './StatusBadge';
import { tip } from './Tooltip';
import { errorMessage } from '../utils/errors';
import { enumLabel, enumList } from '../utils/enums';
import { enumTip } from '../utils/tips';

/** Effective permissions for a user or credential, with a hasPermission matrix when provided. */
export default function EffectivePermissionsModal({ load, title, onClose }) {
  const { t } = useTranslation();
  const [data, setData] = useState(null);
  const [error, setError] = useState(null);

  useEffect(() => {
    load()
      .then(setData)
      .catch((e) => setError(e));
  }, [load]);

  const grants = data?.grants || data?.effectiveGrants || [];
  return (
    <Modal open size="large" onClose={onClose} title={t('components.permissions.title')} headerMeta={title}>
      {!data && !error && <LoadingState />}
      {error && <ErrorBanner message={errorMessage(t, error)} />}
      {data && (
        <>
          {grants.length === 0 ? (
            <p className="text-muted">{t('components.permissions.none')}</p>
          ) : (
            <div className="table-scroll">
              <table className="data-table">
                <thead>
                  <tr>
                    <th scope="col" {...tip(t('tips.permissions.source'))}>{t('components.permissions.source')}</th>
                    <th scope="col" {...tip(t('tips.permissions.type'))}>{t('components.permissions.type')}</th>
                    <th scope="col" {...tip(t('tips.permissions.resources'))}>{t('components.permissions.resources')}</th>
                    <th scope="col" {...tip(t('tips.permissions.operations'))}>{t('components.permissions.operations')}</th>
                    <th scope="col" {...tip(t('tips.permissions.scope'))}>{t('components.permissions.scope')}</th>
                  </tr>
                </thead>
                <tbody>
                  {grants.map((g, i) => (
                    <tr key={i}>
                      <td>{g.source || '-'}</td>
                      <td>
                        <Badge tone={g.permissionType === 'Deny' ? 'danger' : 'success'} tip={enumTip(t, 'permissionType', g.permissionType || 'Permit')}>{enumLabel(t, 'permissionType', g.permissionType || 'Permit')}</Badge>
                      </td>
                      <td>{enumList(t, 'resourceType', g.resourceTypes)}</td>
                      <td>{enumList(t, 'operationType', g.operations || g.operationTypes)}</td>
                      <td>{g.scope === 'Resource' ? `${enumLabel(t, 'resourceScope', g.scope)}: ${g.resourceId}` : enumLabel(t, 'resourceScope', g.scope || 'Tenant')}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
          <details className="details-section">
            <summary>{t('common.actions.viewJson')}</summary>
            <CodeBlock value={data} prettyJson />
          </details>
        </>
      )}
    </Modal>
  );
}
