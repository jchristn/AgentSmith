/**
 * API client for the Hugo server.
 *
 * Hand-rolled over the browser fetch API (no axios). Every backend endpoint the dashboard uses is a named method
 * here, so contract changes are fixed in one place. List endpoints return the normalized page shape
 * { items, pageNumber, pageSize, totalCount }. Errors throw ApiError with the parsed error body.
 *
 * Two route families:
 *  - Identity and administration routes forked from Garrison keep Garrison's paths and JSON shapes
 *    (`/v1.0/api/tenants/{tenantId}/users` and so on), per plan decision D-032.
 *  - Hugo's own routes follow plan section 14.2 (`/v1.0/api/resources`, `/v1.0/api/agent-profiles`, ...). They are
 *    tenant-implicit: the server takes the tenant from the session.
 * A few routes that moved between Garrison and Hugo (server info, restart) try the Hugo path first and fall back to
 * the Garrison path on 404.
 */

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

  /** Change a user's password (forced change after first login, or reset by an administrator). */
  changePassword(userId, body) {
    return this._request('POST', `/v1.0/api/users/${enc(userId)}/password`, { body });
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

  listSessions(params) {
    return this._pagedList('/v1.0/api/sessions', params);
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

  listSecrets(params) {
    return this._pagedList('/v1.0/api/secrets', params);
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

  /** Replace the supplied fields; omitted fields are kept. body: { values: { field: value } }. */
  updateSecretValues(id, values) {
    return this._request('PUT', `/v1.0/api/secrets/${enc(id)}/values`, { body: { values } });
  }

  /** Reveal plaintext values. Needs Reveal on Secret, a reason, and a recent re-authentication. Audited. */
  revealSecret(id, reason) {
    // A 401 here means the session needs re-authentication, not that it ended, so it must not log the user out.
    return this._request('POST', `/v1.0/api/secrets/${enc(id)}/reveal`, { body: { reason }, keepSessionOn401: true });
  }

  /** Resources and model endpoints that use a secret. */
  getSecretReferrers(id) {
    return this._request('GET', `/v1.0/api/secrets/${enc(id)}/referrers`);
  }

  /** 409 while the secret is referenced. */
  deleteSecret(id) {
    return this._request('DELETE', `/v1.0/api/secrets/${enc(id)}`);
  }

  // ------------------------------------------------------------------ connectors

  /** Connector descriptors: type, family, displayName, description, category, settingsSchema, secretSlots, tools. */
  listConnectors(params) {
    return this._pagedList('/v1.0/api/connectors', params);
  }

  getConnector(type) {
    return this._request('GET', `/v1.0/api/connectors/${enc(type)}`);
  }

  // ------------------------------------------------------------------ resources

  listResources(params) {
    return this._pagedList('/v1.0/api/resources', params);
  }

  getResource(id) {
    return this._request('GET', `/v1.0/api/resources/${enc(id)}`);
  }

  createResource(body) {
    return this._request('POST', '/v1.0/api/resources', { body });
  }

  updateResource(id, body) {
    return this._request('PUT', `/v1.0/api/resources/${enc(id)}`, { body });
  }

  deleteResource(id) {
    return this._request('DELETE', `/v1.0/api/resources/${enc(id)}`);
  }

  /** Test a saved resource's connection. Returns { success, message, latencyMs, details }. */
  testResource(id) {
    return this._request('POST', `/v1.0/api/resources/${enc(id)}/test`);
  }

  /** Test unsaved settings (the create and edit form's Test Connection). */
  testResourceSettings(body) {
    return this._request('POST', '/v1.0/api/resources/test', { body });
  }

  /** Profiles and bindings that use a resource. */
  getResourceReferrers(id) {
    return this._request('GET', `/v1.0/api/resources/${enc(id)}/referrers`);
  }

  duplicateResource(id, body) {
    return this._request('POST', `/v1.0/api/resources/${enc(id)}/duplicate`, { body });
  }

  // ------------------------------------------------------------------ agent profiles and bindings

  listProfiles(params) {
    return this._pagedList('/v1.0/api/agent-profiles', params);
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

  duplicateProfile(id, body) {
    return this._request('POST', `/v1.0/api/agent-profiles/${enc(id)}/duplicate`, { body });
  }

  /** Compiled instructions as a caller receives them. query: { surface }. Returns { text, sections: [...] }. */
  getCompiledInstructions(id, query) {
    return this._request('GET', `/v1.0/api/agent-profiles/${enc(id)}/compiled-instructions`, { query });
  }

  /** Tools as the caller would see them. */
  getProfileTools(id) {
    return this._request('GET', `/v1.0/api/agent-profiles/${enc(id)}/tools`);
  }

  /** A2A agent card preview. */
  getProfileCard(id) {
    return this._request('GET', `/v1.0/api/agent-profiles/${enc(id)}/card`);
  }

  listBindings(profileId, params) {
    return this._pagedList(`/v1.0/api/agent-profiles/${enc(profileId)}/bindings`, params);
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

  getToolRules(profileId, bindingId) {
    return this._request('GET', `/v1.0/api/agent-profiles/${enc(profileId)}/bindings/${enc(bindingId)}/tool-rules`);
  }

  updateToolRules(profileId, bindingId, rules) {
    return this._request('PUT', `/v1.0/api/agent-profiles/${enc(profileId)}/bindings/${enc(bindingId)}/tool-rules`, { body: { rules } });
  }

  // ------------------------------------------------------------------ prompt templates (Garrison routes)

  listPromptTemplates(tenantId, params) {
    return this._list(this._t(tenantId, 'prompt-templates'), params);
  }

  getPromptTemplate(tenantId, id) {
    return this._request('GET', this._t(tenantId, `prompt-templates/${enc(id)}`));
  }

  createPromptTemplate(tenantId, body) {
    return this._request('POST', this._t(tenantId, 'prompt-templates'), { body });
  }

  /** Update a template. The server stores a new immutable version. */
  updatePromptTemplate(tenantId, id, body) {
    return this._request('PUT', this._t(tenantId, `prompt-templates/${enc(id)}`), { body });
  }

  deletePromptTemplate(tenantId, id) {
    return this._request('DELETE', this._t(tenantId, `prompt-templates/${enc(id)}`));
  }

  clonePromptTemplate(tenantId, id, body) {
    return this._request('POST', this._t(tenantId, `prompt-templates/${enc(id)}/clone`), { body });
  }

  resetPromptTemplate(tenantId, id) {
    return this._request('POST', this._t(tenantId, `prompt-templates/${enc(id)}/reset`));
  }

  listPromptTemplateVersions(tenantId, id) {
    return this._request('GET', this._t(tenantId, `prompt-templates/${enc(id)}/versions`));
  }

  getPromptTemplateVersion(tenantId, id, version) {
    return this._request('GET', this._t(tenantId, `prompt-templates/${enc(id)}/versions/${enc(version)}`));
  }

  restorePromptTemplateVersion(tenantId, id, version) {
    return this._request('POST', this._t(tenantId, `prompt-templates/${enc(id)}/restore/${enc(version)}`));
  }

  /** Render a stored template with a sample context. Returns rendered text and variable usage. */
  renderPromptTemplate(tenantId, id, body) {
    return this._request('POST', this._t(tenantId, `prompt-templates/${enc(id)}/render`), { body });
  }

  /** Render ad hoc template content (used by the editor preview). */
  renderTemplateContent(tenantId, body) {
    return this._request('POST', this._t(tenantId, 'prompt-templates/render'), { body });
  }

  // ------------------------------------------------------------------ model endpoints

  listModelEndpoints(params) {
    return this._pagedList('/v1.0/api/model-endpoints', params);
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

  /** Send a tiny prompt (or embed a short string); returns { success, latencyMs, dimensions, message }. */
  validateModelEndpoint(id) {
    return this._request('POST', `/v1.0/api/model-endpoints/${enc(id)}/validate`);
  }

  /** The provider's model list, where the provider supports it. */
  listProviderModels(id) {
    return this._request('GET', `/v1.0/api/model-endpoints/${enc(id)}/models`);
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

  /** filters: surface, agentProfileId, resourceId, toolName, effect, decision, status, principalId, fromUtc, toUtc. */
  listToolInvocations(params) {
    return this._pagedList('/v1.0/api/tool-invocations', params);
  }

  getToolInvocation(id) {
    return this._request('GET', `/v1.0/api/tool-invocations/${enc(id)}`);
  }

  getToolInvocationSummary(query) {
    return this._request('GET', '/v1.0/api/tool-invocations/summary', { query });
  }

  /** Classify a call without running it: returns { effect, effectReason, decision, accessMode }. */
  dryRunTool(body) {
    return this._request('POST', '/v1.0/api/tool-invocations/dry-run', { body });
  }

  /** Invoke a tool as a profile through the same pipeline as every other surface. 202 when approval is required. */
  invokeTool(body) {
    return this._request('POST', '/v1.0/api/tool-invocations', { body });
  }

  // ------------------------------------------------------------------ approvals

  /** filters: status, effect, agentProfileId, requestedBy, fromUtc, toUtc, pageNumber, pageSize. */
  listApprovals(params) {
    return this._pagedList('/v1.0/api/approvals', params);
  }

  getApproval(id) {
    return this._request('GET', `/v1.0/api/approvals/${enc(id)}`);
  }

  approveApproval(id, note) {
    return this._request('POST', `/v1.0/api/approvals/${enc(id)}/approve`, { body: { note: note || undefined } });
  }

  rejectApproval(id, note) {
    return this._request('POST', `/v1.0/api/approvals/${enc(id)}/reject`, { body: { note: note || undefined } });
  }

  cancelApproval(id) {
    return this._request('POST', `/v1.0/api/approvals/${enc(id)}/cancel`);
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

  /** query: { fromUtc, toUtc, bucketMinutes, agentProfileId, modelEndpointId }. */
  getAnalyticsOverview(query) {
    return this._request('GET', '/v1.0/api/analytics/overview', { query });
  }

  getAnalyticsTokens(query) {
    return this._request('GET', '/v1.0/api/analytics/tokens', { query });
  }

  getAnalyticsTools(query) {
    return this._request('GET', '/v1.0/api/analytics/tools', { query });
  }

  getAnalyticsModels(query) {
    return this._request('GET', '/v1.0/api/analytics/models', { query });
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
    this.fieldCodes = fieldCodes;
    this.requiredPermission = obj?.requiredPermission || null;
    this.resourceType = obj?.resourceType || null;
    this.body = body;
  }
}

export default ApiClient;
