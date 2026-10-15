// Shared helpers for the declarative record page configurations (resourceConfigs.jsx, hugoConfigs.jsx) rendered by
// ResourceView. Field labels come from resources.<id>.fields.<name>, hints from resources.<id>.hints.<name>, column
// headers from resources.<id>.columns.<key>.

import CopyableId from '../components/CopyableId';
import { Badge, ActiveBadge } from '../components/StatusBadge';
import { formatDateTime, formatRelativeTime } from '../i18n/formatters';
import { resourceTip, enumTip, optionalText } from '../utils/tips';
import { enumLabel } from '../utils/enums';
import { tip } from '../components/Tooltip';

// ---------------------------------------------------------------- helpers
//
// Every field and column carries a tooltip from tips.resources.<id>.<name> (falling back to
// tips.common.fields.<name>). Sub-fields of list editors use tips.resources.<id>.<parent>Items.<name>.

export function field(id, t, name, type, extra = {}) {
  const hint = optionalText(t, `resources.${id}.hints.${name}`);
  const out = { name, type, label: t(`resources.${id}.fields.${name}`), hint: hint || undefined, tip: resourceTip(t, id, name), ...extra };
  if (Array.isArray(out.fields)) out.fields = out.fields.map((f) => ({ ...f, tip: f.tip || resourceTip(t, id, `${name}Items.${f.name}`) }));
  return out;
}

export function section(id, t, name, step) {
  return { name: `section_${name}`, type: 'section', label: t(`resources.${id}.sections.${name}`), tip: resourceTip(t, id, `sections.${name}`), step };
}

export function col(id, t, key, render, extra = {}) {
  return { key, label: t(`resources.${id}.columns.${key}`), tip: resourceTip(t, id, key), render, ...extra };
}

export const idCol = (id, t) => col(id, t, 'id', (r) => <CopyableId value={r.id} />, { cellClass: 'cell-id', defaultHidden: true });
export const nameCol = (id, t) => col(id, t, 'name', (r) => <strong>{r.name}</strong>, { sortable: true, pinned: true });
export const createdCol = (id, t, i18n) =>
  col(id, t, 'createdUtc', (r) => <span {...tip(formatDateTime(r.createdUtc, i18n.language))}>{formatRelativeTime(r.createdUtc, i18n.language)}</span>, {
    sortable: true
  });
export const activeCol = (id, t) =>
  col(id, t, 'active', (r) => (
    <ActiveBadge
      active={r.active}
      activeLabel={t('common.generic.active')}
      inactiveLabel={t('common.generic.inactive')}
      tip={t(r.active ? 'tips.badges.active' : 'tips.badges.inactive')}
    />
  ));
export const builtInCol = (id, t) =>
  col(id, t, 'isBuiltIn', (r) =>
    r.isBuiltIn ? (
      <Badge tone="info" tip={t('tips.badges.builtIn')}>
        {t('common.generic.builtIn')}
      </Badge>
    ) : (
      <Badge tip={t('tips.badges.custom')}>{t('common.generic.custom')}</Badge>
    )
  );
export const enumBadge = (t, group, value, tone = 'neutral') =>
  value ? (
    <Badge tone={tone} tip={enumTip(t, group, value)}>
      {enumLabel(t, group, value)}
    </Badge>
  ) : (
    '-'
  );
export const actionTip = (t, id, name) => t(`tips.resources.${id}.actions.${name}`);

export const notBuiltIn = (r) => !r?.isBuiltIn;

export function safeList(promise) {
  return promise.then((r) => r.items || []).catch(() => []);
}

/** Wrap API methods that take (tenantId, ...) into the ResourceView api signature. */
export function tenantApi(list, create, update, remove) {
  return {
    list: (c, tenant, params) => c[list](tenant, params),
    create: create ? (c, tenant, body) => c[create](tenant, body) : null,
    update: update ? (c, tenant, id, body) => c[update](tenant, id, body) : null,
    remove: remove ? (c, tenant, id) => c[remove](tenant, id) : null
  };
}

/** Names of records a delete affects, shown under the confirmation message. */
export function affectedList(names) {
  return (
    <ul className="plain-list confirm-list" data-testid="delete-affected">
      {names.map((n) => (
        <li key={n}>
          <code>{n}</code>
        </li>
      ))}
    </ul>
  );
}

/** Wrap API methods that ignore the tenant (Hugo routes take the tenant from the session). */
export function hugoApi(list, create, update, remove) {
  return {
    list: (c, _tenant, params) => c[list](params),
    create: create ? (c, _tenant, body) => c[create](body) : null,
    update: update ? (c, _tenant, id, body) => c[update](id, body) : null,
    remove: remove ? (c, _tenant, id) => c[remove](id) : null
  };
}

/** A relative time with the exact time in its tooltip, or '-' (or `emptyText`). */
export function timeCell(value, i18n, emptyText = '-') {
  if (!value) return emptyText;
  return <span {...tip(formatDateTime(value, i18n.language))}>{formatRelativeTime(value, i18n.language)}</span>;
}
