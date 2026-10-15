// Declarative configurations for record pages rendered by ResourceView.
// Field labels come from resources.<id>.fields.<name>; hints from resources.<id>.hints.<name>.

import CopyableId from '../components/CopyableId';
import { Badge } from '../components/StatusBadge';
import SecretOnceModal from '../components/SecretOnceModal';
import EffectivePermissionsModal from '../components/EffectivePermissionsModal';
import { ENUMS } from '../utils/constants';
import { pathFor } from '../config/navConfig';
import { formatDateTime, formatRelativeTime } from '../i18n/formatters';
import { errorMessage } from '../utils/errors';
import { resourceTip, enumTip } from '../utils/tips';
import { enumLabel, enumOptions, enumList } from '../utils/enums';
import { tip } from '../components/Tooltip';
import NamedId from '../components/NamedId';
import CloneTemplateModal from '../components/CloneTemplateModal';
import ValuePromptModal from '../components/ValuePromptModal';
import { mcpConfigSnippet } from '../utils/snippets';
import { field, col, idCol, nameCol, createdCol, activeCol, builtInCol, enumBadge, actionTip, notBuiltIn, safeList, tenantApi } from './configHelpers';

/** Delete confirmation text for a prompt template. */
export async function templateDeletePreview(t, _apiClient, _tenantId, template) {
  return { message: t('resources.promptTemplates.deleteUnbound', { name: template.name }), names: [] };
}

// ---------------------------------------------------------------- administration

export const tenantsConfig = {
  id: 'tenants',
  explainer: 'tenants',
  endpoint: '/v1.0/api/tenants',
  api: {
    list: (c, _t, p) => c.listTenants(p),
    create: (c, _t, b) => c.createTenant(b),
    update: (c, _t, id, b) => c.updateTenant(id, b),
    remove: (c, _t, id) => c.deleteTenant(id)
  },
  blank: () => ({ name: '', description: '', region: '', active: true }),
  columns: ({ t, i18n }) => [
    nameCol('tenants', t),
    idCol('tenants', t),
    col('tenants', t, 'description', (r) => r.description || '-'),
    col('tenants', t, 'region', (r) => r.region || '-', { defaultHidden: true }),
    col('tenants', t, 'lockdown', (r) => (r.lockdown ? <Badge tone="danger" tip={t('tips.badges.lockdown')}>{t('common.generic.lockdown')}</Badge> : '-'), { csv: (r) => (r.lockdown ? 'true' : 'false') }),
    activeCol('tenants', t),
    createdCol('tenants', t, i18n)
  ],
  fields: (_d, { t }) => [field('tenants', t, 'name', 'text', { required: true }), field('tenants', t, 'description', 'textarea'), field('tenants', t, 'region', 'text'), field('tenants', t, 'active', 'checkbox')],
  validate: (d, t) => (!d.name ? { name: t('common.errors.required') } : null),
  toBody: (d) => ({ name: d.name, description: d.description, region: d.region || null, active: !!d.active })
};

export const usersConfig = {
  id: 'users',
  explainer: 'users',
  endpoint: '/v1.0/api/tenants/{tenantId}/users',
  api: tenantApi('listUsers', 'createUser', 'updateUser', 'deleteUser'),
  blank: () => ({ email: '', password: '', firstName: '', lastName: '', isAdmin: false, isTenantAdmin: false, mustChangePassword: true, active: true }),
  toForm: (r) => ({ ...r, password: '' }),
  columns: ({ t, i18n }) => [
    col('users', t, 'email', (r) => <strong>{r.email}</strong>, { pinned: true }),
    col('users', t, 'name', (r) => [r.firstName, r.lastName].filter(Boolean).join(' ') || '-', { csv: (r) => [r.firstName, r.lastName].filter(Boolean).join(' ') }),
    idCol('users', t),
    col('users', t, 'role', (r) => <Badge tone={r.isAdmin || r.isTenantAdmin ? 'info' : 'neutral'} tip={t(r.isAdmin ? 'tips.badges.systemAdmin' : r.isTenantAdmin ? 'tips.badges.tenantAdmin' : 'tips.badges.user')}>{t(r.isAdmin ? 'roles.systemAdmin' : r.isTenantAdmin ? 'roles.tenantAdmin' : 'roles.user')}</Badge>),
    activeCol('users', t),
    createdCol('users', t, i18n)
  ],
  fields: (_d, { t, mode }) => [
    field('users', t, 'email', 'text', { required: true }),
    field('users', t, 'password', 'password', { required: mode === 'create' }),
    field('users', t, 'firstName', 'text'),
    field('users', t, 'lastName', 'text'),
    field('users', t, 'isTenantAdmin', 'checkbox'),
    field('users', t, 'isAdmin', 'checkbox'),
    field('users', t, 'mustChangePassword', 'checkbox'),
    field('users', t, 'active', 'checkbox')
  ],
  validate: (d, t) => {
    const e = {};
    if (!d.email || !/^\S+@\S+$/.test(d.email)) e.email = t('common.errors.invalidEmail');
    if (!d.id && !d.password) e.password = t('common.errors.required');
    return e;
  },
  toBody: (d) => {
    const body = { email: d.email, firstName: d.firstName, lastName: d.lastName, isAdmin: !!d.isAdmin, isTenantAdmin: !!d.isTenantAdmin, mustChangePassword: !!d.mustChangePassword, active: !!d.active };
    if (d.password) body.password = d.password;
    return body;
  },
  rowActions: (r, ctx) => [
    {
      label: ctx.t('resources.users.actions.permissions'),
      tip: actionTip(ctx.t, 'users', 'permissions'),
      onClick: () => ctx.setExtraModal(<EffectivePermissionsModal load={() => ctx.apiClient.getUserPermissions(ctx.tenantId, r.id)} title={r.email} onClose={() => ctx.setExtraModal(null)} />)
    },
    {
      label: ctx.t('resources.users.actions.resetPassword'),
      tip: actionTip(ctx.t, 'users', 'resetPassword'),
      onClick: () =>
        ctx.setExtraModal(
          <ValuePromptModal
            title={ctx.t('resources.users.actions.resetPassword')}
            label={ctx.t('resources.users.fields.password')}
            tip={resourceTip(ctx.t, 'users', 'password')}
            saveTip={actionTip(ctx.t, 'users', 'resetPassword')}
            secret
            onSubmit={async (value) => {
              await ctx.apiClient.changePassword(r.id, { newPassword: value, mustChangePassword: true });
              ctx.addToast({ type: 'success', message: ctx.t('resources.users.passwordReset') });
            }}
            onClose={() => ctx.setExtraModal(null)}
          />
        )
    },
    {
      label: ctx.t('resources.users.actions.assignRole'),
      tip: actionTip(ctx.t, 'users', 'assignRole'),
      onClick: () => ctx.navigate(pathFor('assignments', null, { userId: r.id }))
    }
  ]
};

export const credentialsConfig = {
  id: 'credentials',
  explainer: 'credentials',
  endpoint: '/v1.0/api/tenants/{tenantId}/credentials',
  api: tenantApi('listCredentials', 'createCredential', 'updateCredential', 'deleteCredential'),
  loadOptions: async ({ apiClient, tenantId }) => ({
    users: await safeList(apiClient.listUsers(tenantId, { pageNumber: 1, pageSize: 1000 })),
    profiles: await safeList(apiClient.listProfiles({ pageNumber: 1, pageSize: 1000 }))
  }),
  loadLookups: async ({ apiClient, tenantId }) => ({
    users: Object.fromEntries((await safeList(apiClient.listUsers(tenantId, { pageNumber: 1, pageSize: 1000 }))).map((u) => [u.id, u.email])),
    profiles: Object.fromEntries((await safeList(apiClient.listProfiles({ pageNumber: 1, pageSize: 1000 }))).map((p) => [p.id, p.displayName || p.name]))
  }),
  blank: (ctx) => ({ name: '', userId: ctx?.principal?.userId || '', authMode: 'Hybrid', agentProfileId: '', lockToProfile: false, expiresUtc: null, active: true }),
  columns: ({ t, i18n, lookups }) => [
    nameCol('credentials', t),
    col('credentials', t, 'accessKey', (r) => <CopyableId value={r.accessKey} max={28} />, { cellClass: 'cell-id' }),
    col('credentials', t, 'secretKeyLast4', (r) => (r.secretKeyLast4 ? <code>****{r.secretKeyLast4}</code> : '-')),
    col('credentials', t, 'userId', (r) => <NamedId id={r.userId || r.ownerUserId} name={lookups?.users?.[r.userId || r.ownerUserId]} />, { csv: (r) => lookups?.users?.[r.userId || r.ownerUserId] || r.userId }),
    col('credentials', t, 'agentProfileId', (r) => (r.agentProfileId ? <NamedId id={r.agentProfileId} name={lookups?.profiles?.[r.agentProfileId]} /> : '-'), { csv: (r) => lookups?.profiles?.[r.agentProfileId] || r.agentProfileId || '' }),
    col('credentials', t, 'lockToProfile', (r) => (r.lockToProfile ? <Badge tone="info" tip={t('tips.badges.locked')}>{t('common.generic.locked')}</Badge> : '-'), { defaultHidden: true }),
    col('credentials', t, 'lastUsedUtc', (r) => (r.lastUsedUtc ? formatRelativeTime(r.lastUsedUtc, i18n.language) : t('common.generic.never'))),
    col('credentials', t, 'expiresUtc', (r) => (r.expiresUtc ? formatDateTime(r.expiresUtc, i18n.language) : t('common.generic.never'))),
    activeCol('credentials', t),
    idCol('credentials', t)
  ],
  fields: (_d, { t, options, mode }) => [
    field('credentials', t, 'name', 'text', { required: true }),
    field('credentials', t, 'userId', 'select', {
      required: true,
      readOnly: mode === 'edit',
      options: (options.users || []).map((u) => ({ value: u.id, label: u.email }))
    }),
    field('credentials', t, 'authMode', 'select', { required: true, options: enumOptions(t, 'credentialAuthMode') }),
    field('credentials', t, 'agentProfileId', 'select', { options: (options.profiles || []).map((p) => ({ value: p.id, label: p.displayName || p.name })) }),
    field('credentials', t, 'lockToProfile', 'checkbox', { showIf: (x) => !!x.agentProfileId }),
    field('credentials', t, 'expiresUtc', 'datetime', { futureOnly: true }),
    field('credentials', t, 'active', 'checkbox')
  ],
  validate: (d, t) => {
    const e = {};
    if (!d.name) e.name = t('common.errors.required');
    if (!d.userId) e.userId = t('common.errors.required');
    return e;
  },
  toBody: (d) => ({ name: d.name, userId: d.userId, authMode: d.authMode, agentProfileId: d.agentProfileId || null, lockToProfile: !!(d.agentProfileId && d.lockToProfile), expiresUtc: d.expiresUtc || null, active: !!d.active }),
  afterCreate: (result, ctx) => {
    if (result?.secretKey) {
      const accessKey = result.accessKey || result.credential?.accessKey;
      ctx.setExtraModal(
        <SecretOnceModal accessKey={accessKey} secretKey={result.secretKey} snippet={mcpConfigSnippet({ serverUrl: ctx.apiClient?.baseUrl, accessKey, secretKey: result.secretKey })} onClose={() => ctx.setExtraModal(null)} />
      );
    }
  },
  rowActions: (r, ctx) => [
    {
      label: ctx.t('resources.credentials.actions.rotate'),
      tip: actionTip(ctx.t, 'credentials', 'rotate'),
      onClick: async () => {
        try {
          const result = await ctx.apiClient.rotateCredential(ctx.tenantId, r.id);
          const accessKey = result?.accessKey || r.accessKey;
          ctx.setExtraModal(<SecretOnceModal accessKey={accessKey} secretKey={result?.secretKey} snippet={mcpConfigSnippet({ serverUrl: ctx.apiClient?.baseUrl, accessKey, secretKey: result?.secretKey })} onClose={() => ctx.setExtraModal(null)} />);
          ctx.reload();
        } catch (e) {
          ctx.addToast({ type: 'error', message: errorMessage(ctx.t, e) });
        }
      }
    },
    {
      label: ctx.t('resources.users.actions.permissions'),
      tip: actionTip(ctx.t, 'credentials', 'permissions'),
      onClick: () => ctx.setExtraModal(<EffectivePermissionsModal load={() => ctx.apiClient.getCredentialPermissions(ctx.tenantId, r.id)} title={r.name} onClose={() => ctx.setExtraModal(null)} />)
    },
    {
      label: ctx.t('resources.credentials.actions.scopes'),
      tip: actionTip(ctx.t, 'credentials', 'scopes'),
      onClick: () => ctx.navigate(pathFor('assignments', null, { credentialId: r.id, view: 'credentials' }))
    }
  ]
};

export const rolesConfig = {
  id: 'roles',
  explainer: 'roles',
  endpoint: '/v1.0/api/tenants/{tenantId}/roles',
  api: tenantApi('listRoles', 'createRole', 'updateRole', 'deleteRole'),
  isEditable: notBuiltIn,
  blank: () => ({ name: '', description: '', active: true }),
  columns: ({ t, i18n }) => [nameCol('roles', t), col('roles', t, 'description', (r) => r.description || '-'), builtInCol('roles', t), activeCol('roles', t), idCol('roles', t), createdCol('roles', t, i18n)],
  fields: (_d, { t }) => [field('roles', t, 'name', 'text', { required: true }), field('roles', t, 'description', 'textarea'), field('roles', t, 'active', 'checkbox')],
  validate: (d, t) => (!d.name ? { name: t('common.errors.required') } : null),
  toBody: (d) => ({ name: d.name, description: d.description, active: !!d.active }),
  rowActions: (r, ctx) => [
    {
      label: ctx.t('common.actions.clone'),
      tip: actionTip(ctx.t, 'roles', 'clone'),
      hidden: !r.isBuiltIn,
      onClick: async () => {
        try {
          await ctx.apiClient.cloneRole(ctx.tenantId, r.id, { name: `${r.name}-custom` });
          ctx.addToast({ type: 'success', message: ctx.t('common.toasts.cloned') });
          ctx.reload();
        } catch (e) {
          ctx.addToast({ type: 'error', message: errorMessage(ctx.t, e) });
        }
      }
    }
  ]
};

export const permissionsConfig = {
  id: 'permissions',
  explainer: 'roles',
  endpoint: '/v1.0/api/tenants/{tenantId}/permissions',
  api: tenantApi('listPermissions', 'createPermission', 'updatePermission', 'deletePermission'),
  isEditable: notBuiltIn,
  blank: () => ({ name: '', permissionType: 'Permit', resourceTypes: [], operationTypes: [], active: true }),
  columns: ({ t }) => [
    nameCol('permissions', t),
    col('permissions', t, 'permissionType', (r) => enumBadge(t, 'permissionType', r.permissionType, r.permissionType === 'Deny' ? 'danger' : 'success')),
    col('permissions', t, 'resourceTypes', (r) => enumList(t, 'resourceType', r.resourceTypes) || '-', { csv: (r) => (r.resourceTypes || []).join(' ') }),
    col('permissions', t, 'operationTypes', (r) => enumList(t, 'operationType', r.operationTypes) || '-', { csv: (r) => (r.operationTypes || []).join(' ') }),
    builtInCol('permissions', t),
    idCol('permissions', t)
  ],
  fields: (_d, { t }) => [
    field('permissions', t, 'name', 'text', { required: true }),
    field('permissions', t, 'permissionType', 'select', { required: true, options: enumOptions(t, 'permissionType') }),
    field('permissions', t, 'resourceTypes', 'multiselect', { options: enumOptions(t, 'resourceType') }),
    field('permissions', t, 'operationTypes', 'multiselect', { options: enumOptions(t, 'operationType') }),
    field('permissions', t, 'active', 'checkbox')
  ],
  validate: (d, t) => {
    const e = {};
    if (!d.name) e.name = t('common.errors.required');
    if (!d.resourceTypes?.length) e.resourceTypes = t('common.errors.chooseOne');
    if (!d.operationTypes?.length) e.operationTypes = t('common.errors.chooseOne');
    return e;
  }
};

function assignmentConfig(id, principalKey, api, loadPrincipals, principalLabel, endpoint) {
  return {
    id,
    explainer: 'assignments',
    endpoint,
    urlFilters: [principalKey],
    api,
    loadOptions: async (ctx) => ({
      principals: await loadPrincipals(ctx),
      roles: await safeList(ctx.apiClient.listRoles(ctx.tenantId, { pageNumber: 1, pageSize: 1000 }))
    }),
    // Show who holds the role by name, with the ID on hover (M1-20).
    loadLookups: async (ctx) => ({ principals: Object.fromEntries((await loadPrincipals(ctx)).map((p) => [p.id, principalLabel(p)])) }),
    blank: () => ({ [principalKey]: '', roleId: '', resourceScope: 'Tenant', resourceId: '', inheritsToChildren: true, active: true }),
    columns: ({ t, i18n, lookups }) => [
      col(id, t, principalKey, (r) => <NamedId id={r[principalKey]} name={lookups?.principals?.[r[principalKey]]} />, { pinned: true, csv: (r) => lookups?.principals?.[r[principalKey]] || r[principalKey] }),
      col(id, t, 'roleName', (r) => <Badge tone="info" tip={t('tips.badges.role', { role: r.roleName || r.roleId || '-' })}>{r.roleName || r.roleId || '-'}</Badge>),
      col(id, t, 'resourceScope', (r) => enumLabel(t, 'resourceScope', r.resourceScope)),
      col(id, t, 'resourceId', (r) => (r.resourceId ? <CopyableId value={r.resourceId} /> : '-')),
      activeCol(id, t),
      createdCol(id, t, i18n),
      idCol(id, t)
    ],
    fields: (d, { t, options }) => [
      field(id, t, principalKey, 'select', { required: true, options: (options.principals || []).map((p) => ({ value: p.id, label: principalLabel(p) })) }),
      field(id, t, 'roleId', 'select', { required: true, options: (options.roles || []).map((r) => ({ value: r.id, label: r.name })) }),
      field(id, t, 'resourceScope', 'select', { required: true, options: enumOptions(t, 'resourceScope') }),
      field(id, t, 'resourceId', 'text', { showIf: (x) => x.resourceScope === 'Resource' }),
      field(id, t, 'inheritsToChildren', 'checkbox', { showIf: (x) => x.resourceScope === 'Tenant' }),
      field(id, t, 'active', 'checkbox')
    ],
    validate: (d, t) => {
      const e = {};
      if (!d[principalKey]) e[principalKey] = t('common.errors.required');
      if (!d.roleId) e.roleId = t('common.errors.required');
      if (d.resourceScope === 'Resource' && !d.resourceId) e.resourceId = t('common.errors.required');
      return e;
    }
  };
}

export const userAssignmentsConfig = assignmentConfig(
  'userAssignments',
  'userId',
  tenantApi('listUserRoleAssignments', 'createUserRoleAssignment', 'updateUserRoleAssignment', 'deleteUserRoleAssignment'),
  (ctx) => safeList(ctx.apiClient.listUsers(ctx.tenantId, { pageNumber: 1, pageSize: 1000 })),
  (u) => u.email,
  '/v1.0/api/tenants/{tenantId}/user-role-assignments'
);

export const credentialAssignmentsConfig = assignmentConfig(
  'credentialAssignments',
  'credentialId',
  tenantApi('listCredentialScopeAssignments', 'createCredentialScopeAssignment', 'updateCredentialScopeAssignment', 'deleteCredentialScopeAssignment'),
  (ctx) => safeList(ctx.apiClient.listCredentials(ctx.tenantId, { pageNumber: 1, pageSize: 1000 })),
  (c) => `${c.name} (${c.accessKey || c.id})`,
  '/v1.0/api/tenants/{tenantId}/credential-scope-assignments'
);

// ---------------------------------------------------------------- prompt templates

export const promptTemplatesConfig = {
  id: 'promptTemplates',
  explainer: 'promptTemplates',
  endpoint: '/v1.0/api/tenants/{tenantId}/prompt-templates',
  api: {
    list: (c, tenant, p) => c.listPromptTemplates(tenant, p),
    create: null,
    update: null,
    remove: (c, tenant, id) => c.deletePromptTemplate(tenant, id)
  },
  filters: [{ name: 'kind', label: (t) => t('resources.promptTemplates.fields.kind'), options: ENUMS.promptTemplateKind, enumGroup: 'promptTemplateKind' }],
  canDuplicate: false,
  isEditable: notBuiltIn,
  columns: ({ t, i18n }) => [
    nameCol('promptTemplates', t),
    col('promptTemplates', t, 'kind', (r) => enumBadge(t, 'promptTemplateKind', r.kind)),
    col('promptTemplates', t, 'currentVersion', (r) => `v${r.currentVersion ?? 1}`, { cellClass: 'right' }),
    col('promptTemplates', t, 'description', (r) => r.description || '-', { defaultHidden: true }),
    builtInCol('promptTemplates', t),
    idCol('promptTemplates', t),
    col('promptTemplates', t, 'lastUpdateUtc', (r) => formatRelativeTime(r.lastUpdateUtc, i18n.language))
  ],
  fields: () => [],
  headerActions: ({ t, navigate }) => (
    <button type="button" className="button-primary" onClick={() => navigate(pathFor('promptTemplates', 'new'))} data-testid="add-template" {...tip(t('tips.resources.promptTemplates.add'))}>
      {t('resources.promptTemplates.add')}
    </button>
  ),
  detailPath: (r) => pathFor('promptTemplates', r.id),
  deletePreview: (r, { apiClient, tenantId, t }) => templateDeletePreview(t, apiClient, tenantId, r),
  rowActions: (r, ctx) => [
    {
      label: ctx.t('common.actions.clone'),
      tip: actionTip(ctx.t, 'promptTemplates', 'clone'),
      onClick: () =>
        ctx.setExtraModal(
          <CloneTemplateModal
            template={r}
            onClose={() => ctx.setExtraModal(null)}
            onCloned={(created) => {
              ctx.setExtraModal(null);
              ctx.addToast({ type: 'success', message: ctx.t('common.toasts.cloned') });
              if (created?.id) ctx.navigate(pathFor('promptTemplates', created.id));
              else ctx.reload();
            }}
          />
        )
    }
  ]
};

// ---------------------------------------------------------------- audit

export const auditConfig = {
  id: 'audit',
  explainer: 'audit',
  endpoint: '/v1.0/api/tenants/{tenantId}/audit',
  api: {
    list: (c, tenant, p) => c.listAudit(tenant, p),
    create: null,
    update: null,
    remove: (c, tenant, id) => c.deleteAuditRecord(tenant, id)
  },
  filters: [
    { name: 'eventType', label: (t) => t('resources.audit.columns.eventType'), options: ENUMS.auditEventType, enumGroup: 'auditEventType' },
    { name: 'principalId', type: 'text', label: (t) => t('resources.audit.fields.principalId') },
    { name: 'fromUtc', type: 'datetime', label: (t) => t('components.filters.from') },
    { name: 'toUtc', type: 'datetime', label: (t) => t('components.filters.to') }
  ],
  canDuplicate: false,
  isEditable: () => false,
  canDelete: () => true,
  columns: ({ t, i18n }) => [
    col('audit', t, 'createdUtc', (r) => <span {...tip(formatDateTime(r.createdUtc, i18n.language))}>{formatRelativeTime(r.createdUtc, i18n.language)}</span>, { sortable: true, pinned: true }),
    col('audit', t, 'eventType', (r) => (
      <Badge tone={/Denied|Failure|Mismatch/.test(r.eventType) ? 'danger' : /Bypass|Revealed|Lockdown/.test(r.eventType) ? 'warning' : 'neutral'} tip={enumTip(t, 'auditEventType', r.eventType)}>
        {t(`enums.auditEventType.${r.eventType}`, { defaultValue: r.eventType })}
      </Badge>
    )),
    col('audit', t, 'principalName', (r) => r.principalName || r.principalId || '-'),
    col('audit', t, 'resource', (r) => [r.resourceType && enumLabel(t, 'resourceType', r.resourceType), r.operation && enumLabel(t, 'operationType', r.operation)].filter(Boolean).join(' / ') || '-', { csv: (r) => [r.resourceType, r.operation].filter(Boolean).join('/') }),
    col('audit', t, 'reason', (r) => r.reason || r.description || '-'),
    col('audit', t, 'sourceIp', (r) => r.sourceIp || '-', { defaultHidden: true }),
    col('audit', t, 'id', (r) => <CopyableId value={r.id} />, { cellClass: 'cell-id', defaultHidden: true })
  ],
  fields: (_d, { t }) =>
    ['eventType', 'principalType', 'principalName', 'principalId', 'resourceType', 'resourceId', 'operation', 'method', 'path', 'authResult', 'authorizationResult', 'reason', 'bypassReason', 'statusCode', 'sourceIp', 'requestId', 'createdUtc'].map((name) => ({
      name,
      type: name === 'reason' ? 'textarea' : 'text',
      label: t(`resources.audit.fields.${name}`),
      tip: resourceTip(t, 'audit', name)
    }))
};
