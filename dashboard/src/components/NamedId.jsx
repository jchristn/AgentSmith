import { useTranslation } from 'react-i18next';
import CopyButton from './CopyButton';
import CopyableId from './CopyableId';
import { tip } from './Tooltip';

/**
 * A record's name with its ID one hover away (M1-20): the name is shown, the tooltip carries the ID, and the copy
 * button copies the ID. Without a name it falls back to the copyable, truncated ID. `deleted` marks a record that
 * no longer exists.
 */
export default function NamedId({ id, name, max = 20, deleted = false }) {
  const { t } = useTranslation();
  if (!id) return <span className="text-muted">-</span>;
  if (!name) return <CopyableId value={id} max={max} copyTip={t('tips.copy.id')} />;
  return (
    <span className="copyable-id named-id">
      <span className={deleted ? 'text-muted' : ''} {...tip(t(deleted ? 'tips.ids.deleted' : 'tips.ids.named', { id }))}>
        {deleted ? t('common.generic.deletedName', { name }) : name}
      </span>
      <CopyButton value={id} tipText={t('tips.copy.id')} />
    </span>
  );
}
