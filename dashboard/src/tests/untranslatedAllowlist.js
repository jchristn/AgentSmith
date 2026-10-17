// Values a translated catalog may leave identical to English.
//
// isProductTerm() accepts strings made only of product names, protocol names, acronyms, units, numbers,
// placeholders, and punctuation: those stay in English in every language (docs/I18N.md, Glossary).
// UNTRANSLATED_ALLOWLIST lists whole values that are legitimately spelled the same as English in one language.
// Keep both lists short; anything else is a missed translation.

const PRODUCT_WORDS = new Set([
  'Hugo',
  'Agent',
  'Smith',
  'Anderson',
  'Mr',
  'AgentSmith',
  'Claude',
  'Code',
  'Codex',
  'Anthropic',
  'OpenAI',
  'GitHub',
  'Ollama',
  'Gemini',
  'Azure',
  'Bedrock',
  'Vertex',
  'AI',
  'Voyage',
  'Amazon',
  'Kubernetes',
  'Docker',
  'chat',
  'Prometheus',
  'Grafana',
  'Tempo',
  'Loki',
  'SolarWinds',
  'Redis',
  'PostgreSQL',
  'MySQL',
  'SQLite',
  'S3',
  'SSH',
  'TLS',
  'PEM',
  'OpenTelemetry',
  'OpenAPI',
  'OAuth',
  'MCP',
  'A2A',
  'REST',
  'API',
  'APIs',
  'SSE',
  'JSON',
  'YAML',
  'URL',
  'URLs',
  'HTTP',
  'HTTPS',
  'SDK',
  'ID',
  'IDs',
  'UTC',
  'CORS',
  'CLI',
  'CSV',
  'PNG',
  'SVG',
  'HMAC',
  'SHA',
  'IP',
  'p50',
  'p90',
  'p95',
  'p99',
  'ms',
  's',
  'min',
  'h',
  'KB',
  'MB',
  'GB',
  'TB',
  'B',
  'v',
  'compatible',
  'Kubeconfig',
  'kubeconfig',
  'hugo',
  'json',
  'stdio',
  'pgvector'
]);

/** True when every word in the value is a product, protocol, or unit term that stays in English. */
export function isProductTerm(value) {
  const words = String(value)
    .replace(/\{\{[^}]+\}\}/g, ' ')
    .split(/[^A-Za-z0-9]+/)
    .filter((w) => /[A-Za-z]/.test(w));
  return words.every((w) => PRODUCT_WORDS.has(w));
}

export const UNTRANSLATED_ALLOWLIST = {
  '*': ['Alias', 'Audit', 'Editor', 'Streaming', 'Prompts', 'Tokens', 'Token']
};
