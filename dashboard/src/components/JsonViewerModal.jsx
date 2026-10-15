import { useTranslation } from 'react-i18next';
import Modal from './Modal';
import CodeBlock from './CodeBlock';

/** A modal that shows a formatted JSON payload with copy. */
export default function JsonViewerModal({ open, onClose, title, value }) {
  const { t } = useTranslation();
  return (
    <Modal open={open} onClose={onClose} size="large" title={title || t('common.actions.viewJson')}>
      <CodeBlock value={value} prettyJson />
    </Modal>
  );
}
