// OpenAPI parsing helpers for the API Explorer.

const HTTP_METHODS = ['get', 'post', 'put', 'delete', 'patch', 'head', 'options'];

/** Resolve a local $ref against the spec's components. */
function resolveRef(ref, spec) {
  if (!ref || !ref.startsWith('#/')) return null;
  const parts = ref.slice(2).split('/');
  let node = spec;
  for (const p of parts) {
    node = node?.[p];
    if (node === undefined) return null;
  }
  return node;
}

/** Flatten a spec into a list of operations grouped later by tag. */
export function flattenOpenApiSpec(spec) {
  if (!spec || !spec.paths) return [];
  const operations = [];
  for (const [path, pathItem] of Object.entries(spec.paths)) {
    const pathParameters = pathItem.parameters || [];
    for (const [method, op] of Object.entries(pathItem)) {
      if (!HTTP_METHODS.includes(method)) continue;
      operations.push({
        id: op.operationId || `${method.toUpperCase()} ${path}`,
        method: method.toUpperCase(),
        path,
        tags: op.tags && op.tags.length ? op.tags : ['Ungrouped'],
        summary: op.summary || op.description || '',
        parameters: [...pathParameters, ...(op.parameters || [])],
        requestBody: op.requestBody || null,
        responses: op.responses || {}
      });
    }
  }
  operations.sort((a, b) => `${a.tags[0]} ${a.path} ${a.method}`.localeCompare(`${b.tags[0]} ${b.path} ${b.method}`));
  return operations;
}

/** Group operations by their first tag. */
export function groupOperations(operations) {
  const grouped = {};
  for (const op of operations) {
    const tag = op.tags[0];
    (grouped[tag] = grouped[tag] || []).push(op);
  }
  return grouped;
}

/** A sensible default value for a parameter (example, default, enum[0], or ''). */
export function getParameterDefault(parameter) {
  const schema = parameter.schema || {};
  if (parameter.example !== undefined) return String(parameter.example);
  if (schema.example !== undefined) return String(schema.example);
  if (schema.default !== undefined) return String(schema.default);
  if (Array.isArray(schema.enum) && schema.enum.length) return String(schema.enum[0]);
  return '';
}

/** Build an example JSON body string from a requestBody schema (resolves $ref/allOf). */
export function getRequestBodyTemplate(requestBody, spec) {
  if (!requestBody || !requestBody.content) return '';
  const contentType = Object.keys(requestBody.content)[0];
  if (!contentType || !contentType.includes('json')) return '';
  let schema = requestBody.content[contentType].schema;
  if (!schema) return '{\n  \n}';
  const example = sampleFromSchema(schema, spec, 0);
  try {
    return JSON.stringify(example, null, 2);
  } catch {
    return '{\n  \n}';
  }
}

function sampleFromSchema(schema, spec, depth) {
  if (!schema || depth > 6) return null;
  if (schema.$ref) return sampleFromSchema(resolveRef(schema.$ref, spec), spec, depth + 1);
  if (schema.example !== undefined) return schema.example;
  if (schema.default !== undefined) return schema.default;
  if (Array.isArray(schema.allOf)) {
    return schema.allOf.reduce((acc, s) => ({ ...acc, ...(sampleFromSchema(s, spec, depth + 1) || {}) }), {});
  }
  if (Array.isArray(schema.enum) && schema.enum.length) return schema.enum[0];

  switch (schema.type) {
    case 'object': {
      const obj = {};
      const props = schema.properties || {};
      for (const [key, prop] of Object.entries(props)) obj[key] = sampleFromSchema(prop, spec, depth + 1);
      return obj;
    }
    case 'array':
      return [sampleFromSchema(schema.items, spec, depth + 1)].filter((x) => x !== null);
    case 'integer':
    case 'number':
      return 0;
    case 'boolean':
      return false;
    case 'string':
      return schema.format === 'date-time' ? new Date().toISOString() : '';
    default:
      if (schema.properties) {
        const obj = {};
        for (const [key, prop] of Object.entries(schema.properties)) obj[key] = sampleFromSchema(prop, spec, depth + 1);
        return obj;
      }
      return null;
  }
}

/** Substitute path params into a path template. */
export function resolvePath(template, params) {
  return String(template).replace(/\{([^}]+)\}/g, (_, k) => encodeURIComponent(params[k] || `{${k}}`));
}

/** Build a full URL from base + path + query object. */
export function buildUrl(baseUrl, path, query) {
  const base = (baseUrl || '').replace(/\/+$/, '');
  const qs = new URLSearchParams();
  for (const [k, v] of Object.entries(query || {})) {
    if (v !== undefined && v !== null && v !== '') qs.append(k, v);
  }
  const suffix = qs.toString();
  return `${base}${path}${suffix ? `?${suffix}` : ''}`;
}

/** True for operations that should require confirmation before executing. */
export function isDestructive(op) {
  return op.method === 'DELETE' || op.path.includes('/bulk');
}

/** Build curl / fetch / C# HttpClient snippets for a composed request. */
export function buildCodeSnippets({ method, url, headers, body }) {
  const hasBody = body !== null && body !== undefined && body !== '';
  const headerEntries = Object.entries(headers || {}).filter(([, v]) => v !== undefined && v !== '');

  const curlLines = [`curl -X ${method} '${url}'`];
  for (const [k, v] of headerEntries) curlLines.push(`  -H '${k}: ${v}'`);
  if (hasBody) curlLines.push(`  -d '${String(body).replace(/'/g, "'\\''")}'`);
  const curl = curlLines.join(' \\\n');

  const fetchHeaders = JSON.stringify(Object.fromEntries(headerEntries), null, 2);
  const fetchLines = [
    `await fetch('${url}', {`,
    `  method: '${method}',`,
    `  headers: ${fetchHeaders.replace(/\n/g, '\n  ')},`
  ];
  if (hasBody) fetchLines.push(`  body: ${JSON.stringify(String(body))}`);
  fetchLines.push('});');
  const fetchSnippet = fetchLines.join('\n');

  const csLines = [
    'using var client = new HttpClient();',
    `var request = new HttpRequestMessage(new HttpMethod("${method}"), "${url}");`
  ];
  for (const [k, v] of headerEntries) {
    if (k.toLowerCase() === 'content-type') continue;
    csLines.push(`request.Headers.TryAddWithoutValidation("${k}", "${v}");`);
  }
  if (hasBody) {
    const ct = headerEntries.find(([k]) => k.toLowerCase() === 'content-type')?.[1] || 'application/json';
    csLines.push(`request.Content = new StringContent(${JSON.stringify(String(body))}, System.Text.Encoding.UTF8, "${ct}");`);
  }
  csLines.push('var response = await client.SendAsync(request);');
  csLines.push('var responseBody = await response.Content.ReadAsStringAsync();');
  const csharp = csLines.join('\n');

  return { curl, fetch: fetchSnippet, csharp };
}
