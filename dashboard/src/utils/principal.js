// Client-side capabilities derived from the principal. UX only; the server enforces authorization.

export function principalCaps(principal) {
  const isSystemAdmin = !!principal?.isAdmin;
  const isTenantAdmin = !!principal?.isTenantAdmin;
  return {
    isSystemAdmin,
    isTenantAdmin,
    isAdminOfSomething: isSystemAdmin || isTenantAdmin,
    tenantId: principal?.tenantId || null,
    principalName: principal?.principalName || '',
    principalType: principal?.principalType || ''
  };
}

/** i18n key for the principal's role label. */
export function roleKey(principal) {
  if (principal?.isAdmin) return 'roles.systemAdmin';
  if (principal?.isTenantAdmin) return 'roles.tenantAdmin';
  if (principal?.principalType === 'Credential') return 'roles.credential';
  return 'roles.user';
}

/** Whether a principal may see a route with the given requirement. */
export function allowed(requires, principal) {
  const caps = principalCaps(principal);
  if (!requires) return true;
  if (requires === 'systemAdmin') return caps.isSystemAdmin;
  if (requires === 'admin') return caps.isAdminOfSomething;
  return true;
}
