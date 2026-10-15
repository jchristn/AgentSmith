// Runtime configuration. The container entrypoint rewrites this file from environment variables.
// apiUrl empty means the page origin (nginx proxies /v1.0, /mcp, and /openapi.json to hugo-server).
window.HUGO_CONFIG = window.HUGO_CONFIG || { apiUrl: '' };
