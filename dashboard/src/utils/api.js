/**
 * API client for the Hugo server.
 *
 * Hand-rolled over the browser fetch API (no axios). Every backend endpoint the dashboard uses is a named method
 * here, so contract changes are fixed in one place. List endpoints return the normalized page shape
 * { items, pageNumber, pageSize, totalCount }. Errors throw ApiError with the parsed error body
 * ({ error, code, message, requestId, fields, fieldCodes }).
 *
 * Two route families:
 *  - Identity and administration routes forked from Garrison keep Garrison's paths and JSON shapes
 *    (`/v1.0/api/tenants/{tenantId}/users` and so on), per plan decision D-032.
 *  - Hugo's own routes follow plan section 14.2 (`/v1.0/api/resources`, `/v1.0/api/agent-profiles`, ...). They are
 *    tenant-implicit: the server takes the tenant from the session (system administrators may add ?tenantId=).
 *    They enumerate with Garrison's envelope (maxResults, skip, objects, totalRecords; D-039), and a few answer bare
 *    arrays (connectors, bindings, knowledge, referrers). Request history and audit page with pageNumber and pageSize.
 * A few routes that moved between Garrison and Hugo (server info, restart) try the Hugo path first and fall back to
 * the Garrison path on 404.
 */

import { ROOT_KEY, isRootKey } from './knowledge';

const enc = encodeURIComponent;

class ApiClient {
  /**
   * @param {string} baseUrl Base URL of the server (trailing slash optional).
   * @param {string|null} token Bearer token.
   */
  constructor(baseUrl, token = null) {
    this.baseUrl = (baseUrl || '').replace(/\/$/, '');
    this.token = token;
  }

  _headers(extra = {}) {
    const headers = { 'Content-Type': 'application/json', ...extra };
    if (this.token) headers['Authorization'] = `Bearer ${this.token}`;
    try {
      const locale = document.documentElement.lang;
      if (locale) headers['Accept-Language'] = locale;
    } catch {
      /* not in a browser */
    }
    return headers;
  }

  _url(path, query) {
    const url = new URL(this.baseUrl + path);
    if (query) {
      for (const [k, v] of Object.entries(query)) {
        if (v !== undefined && v !== null && v !== '') url.searchParams.append(k, v);
      }
    }
    return url.toString();
  }

  /** Translate { pageNumber, pageSize, ...filters } into { maxResults, skip, ...filters } (Garrison enumeration). */
  _enumParams(params = {}) {
    const { pageNumber, pageSize, ...rest } = params || {};
    const out = { ...rest };
    if (pageSize != null) {
      out.maxResults = pageSize;
      out.skip = ((pageNumber || 1) - 1) * pageSize;
    }
    return out;
  }

  async _request(method, path, { query = null, body = null, headers = {}, raw = false, signal, keepSessionOn401 = false } = {}) {
    const init = { method, headers: this._headers(headers), signal };
    if (body !== null && body !== undefined) init.body = raw ? body : JSON.stringify(body);

    let response;
    try {
      response = await fetch(this._url(path, query), init);
    } catch (e) {
      if (e?.name === 'AbortError') throw e;
      throw new ApiError(0, { code: 'NetworkError', message: e?.message || 'Network error' });
    }

    if (response.status === 401 && !keepSessionOn401 && !path.startsWith('/v1.0/token')) {
      window.dispatchEvent(new CustomEvent('auth:unauthorized'));
    }

    const text = await response.text().catch(() => '');
    let parsed = null;
    if (text) {
      try {
        parsed = JSON.parse(text);
      } catch {
        parsed = text;
      }
    }

    if (!response.ok) throw new ApiError(response.status, parsed ?? response.statusText, response.headers?.get?.('x-request-id'));
    if (response.status === 204) return null;
    return parsed;
  }

  /** Try `primary`; on 404 (route missing on this server) try `fallback`. */
  async _withFallback(primary, fallback) {
    try {
      return await primary();
    } catch (e) {
      if (e?.status === 404) return fallback();
      throw e;
    }
  }

  /** Garrison-style enumeration (maxResults and skip). */
  async _list(path, params = {}) {
    const res = await this._request('GET', path, { query: this._enumParams(params) });
    return normalizeEnumeration(res, params.pageNumber, params.pageSize);
  }

  /** Page-number enumeration (pageNumber and pageSize), used by request history, audit, and every Hugo route. */
  async _pagedList(path, params = {}) {
    const res = await this._request('GET', path, { query: params });
    return normalizeEnumeration(res, params.pageNumber, params.pageSize);
  }

  /** Garrison tenant-scoped path. */
  _t(tenantId, sub) {
    return `/v1.0/api/tenants/${enc(tenantId)}/${sub}`;
  }

  // ------------------------------------------------------------------ auth and health

  /** Anonymous health probe. */
  healthCheck() {
    return this._request('GET', '/v1.0/api/health');
  }

  /** Validate the bearer token. */
  validateToken() {
    return this._request('GET', '/v1.0/token');
  }

  /** Describe the authenticated principal (tenant, user, admin flags, mustChangePassword). */
  getTokenDetails() {
    return this._request('GET', '/v1.0/token/details');
  }

  /** Log in with tenant, email, and password. */
  loginWithPassword(tenantId, email, password) {
    return this._request('POST', '/v1.0/token', { body: { tenantId: tenantId || undefined, email, password } });
  }

  /** Log in with an access key and secret key. */
  loginWithKey(accessKey, secretKey) {
    return this._request('POST', '/v1.0/token', { body: { accessKey, secretKey } });
  }

  /** Tenants an email belongs to, for the login form. Returns [{ id, name }]. */
  async lookupTenants(email) {
    const res = await this._request('POST', '/v1.0/tenants/lookup', { body: { email } });
    if (Array.isArray(res)) return res;
    return res?.tenants || res?.objects || res?.items || [];
  }

  /** Refresh the session token. */
  refreshToken() {
    return this._request('POST', '/v1.0/token/refresh');
  }

  /** Re-authenticate the current session with the password (needed within minutes before a secret reveal). */
  reauthenticate(password) {
    return this._request('POST', '/v1.0/token/reauthenticate', { body: { password } });
  }

  /** Revoke the session token (logout). */
  revokeToken() {
    return this._request('DELETE', '/v1.0/token');
  }

  /** Change the signed-in user's password (clears must-change-password). body: { currentPassword, newPassword }. */
  changeMyPassword(body) {
    return this._request('POST', '/v1.0/api/users/me/password', { body });
  }

  /** An administrator's password reset; the user must change it at next sign-in unless mustChangePassword is false. */
  resetUserPassword(tenantId, userId, newPassword, mustChangePassword = true) {
    return this._request('POST', this._t(tenantId, `users/${enc(userId)}/password`), {
      body: { newPassword },
      query: mustChangePassword ? undefined : { mustChangePassword: 'false' }
    });
  }

  // ------------------------------------------------------------------ server, settings, events

  getServerInfo() {
    return this._withFallback(
      () => this._request('GET', '/v1.0/api/info'),
      () => this._request('GET', '/v1.0/api/server-info')
    );
  }

  /** Reachability of the bundled observability services (Grafana, Prometheus, Tempo, Loki). */
  getObservabilityStatus() {
    return this._request('GET', '/v1.0/api/observability/status');
  }

  getSettings() {
    return this._request('GET', '/v1.0/api/settings');
  }

  updateSettings(body) {
    return this._request('PUT', '/v1.0/api/settings', { body });
  }

  restartServer() {
    return this._withFallback(
      () => this._request('POST', '/v1.0/api/system/restart'),
      () => this._request('POST', '/v1.0/api/settings/restart')
    );
  }

  testDatabaseConnection(body) {
    return this._request('POST', '/v1.0/api/settings/database/test', { body });
  }

  /** The tenant's lockdown state: { tenantId, lockdown, approvalsCancelled }. */
  getLockdown() {
    return this._request('GET', '/v1.0/api/tenant/lockdown');
  }

  /** Turn tenant lockdown on (every non-Read call denied, pending approvals cancelled) or off. */
  setLockdown(on, reason) {
    return on
      ? this._request('POST', '/v1.0/api/tenant/lockdown', { body: { reason: reason || undefined } })
      : this._request('DELETE', '/v1.0/api/tenant/lockdown');
  }

  /**
   * Subscribe to the tenant event stream (GET /v1.0/api/events, Server-Sent Events). Calls onEvent({ event, data, id })
   * per event, onOpen() once connected, and onEnd(error|null) when the stream closes. Returns an AbortController.
   */
  streamEvents(onEvent, { onOpen, onEnd } = {}) {
    return this._stream('GET', '/v1.0/api/events', {}, { onEvent, onOpen, onEnd });
  }

  // ------------------------------------------------------------------ tenants (Garrison routes)

  listTenants(params) {
    return this._list('/v1.0/api/tenants', params);
  }

  getTenant(id) {
    return this._request('GET', `/v1.0/api/tenants/${enc(id)}`);
  }

  createTenant(body) {
    return this._request('POST', '/v1.0/api/tenants', { body });
  }

  updateTenant(id, body) {
    return this._request('PUT', `/v1.0/api/tenants/${enc(id)}`, { body });
  }

  deleteTenant(id) {
    return this._request('DELETE', `/v1.0/api/tenants/${enc(id)}`);
  }

  // ------------------------------------------------------------------ users and credentials (Garrison routes)

  listUsers(tenantId, params) {
    return this._list(this._t(tenantId, 'users'), params);
  }

  getUser(tenantId, id) {
    return this._request('GET', this._t(tenantId, `users/${enc(id)}`));
  }

  createUser(tenantId, body) {
    return this._request('POST', this._t(tenantId, 'users'), { body });
  }

  updateUser(tenantId, id, body) {
    return this._request('PUT', this._t(tenantId, `users/${enc(id)}`), { body });
  }

  deleteUser(tenantId, id) {
    return this._request('DELETE', this._t(tenantId, `users/${enc(id)}`));
  }

  getUserPermissions(tenantId, id) {
    return this._request('GET', this._t(tenantId, `users/${enc(id)}/permissions`));
  }

  listCredentials(tenantId, params) {
    return this._list(this._t(tenantId, 'credentials'), params);
  }

  getCredential(tenantId, id) {
    return this._request('GET', this._t(tenantId, `credentials/${enc(id)}`));
  }

  /** Create a credential. The response includes secretKey exactly once. */
  createCredential(tenantId, body) {
    return this._request('POST', this._t(tenantId, 'credentials'), { body });
  }

  updateCredential(tenantId, id, body) {
    return this._request('PUT', this._t(tenantId, `credentials/${enc(id)}`), { body });
  }

  deleteCredential(tenantId, id) {
    return this._request('DELETE', this._t(tenantId, `credentials/${enc(id)}`));
  }

  /** Rotate a credential secret. The response includes the new secretKey exactly once. */
  rotateCredential(tenantId, id) {
    return this._request('POST', this._t(tenantId, `credentials/${enc(id)}/rotate`));
  }

  getCredentialPermissions(tenantId, id) {
    return this._request('GET', this._t(tenantId, `credentials/${enc(id)}/permissions`));
  }

  // ------------------------------------------------------------------ RBAC (Garrison routes)

  listRoles(tenantId, params) {
    return this._list(this._t(tenantId, 'roles'), params);
  }

  getRole(tenantId, id) {
    return this._request('GET', this._t(tenantId, `roles/${enc(id)}`));
  }

  createRole(tenantId, body) {
    return this._request('POST', this._t(tenantId, 'roles'), { body });
  }

  updateRole(tenantId, id, body) {
    return this._request('PUT', this._t(tenantId, `roles/${enc(id)}`), { body });
  }

  deleteRole(tenantId, id) {
    return this._request('DELETE', this._t(tenantId, `roles/${enc(id)}`));
  }

  cloneRole(tenantId, id, body) {
    return this._request('POST', this._t(tenantId, `roles/${enc(id)}/clone`), { body });
  }

  listPermissions(tenantId, params) {
    return this._list(this._t(tenantId, 'permissions'), params);
  }

  createPermission(tenantId, body) {
    return this._request('POST', this._t(tenantId, 'permissions'), { body });
  }

  updatePermission(tenantId, id, body) {
    return this._request('PUT', this._t(tenantId, `permissions/${enc(id)}`), { body });
  }

  deletePermission(tenantId, id) {
    return this._request('DELETE', this._t(tenantId, `permissions/${enc(id)}`));
  }

  listRolePermissions(tenantId, params) {
    return this._list(this._t(tenantId, 'role-permissions'), params);
  }

  createRolePermission(tenantId, body) {
    return this._request('POST', this._t(tenantId, 'role-permissions'), { body });
  }

  deleteRolePermission(tenantId, id) {
    return this._request('DELETE', this._t(tenantId, `role-permissions/${enc(id)}`));
  }

  listUserRoleAssignments(tenantId, params) {
    return this._list(this._t(tenantId, 'user-role-assignments'), params);
  }

  createUserRoleAssignment(tenantId, body) {
    return this._request('POST', this._t(tenantId, 'user-role-assignments'), { body });
  }

  updateUserRoleAssignment(tenantId, id, body) {
    return this._request('PUT', this._t(tenantId, `user-role-assignments/${enc(id)}`), { body });
  }

  deleteUserRoleAssignment(tenantId, id) {
    return this._request('DELETE', this._t(tenantId, `user-role-assignments/${enc(id)}`));
  }

  listCredentialScopeAssignments(tenantId, params) {
    return this._list(this._t(tenantId, 'credential-scope-assignments'), params);
  }

  createCredentialScopeAssignment(tenantId, body) {
    return this._request('POST', this._t(tenantId, 'credential-scope-assignments'), { body });
  }

  updateCredentialScopeAssignment(tenantId, id, body) {
    return this._request('PUT', this._t(tenantId, `credential-scope-assignments/${enc(id)}`), { body });
  }

  deleteCredentialScopeAssignment(tenantId, id) {
    return this._request('DELETE', this._t(tenantId, `credential-scope-assignments/${enc(id)}`));
  }

  // ------------------------------------------------------------------ sessions (plan section 14.2)

  /**
   * Sessions in the caller's tenant (flat alias of the tenant-scoped route). filters: principalType (User,
   * Credential, Administrator), userId, credentialId. Each row carries principalName.
   */
  listSessions(params) {
    return this._list('/v1.0/api/sessions', params);
  }

  revokeSession(id) {
    return this._request('DELETE', `/v1.0/api/sessions/${enc(id)}`);
  }

  // ------------------------------------------------------------------ audit (Garrison routes)

  listAudit(tenantId, params) {
    return this._pagedList(this._t(tenantId, 'audit'), params);
  }

  getAuditRecord(tenantId, id) {
    return this._request('GET', this._t(tenantId, `audit/${enc(id)}`));
  }

  deleteAuditRecord(tenantId, id) {
    return this._request('DELETE', this._t(tenantId, `audit/${enc(id)}`));
  }

  deleteAuditBulk(tenantId, filters) {
    return this._request('DELETE', this._t(tenantId, 'audit'), { query: filters });
  }

  // ------------------------------------------------------------------ request history

  getRequestHistory(filters = {}) {
    return this._pagedList('/v1.0/api/request-history', filters);
  }

  getRequestHistorySummary(filters = {}) {
    return this._request('GET', '/v1.0/api/request-history/summary', { query: filters });
  }

  getRequestHistoryEntry(id) {
    return this._request('GET', `/v1.0/api/request-history/${enc(id)}`);
  }

  deleteRequestHistoryEntry(id) {
    return this._request('DELETE', `/v1.0/api/request-history/${enc(id)}`);
  }

  deleteRequestHistoryBulk(filters = {}) {
    return this._request('DELETE', '/v1.0/api/request-history', { query: filters });
  }

  // ------------------------------------------------------------------ secrets (plan section 12.2)

  /** filters: kind (a secret kind such as Password or ApiToken), search. */
  listSecrets(params) {
    return this._list('/v1.0/api/secrets', params);
  }

  /** Metadata, fields present, fingerprint, and referrers. Never values. */
  getSecret(id) {
    return this._request('GET', `/v1.0/api/secrets/${enc(id)}`);
  }

  /** body: { name, description, kind, expiresUtc, values: { field: value } }. Returns metadata only. */
  createSecret(body) {
    return this._request('POST', '/v1.0/api/secrets', { body });
  }

  /** Name, description, and expiry. */
  updateSecret(id, body) {
    return this._request('PUT', `/v1.0/api/secrets/${enc(id)}`, { body });
  }

  /** Replace the supplied fields; omitted fields are kept. body: { values: { field: value }, remove? }. */
  updateSecretValues(id, values) {
    return this._request('PUT', `/v1.0/api/secrets/${enc(id)}/values`, { body: { values } });
  }

  /** Reveal plaintext values. Needs Reveal on Secret, a reason, and a recent re-authentication. Audited. */
  revealSecret(id, reason) {
    // A 401 here means the session needs re-authentication, not that it ended, so it must not log the user out.
    return this._request('POST', `/v1.0/api/secrets/${enc(id)}/reveal`, { body: { reason }, keepSessionOn401: true });
  }

  /** Resources and model endpoints that use a secret: [{ referrerType, referrerId, referrerName, detail }]. */
  getSecretReferrers(id) {
    return this._request('GET', `/v1.0/api/secrets/${enc(id)}/referrers`);
  }

  /** 409 while the secret is referenced. */
  deleteSecret(id) {
    return this._request('DELETE', `/v1.0/api/secrets/${enc(id)}`);
  }

  // ------------------------------------------------------------------ connectors

  /**
   * Connector descriptors (a bare array): type, family, displayName, description, category, iconKey, version,
   * documentationUrl, defaultInstructionsKey, plugin, settings (the settings schema), secretSlots, tools.
   */
  listConnectors(params) {
    return this._list('/v1.0/api/connectors', params);
  }

  getConnector(type) {
    return this._request('GET', `/v1.0/api/connectors/${enc(type)}`);
  }

  // ------------------------------------------------------------------ resources

  /**
   * filters: connectorType, environment (case-insensitive exact match), health (Healthy, Unhealthy, or Unknown from
   * the last connection test), search.
   */
  listResources(params) {
    return this._list('/v1.0/api/resources', params);
  }

  getResource(id) {
    return this._request('GET', `/v1.0/api/resources/${enc(id)}`);
  }

  /** 400 SettingsInvalid (fields keyed by setting) when the settings fail the connector schema; 409 Duplicate. */
  createResource(body) {
    return this._request('POST', '/v1.0/api/resources', { body });
  }

  updateResource(id, body) {
    return this._request('PUT', `/v1.0/api/resources/${enc(id)}`, { body });
  }

  deleteResource(id) {
    return this._request('DELETE', `/v1.0/api/resources/${enc(id)}`);
  }

  /** Test a saved resource's connection. Returns { success, message, errorCode, latencyMs, details }. */
  testResource(id) {
    return this._request('POST', `/v1.0/api/resources/${enc(id)}/test`);
  }

  /** Test unsaved settings (the create and edit form's Test Connection). Nothing is stored. */
  testResourceSettings(body) {
    return this._request('POST', '/v1.0/api/resources/test', { body });
  }

  /** Bindings that use a resource: [{ agentProfileId, agentProfileName, bindingId, alias, accessMode, enabled }]. */
  getResourceReferrers(id) {
    return this._request('GET', `/v1.0/api/resources/${enc(id)}/referrers`);
  }

  /** body: { name }. Copies settings and secret slots. */
  duplicateResource(id, body) {
    return this._request('POST', `/v1.0/api/resources/${enc(id)}/duplicate`, { body });
  }

  /**
   * Invoke a tool on a resource directly, without a profile (needs Execute on Resource; the access mode is the
   * resource ceiling). Returns a tool invocation outcome; 202 while approval is pending.
   */
  invokeResourceTool(id, toolName, args = {}) {
    return this._request('POST', `/v1.0/api/resources/${enc(id)}/invoke`, { body: { toolName, arguments: args } });
  }

  /** Every knowledge entry of a resource, or one scope's entries: [{ scope, key, content, source, version, ... }]. */
  listResourceKnowledge(id, scope) {
    return this._request('GET', scope ? `/v1.0/api/resources/${enc(id)}/knowledge/${enc(scope)}` : `/v1.0/api/resources/${enc(id)}/knowledge`);
  }

  /** Path of one knowledge entry; a scope's root entry (key _root, or empty) is addressed as _root. */
  _knowledgePath(id, scope, key) {
    return `/v1.0/api/resources/${enc(id)}/knowledge/${enc(scope)}/${isRootKey(key) ? ROOT_KEY : enc(key)}`;
  }

  getResourceKnowledge(id, scope, key) {
    return this._request('GET', this._knowledgePath(id, scope, key));
  }

  /** Write one knowledge entry as an operator edit (scrubbed and audited). Creates a version. */
  putResourceKnowledge(id, scope, key, content) {
    return this._request('PUT', this._knowledgePath(id, scope, key), { body: { content } });
  }

  deleteResourceKnowledge(id, scope, key) {
    return this._request('DELETE', this._knowledgePath(id, scope, key));
  }

  /** Version history of one knowledge entry, newest first. */
  listResourceKnowledgeVersions(id, scope, key) {
    return this._request('GET', `${this._knowledgePath(id, scope, key)}/versions`);
  }

  // ------------------------------------------------------------------ agent profiles and bindings

  /** Profiles; each row carries bindingCount. */
  listProfiles(params) {
    return this._list('/v1.0/api/agent-profiles', params);
  }

  getProfile(id) {
    return this._request('GET', `/v1.0/api/agent-profiles/${enc(id)}`);
  }

  createProfile(body) {
    return this._request('POST', '/v1.0/api/agent-profiles', { body });
  }

  updateProfile(id, body) {
    return this._request('PUT', `/v1.0/api/agent-profiles/${enc(id)}`, { body });
  }

  deleteProfile(id) {
    return this._request('DELETE', `/v1.0/api/agent-profiles/${enc(id)}`);
  }

  /** body: { name }. Copies the profile with its bindings. */
  duplicateProfile(id, body) {
    return this._request('POST', `/v1.0/api/agent-profiles/${enc(id)}/duplicate`, { body });
  }

  /**
   * Compiled instructions as a caller receives them. query: { surface: Chat|Mcp|A2A|OpenAI }. Returns { text,
   * tokenEstimate, bindingCount, segments: [{ key, layer, version, bindingAlias, start, length, depth }], warnings }.
   */
  getCompiledInstructions(id, query) {
    return this._request('GET', `/v1.0/api/agent-profiles/${enc(id)}/compiled-instructions`, { query });
  }

  /** Tools as the caller would see them: { hasModel, resourceCount, tools: [...], metaTools: [...] }. */
  getProfileTools(id, query) {
    return this._request('GET', `/v1.0/api/agent-profiles/${enc(id)}/tools`, { query });
  }

  /** A2A agent card preview. */
  getProfileCard(id) {
    return this._request('GET', `/v1.0/api/agent-profiles/${enc(id)}/card`);
  }

  /** A profile's bindings (a bare array). */
  listBindings(profileId, params) {
    return this._list(`/v1.0/api/agent-profiles/${enc(profileId)}/bindings`, params);
  }

  createBinding(profileId, body) {
    return this._request('POST', `/v1.0/api/agent-profiles/${enc(profileId)}/bindings`, { body });
  }

  updateBinding(profileId, bindingId, body) {
    return this._request('PUT', `/v1.0/api/agent-profiles/${enc(profileId)}/bindings/${enc(bindingId)}`, { body });
  }

  deleteBinding(profileId, bindingId) {
    return this._request('DELETE', `/v1.0/api/agent-profiles/${enc(profileId)}/bindings/${enc(bindingId)}`);
  }

  /** [{ toolName, rule: Allow|Deny|RequireApproval }]. */
  getToolRules(profileId, bindingId) {
    return this._request('GET', `/v1.0/api/agent-profiles/${enc(profileId)}/bindings/${enc(bindingId)}/tool-rules`);
  }

  /** Replace a binding's tool rules; the body is the array itself. Returns the binding. */
  updateToolRules(profileId, bindingId, rules) {
    return this._request('PUT', `/v1.0/api/agent-profiles/${enc(profileId)}/bindings/${enc(bindingId)}/tool-rules`, { body: rules || [] });
  }

  // ------------------------------------------------------------------ prompt templates (layered by key, plan section 10.2)

  /** Every template key with its Built-in, Global, and Tenant layers and the layer in effect (a bare array). */
  listPromptLayers() {
    return this._request('GET', '/v1.0/api/prompt-templates');
  }

  /** One key: { key, description, source, activeLayer, effectiveContent, builtInContent, builtInHash, global, tenant }. */
  getPromptLayers(key) {
    return this._request('GET', `/v1.0/api/prompt-templates/${enc(key)}`);
  }

  /** Save content at a layer (Tenant, or Global for system administrators). Creates a version; returns the view. */
  savePromptLayer(key, layer, content, changeNote) {
    return this._request('PUT', `/v1.0/api/prompt-templates/${enc(key)}`, { query: { layer }, body: { content, changeNote: changeNote || undefined } });
  }

  /** Reset Tenant to the Global default, or Global to the Built-in (adopting a changed Built-in). Keeps history. */
  resetPromptLayer(key, layer) {
    return this._request('POST', `/v1.0/api/prompt-templates/${enc(key)}/reset`, { query: { layer } });
  }

  /** Version history of a layer, newest first. */
  listPromptLayerVersions(key, layer) {
    return this._request('GET', `/v1.0/api/prompt-templates/${enc(key)}/versions`, { query: { layer } });
  }

  /** The shipped Built-in: { key, content, hash, source, description }. */
  getPromptBuiltIn(key) {
    return this._request('GET', `/v1.0/api/prompt-templates/${enc(key)}/builtin`);
  }

  /** Line diff of a layer against the layer below it: [{ op: same|add|remove, text }]. */
  getPromptLayerDiff(key, layer) {
    return this._request('GET', `/v1.0/api/prompt-templates/${enc(key)}/diff`, { query: { layer } });
  }

  /** Render content with a sample context; includes resolve through the layers. Returns { rendered, success, variables, includes, errors }. */
  renderPromptContent(content, context) {
    return this._request('POST', '/v1.0/api/prompt-templates/render', { body: { content, context: context || undefined } });
  }

  // ------------------------------------------------------------------ model endpoints

  /** filters: kind (Inference, Embedding), provider (a model provider), health (Unknown, Healthy, Unhealthy), search. */
  listModelEndpoints(params) {
    return this._list('/v1.0/api/model-endpoints', params);
  }

  getModelEndpoint(id) {
    return this._request('GET', `/v1.0/api/model-endpoints/${enc(id)}`);
  }

  createModelEndpoint(body) {
    return this._request('POST', '/v1.0/api/model-endpoints', { body });
  }

  updateModelEndpoint(id, body) {
    return this._request('PUT', `/v1.0/api/model-endpoints/${enc(id)}`, { body });
  }

  deleteModelEndpoint(id) {
    return this._request('DELETE', `/v1.0/api/model-endpoints/${enc(id)}`);
  }

  /**
   * Send a tiny prompt (or embed a short string) and record health. Returns { success, kind, latencyMs, dimensions,
   * model, responsePreview, statusCode, error, problems }.
   */
  validateModelEndpoint(id) {
    return this._request('POST', `/v1.0/api/model-endpoints/${enc(id)}/validate`);
  }

  /** Validate unsaved model endpoint settings (the form's Validate button); nothing is stored. */
  validateModelEndpointDraft(body) {
    return this._request('POST', '/v1.0/api/model-endpoints/validate', { body });
  }

  /**
   * The provider's model names (a bare array of strings), where the provider supports listing. 502 with code
   * ProviderUnreachable or ProviderTimeout and the provider's message when the provider cannot be reached.
   */
  listProviderModels(id) {
    return this._request('GET', `/v1.0/api/model-endpoints/${enc(id)}/models`);
  }

  /**
   * Recent health checks of an endpoint, newest first: [{ id, tenantId, modelEndpointId, status (Unknown, Healthy,
   * Unhealthy), source (Sweep, Validate), latencyMs, message, checkedUtc }]. maxResults 1 to 1000 (server default 50).
   */
  getModelEndpointHealthHistory(id, maxResults) {
    return this._request('GET', `/v1.0/api/model-endpoints/${enc(id)}/health-history`, { query: { maxResults } });
  }

  // ------------------------------------------------------------------ conversations and chat

  /** filters: agentProfileId, userId, source, search, fromUtc, toUtc, pageNumber, pageSize. */
  listConversations(params) {
    return this._pagedList('/v1.0/api/conversations', params);
  }

  getConversation(id) {
    return this._request('GET', `/v1.0/api/conversations/${enc(id)}`);
  }

  /** body: { agentProfileId, title? }. */
  createConversation(body) {
    return this._request('POST', '/v1.0/api/conversations', { body });
  }

  updateConversation(id, body) {
    return this._request('PUT', `/v1.0/api/conversations/${enc(id)}`, { body });
  }

  deleteConversation(id) {
    return this._request('DELETE', `/v1.0/api/conversations/${enc(id)}`);
  }

  listMessages(id, params = { pageNumber: 1, pageSize: 1000 }) {
    return this._pagedList(`/v1.0/api/conversations/${enc(id)}/messages`, params);
  }

  /**
   * Send a message and stream the turn (D-022 named events: turn.started, assistant.delta, assistant.thinking,
   * tool.proposed, tool.started, tool.completed, approval.required, approval.resolved, turn.completed, error).
   * Returns an AbortController; abort() stops the stream (the server cancels the turn when the client goes away).
   */
  streamMessage(id, body, handlers) {
    return this._stream('POST', `/v1.0/api/conversations/${enc(id)}/messages`, { body }, handlers);
  }

  cancelConversation(id) {
    return this._request('POST', `/v1.0/api/conversations/${enc(id)}/cancel`);
  }

  /** Export as Markdown (format=md) or JSON (format=json). Returns the text. */
  exportConversation(id, format = 'md') {
    return this._request('GET', `/v1.0/api/conversations/${enc(id)}/export`, { query: { format } });
  }

  // ------------------------------------------------------------------ tool invocations

  /**
   * filters: surface, agentProfileId, resourceId, toolName, effect, decision, status, principalId, conversationId,
   * fromUtc (inclusive), and toUtc (exclusive). The server enumerates newest first with maxResults and skip.
   */
  listToolInvocations(params = {}) {
    return this._list('/v1.0/api/tool-invocations', params);
  }

  getToolInvocation(id) {
    return this._request('GET', `/v1.0/api/tool-invocations/${enc(id)}`);
  }

  /**
   * Counts since a time: { total, sinceUtc, byDecision, byStatus, byEffect, byTool }, each a list of
   * { status, count }. query: { fromUtc } (sent as since).
   */
  getToolInvocationSummary(query = {}) {
    return this._request('GET', '/v1.0/api/tool-invocations/summary', { query: { since: query.fromUtc || query.since } });
  }

  /**
   * Resolve, authorize, validate, classify, and apply rules without running or recording anything. body: { profileId,
   * resource (alias), toolName, arguments }. Returns the outcome: { decision, decisionCode, reason, effect,
   * effectReason, accessMode, bindingAlias, approvalSummary }.
   */
  dryRunTool(body) {
    return this._request('POST', '/v1.0/api/tool-invocations/dry-run', { body });
  }

  /**
   * Invoke a tool as a profile through the same pipeline as every other surface. Answers 200 with the outcome
   * (including denials, whose decision is Denied), or 202 with approvalId and approvalStatus Pending.
   */
  invokeTool(body) {
    return this._request('POST', '/v1.0/api/tool-invocations', { body });
  }

  // ------------------------------------------------------------------ approvals

  /**
   * filters: status, effect, agentProfileId, resourceId, principalId, toolName, fromUtc (inclusive) and toUtc
   * (exclusive) on the created time, pageNumber, pageSize.
   */
  listApprovals(params) {
    return this._list('/v1.0/api/approvals', params);
  }

  /** One approval with its tool invocation (and the result once executed). */
  getApproval(id) {
    return this._request('GET', `/v1.0/api/approvals/${enc(id)}`);
  }

  /** 409 NotPending when already decided; 409 SelfApprovalNotAllowed when self-approval is off for the tenant. */
  approveApproval(id, note) {
    return this._request('POST', `/v1.0/api/approvals/${enc(id)}/approve`, { body: { note: note || undefined } });
  }

  rejectApproval(id, note) {
    return this._request('POST', `/v1.0/api/approvals/${enc(id)}/reject`, { body: { note: note || undefined } });
  }

  cancelApproval(id, note) {
    return this._request('POST', `/v1.0/api/approvals/${enc(id)}/cancel`, { body: { note: note || undefined } });
  }

  /** Number of approvals waiting for a person (one-row page, totalCount). */
  async countPendingApprovals() {
    const page = await this.listApprovals({ status: 'Pending', pageNumber: 1, pageSize: 1 });
    return page.totalCount ?? 0;
  }

  // ------------------------------------------------------------------ knowledge (M2)

  listKnowledge(params) {
    return this._pagedList('/v1.0/api/knowledge', params);
  }

  createKnowledge(body) {
    return this._request('POST', '/v1.0/api/knowledge', { body });
  }

  updateKnowledge(id, body) {
    return this._request('PUT', `/v1.0/api/knowledge/${enc(id)}`, { body });
  }

  deleteKnowledge(id) {
    return this._request('DELETE', `/v1.0/api/knowledge/${enc(id)}`);
  }

  /** body: { query, agentProfileId?, resourceId?, topK? }. */
  searchKnowledge(body) {
    return this._request('POST', '/v1.0/api/knowledge/search', { body });
  }

  // ------------------------------------------------------------------ analytics

  /**
   * One analytics report (kind: overview, tokens, tools, or models). query: { fromUtc, toUtc, interval (minute,
   * hour, day) }, sent as from, to, and interval. Returns { kind, fromUtc, toUtc, interval, conversations,
   * approvalsRequested, totals, buckets: [{ startUtc, turns, inputTokens, outputTokens, toolCalls, toolErrors,
   * approvals, avgTimeToFirstTokenMs, avgTurnDurationMs }], tools (tools report), models (models report) }. The
   * server fills every bucket in the window.
   */
  getAnalyticsReport(kind, query = {}) {
    const { fromUtc, toUtc, interval } = query || {};
    return this._request('GET', `/v1.0/api/analytics/${enc(kind)}`, { query: { from: fromUtc, to: toUtc, interval } });
  }

  getAnalyticsOverview(query) {
    return this.getAnalyticsReport('overview', query);
  }

  getAnalyticsTokens(query) {
    return this.getAnalyticsReport('tokens', query);
  }

  getAnalyticsTools(query) {
    return this.getAnalyticsReport('tools', query);
  }

  getAnalyticsModels(query) {
    return this.getAnalyticsReport('models', query);
  }

  // ------------------------------------------------------------------ plugins (system administrators)

  listPlugins(params) {
    return this._pagedList('/v1.0/api/system/plugins', params);
  }

  updatePlugin(id, body) {
    return this._request('PUT', `/v1.0/api/system/plugins/${enc(id)}`, { body });
  }

  // ------------------------------------------------------------------ MCP and A2A explorers

  /**
   * Send one JSON-RPC message to the MCP endpoint (/mcp or /mcp/{profile}). Returns the raw Response, which may be
   * JSON or an SSE stream (streamable HTTP). sessionId is sent as Mcp-Session-Id once the server assigns one.
   */
  mcpRequest(profile, message, { sessionId, signal } = {}) {
    const headers = this._headers({ Accept: 'application/json, text/event-stream' });
    if (sessionId) headers['Mcp-Session-Id'] = sessionId;
    const path = profile ? `/mcp/${enc(profile)}` : '/mcp';
    return fetch(this._url(path), { method: 'POST', headers, body: JSON.stringify(message), signal });
  }

  /** Public agent card of an exposed profile. */
  getA2aCard(tenantId, profile) {
    return this._request('GET', `/v1.0/a2a/${enc(tenantId)}/${enc(profile)}/.well-known/agent-card.json`);
  }

  /** Cards of every exposed profile in a tenant. */
  listA2aCards(tenantId) {
    return this._request('GET', `/v1.0/a2a/${enc(tenantId)}`);
  }

  /** Send one A2A JSON-RPC request. Returns the raw Response (JSON, or SSE for SendStreamingMessage). */
  a2aRequest(tenantId, profile, message, { signal } = {}) {
    const headers = this._headers({ Accept: 'application/json, text/event-stream' });
    return fetch(this._url(`/v1.0/a2a/${enc(tenantId)}/${enc(profile)}`), { method: 'POST', headers, body: JSON.stringify(message), signal });
  }

  // ------------------------------------------------------------------ OpenAPI and API Explorer

  getOpenApiSpec() {
    return this._request('GET', '/openapi.json');
  }

  /** Execute a request built by the API Explorer. Returns the raw Response for streaming access. */
  executeExplorer({ method, path, query, headers, body, signal }) {
    const finalHeaders = this._headers(headers || {});
    const hasBody = body !== null && body !== undefined && body !== '';
    if (!hasBody) delete finalHeaders['Content-Type'];
    return fetch(this._url(path, query), { method, headers: finalHeaders, body: hasBody ? body : undefined, signal });
  }

  // ------------------------------------------------------------------ streaming

  /**
   * Fetch-based Server-Sent Events (EventSource cannot send the bearer header). Calls handlers.onEvent({ event,
   * data, id }) for every frame, handlers.onOpen(response) once the response arrives, and handlers.onEnd(error|null)
   * when the stream ends. Returns an AbortController.
   */
  _stream(method, path, { body, query } = {}, handlers = {}) {
    const controller = new AbortController();
    const headers = this._headers({ Accept: 'text/event-stream' });
    if (body === undefined || body === null) delete headers['Content-Type'];
    fetch(this._url(path, query), { method, headers, body: body === undefined || body === null ? undefined : JSON.stringify(body), signal: controller.signal })
      .then(async (response) => {
        if (!response.ok) {
          const text = await response.text().catch(() => '');
          let parsed = text;
          try {
            parsed = text ? JSON.parse(text) : response.statusText;
          } catch {
            /* plain text */
          }
          throw new ApiError(response.status, parsed, response.headers?.get?.('x-request-id'));
        }
        handlers.onOpen?.(response);
        const type = response.headers?.get?.('content-type') || '';
        if (!type.includes('text/event-stream') || !response.body) {
          // A server that answers with JSON (no streaming) delivers one synthetic event.
          const text = await response.text().catch(() => '');
          let data = text;
          try {
            data = text ? JSON.parse(text) : null;
          } catch {
            /* plain text */
          }
          handlers.onEvent?.({ event: 'message', data, id: null });
          handlers.onEnd?.(null);
          return;
        }
        const reader = response.body.getReader();
        const decoder = new TextDecoder();
        let buffer = '';
        for (;;) {
          const { value, done } = await reader.read();
          if (done) break;
          buffer += decoder.decode(value, { stream: true }).replace(/\r\n/g, '\n');
          const frames = buffer.split('\n\n');
          buffer = frames.pop() || '';
          for (const frame of frames) {
            const evt = parseSseEvent(frame);
            if (evt) handlers.onEvent?.(evt);
          }
        }
        if (buffer.trim()) {
          const evt = parseSseEvent(buffer);
          if (evt) handlers.onEvent?.(evt);
        }
        handlers.onEnd?.(null);
      })
      .catch((e) => {
        if (e?.name === 'AbortError') handlers.onEnd?.(null, { aborted: true });
        else handlers.onEnd?.(e instanceof ApiError ? e : new ApiError(0, { code: 'NetworkError', message: e?.message || 'Network error' }));
      });
    return controller;
  }
}

/**
 * Parse one SSE frame into { event, data, id }. `event` defaults to "message"; data is parsed as JSON when it can
 * be. Comment-only frames (keep-alives) return null.
 */
export function parseSseEvent(frame) {
  const lines = String(frame || '').split('\n');
  let event = 'message';
  let id = null;
  const data = [];
  for (const line of lines) {
    if (!line || line.startsWith(':')) continue;
    const colon = line.indexOf(':');
    const field = colon === -1 ? line : line.slice(0, colon);
    const value = colon === -1 ? '' : line.slice(colon + 1).replace(/^ /, '');
    if (field === 'event') event = value;
    else if (field === 'data') data.push(value);
    else if (field === 'id') id = value;
  }
  if (data.length === 0 && event === 'message') return null;
  const text = data.join('\n');
  let parsed = text;
  try {
    parsed = text ? JSON.parse(text) : null;
  } catch {
    /* plain text */
  }
  return { event, data: parsed, id };
}

/** Parse one SSE frame; returns the parsed JSON (or raw text) of its data lines, or null. */
export function parseSseFrame(frame) {
  const evt = parseSseEvent(frame);
  return evt ? evt.data : null;
}

/**
 * Normalize an EnumerationResult ({ objects, totalRecords, maxResults, skip }), a page result ({ items | objects,
 * pageNumber, pageSize, totalCount }), or a bare array into { items, pageNumber, pageSize, totalCount }.
 */
export function normalizeEnumeration(result, requestedPageNumber, requestedPageSize) {
  const objects = Array.isArray(result?.items) ? result.items : Array.isArray(result?.objects) ? result.objects : Array.isArray(result) ? result : [];
  if (result && !Array.isArray(result) && result.totalCount !== undefined && result.pageNumber !== undefined) {
    const pageSize = result.pageSize ?? requestedPageSize ?? (objects.length || 25);
    return {
      items: objects,
      pageNumber: result.pageNumber,
      pageSize,
      totalCount: result.totalCount,
      endOfResults: result.pageNumber >= (result.totalPages ?? Math.ceil(result.totalCount / pageSize))
    };
  }
  const total = result?.totalRecords ?? result?.totalCount ?? objects.length;
  const size = result?.maxResults ?? requestedPageSize ?? (objects.length || 25);
  const skip = result?.skip ?? 0;
  const pageNumber = size > 0 ? Math.floor(skip / size) + 1 : requestedPageNumber || 1;
  return {
    items: objects,
    pageNumber,
    pageSize: size,
    totalCount: total,
    endOfResults: result?.endOfResults ?? true
  };
}

/** out_of_range -> OutOfRange; PascalCase codes pass through. */
export function pascalCode(code) {
  if (typeof code !== 'string' || !code.includes('_')) return code;
  return code
    .split('_')
    .filter(Boolean)
    .map((part) => part[0].toUpperCase() + part.slice(1))
    .join('');
}

/**
 * Normalized API error: status, stable code, message, request ID, trace ID, and field errors. Accepts both the
 * Garrison error body ({ code, message, fields }) and the Hugo body ({ error, message, requestId, traceId,
 * details: [{ field, code, message }] }).
 */
export class ApiError extends Error {
  constructor(status, body, headerRequestId = null) {
    const obj = body && typeof body === 'object' ? body : null;
    const message = obj?.message || (typeof body === 'string' && body) || `HTTP ${status}`;
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.code = obj?.code || (typeof obj?.error === 'string' ? obj.error : null) || (status === 0 ? 'NetworkError' : `Http${status}`);
    this.requestId = obj?.requestId || headerRequestId || null;
    this.traceId = obj?.traceId || null;
    let fields = obj?.fields || (obj?.errors && typeof obj.errors === 'object' && !Array.isArray(obj.errors) ? obj.errors : null);
    let fieldCodes = obj?.fieldCodes && typeof obj.fieldCodes === 'object' ? obj.fieldCodes : null;
    if (!fields && Array.isArray(obj?.details)) {
      fields = {};
      fieldCodes = {};
      for (const d of obj.details) {
        if (!d?.field) continue;
        fields[d.field] = d.message || d.code || '';
        if (d.code) fieldCodes[d.field] = d.code;
      }
      if (Object.keys(fields).length === 0) fields = null;
    }
    this.fields = fields;
    // Connector setting validation reports snake_case codes (out_of_range); the catalog keys are PascalCase.
    this.fieldCodes = fieldCodes ? Object.fromEntries(Object.entries(fieldCodes).map(([k, v]) => [k, pascalCode(v)])) : fieldCodes;
    this.requiredPermission = obj?.requiredPermission || null;
    this.resourceType = obj?.resourceType || null;
    this.body = body;
  }
}

export default ApiClient;
