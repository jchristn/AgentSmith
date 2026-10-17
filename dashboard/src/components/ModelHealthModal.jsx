import { useTranslation } from 'react-i18next';
import Modal from './Modal';
import { tip } from './Tooltip';
import { ModelHealthHistory } from './ModelHealthHistory';
import './ModelHealth.css';

/** Read-only health inspection, separate from endpoint editing and validation. */
export default function ModelHealthModal({ endpoint, onClose }) {
  const { t } = useTranslation();
  return (
    <Modal
      open={!!endpoint}
      onClose={onClose}
      title={t('resources.models.health.title', { name: endpoint?.name || '' })}
      size="large"
      testId="model-health-modal"
      footer={
        <button type="button" className="button-secondary" onClick={onClose} {...tip(t('tips.modal.close'))}>
          {t('common.actions.close')}
        </button>
      }
    >
      {endpoint && <ModelHealthHistory key={endpoint.id} endpointId={endpoint.id} />}
    </Modal>
  );
}
