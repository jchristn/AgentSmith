// Connection snippets shown after creating a credential and on a profile's Connect tab (plan sections 14.3 and 14.4).
// Values the operator must keep secret are referenced through environment variables, never pasted into the text.

/** Base URL without a trailing slash; falls back to the local default listener. */
function base(serverUrl) {
  return String(serverUrl || 'http://127.0.0.1:8800').replace(/\/$/, '');
}

/**
 * Claude Code (and other MCP clients) configuration for Hugo's streamable HTTP endpoint. With a credential the
 * snippet uses the access key headers; the secret key is read from HUGO_SECRET_KEY. Without one it uses a bearer
 * token from HUGO_TOKEN. `profile` selects /mcp/{profile}.
 */
export function mcpConfigSnippet({ serverUrl, accessKey, profile } = {}) {
  const url = `${base(serverUrl)}/mcp${profile ? `/${encodeURIComponent(profile)}` : ''}`;
  const headers = accessKey ? { 'x-access-key': accessKey, 'x-secret-key': '${HUGO_SECRET_KEY}' } : { Authorization: 'Bearer ${HUGO_TOKEN}' };
  return JSON.stringify({ mcpServers: { hugo: { type: 'http', url, headers } } }, null, 2);
}

/** The claude CLI command that registers the same server. */
export function claudeCliSnippet({ serverUrl, accessKey, profile } = {}) {
  const url = `${base(serverUrl)}/mcp${profile ? `/${encodeURIComponent(profile)}` : ''}`;
  const headers = accessKey
    ? `--header "x-access-key: ${accessKey}" --header "x-secret-key: $HUGO_SECRET_KEY"`
    : '--header "Authorization: Bearer $HUGO_TOKEN"';
  return `claude mcp add --transport http hugo ${url} ${headers}`;
}

/** The stdio relay for clients that only speak stdio (plan section 14.3). */
export function stdioSnippet({ serverUrl, profile } = {}) {
  return `HUGO_ACCESS_KEY=... HUGO_SECRET_KEY=... hugo mcp stdio --server ${base(serverUrl)}${profile ? ` --profile ${profile}` : ''}`;
}

/** A2A agent card URL for an exposed profile. */
export function a2aCardUrl({ serverUrl, tenantId, profile }) {
  return `${base(serverUrl)}/v1.0/a2a/${encodeURIComponent(tenantId || 'default')}/${encodeURIComponent(profile || 'Hugo')}/.well-known/agent-card.json`;
}

/** OpenAI-compatible base URL (the profile name is the model ID). */
export function openAiBaseUrl({ serverUrl }) {
  return `${base(serverUrl)}/v1.0/openai/v1`;
}
