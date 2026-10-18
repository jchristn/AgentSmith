import { useTranslation } from 'react-i18next';
import CopyableId from './CopyableId';

/** Review the exact conversation snapshot before a destructive batch. */
export default function ConversationDeleteDetails({ items }) {
  const { t } = useTranslation();
  return (
    <>
      <p className="field-hint">{t('views.chat.deleteRunningHint')}</p>
      <ul className="conversation-delete-list">
        {items.map((item) => (
          <li key={item.id}>
            <strong>{item.title || t('views.chat.untitled')}</strong>
            <CopyableId value={item.id} />
          </li>
        ))}
      </ul>
    </>
  );
}
