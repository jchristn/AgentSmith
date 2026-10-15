// Template helpers mirroring the server's logic-light syntax (plan section 10.3): {{path}}, {{path | default: "x"}},
// {{#if path}}...{{else}}...{{/if}}, {{#each path}}...{{/each}}, {{> include}}, and {{{{raw}}}} blocks.

const KEYWORDS = new Set(['else', 'this']);

/**
 * Extract variable paths referenced by a template: {{a.b}}, {{a | default: "x"}}, {{#if a}}, {{#each a}}.
 * Includes ({{> name}}), closing tags, and raw blocks are ignored.
 * @param {string} content
 * @returns {string[]} Sorted unique variable paths.
 */
export function extractVariables(content) {
  const found = new Set();
  const text = String(content || '').replace(/\{\{\{\{raw\}\}\}\}[\s\S]*?\{\{\{\{\/raw\}\}\}\}/g, '');
  const re = /\{\{\s*([#/>]?)\s*([^}]*?)\s*\}\}/g;
  let m;
  while ((m = re.exec(text))) {
    const sigil = m[1];
    let expr = m[2];
    if (sigil === '/' || sigil === '>') continue;
    if (sigil === '#') expr = expr.replace(/^(if|each|unless)\s+/, '');
    const path = expr.split('|')[0].trim();
    if (!path || KEYWORDS.has(path) || path.startsWith('this.') || path.startsWith('"')) continue;
    if (/^[A-Za-z_][\w.]*$/.test(path)) found.add(path);
  }
  return [...found].sort();
}

function lookup(context, path) {
  return String(path)
    .split('.')
    .reduce((node, part) => (node !== null && node !== undefined && typeof node === 'object' ? node[part] : undefined), context);
}

function truthy(value) {
  if (Array.isArray(value)) return value.length > 0;
  return !!value;
}

/**
 * Render a template locally for previews when the server's render route is unavailable. Supports substitution,
 * `| default: "x"`, #if/#unless with else, and #each (with {{this}} and {{this.field}}). Includes render as a
 * visible marker. Returns { rendered, missingVariables }.
 */
export function renderTemplateLocal(content, context = {}) {
  const missing = new Set();
  const renderBlock = (text, ctx) => {
    let out = String(text || '');
    // Sections, innermost first.
    const section = /\{\{#(if|unless|each)\s+([\w.]+)\s*\}\}((?:(?!\{\{#(?:if|unless|each)\s)[\s\S])*?)\{\{\/\1\}\}/;
    let m;
    let guard = 0;
    while ((m = section.exec(out)) && guard++ < 500) {
      const [whole, kind, path, body] = m;
      const value = path === 'this' ? ctx : path.startsWith('this.') ? lookup(ctx, path.slice(5)) : lookup(ctx, path);
      const [yes, no = ''] = body.split(/\{\{else\}\}/);
      let replacement = '';
      if (kind === 'each') replacement = Array.isArray(value) ? value.map((item) => renderBlock(yes, item)).join('') : '';
      else if (kind === 'if') replacement = truthy(value) ? yes : no;
      else replacement = truthy(value) ? no : yes;
      out = out.replace(whole, replacement);
    }
    return out.replace(/\{\{\s*(>?)\s*([^}]*?)\s*\}\}/g, (whole, include, expr) => {
      if (include) return `[include: ${expr.trim()}]`;
      const [rawPath, ...filters] = expr.split('|').map((s) => s.trim());
      let value;
      if (rawPath === 'this') value = ctx;
      else if (rawPath.startsWith('this.')) value = lookup(ctx, rawPath.slice(5));
      else value = lookup(context, rawPath) ?? lookup(ctx, rawPath);
      if (value === undefined || value === null || value === '') {
        const fallback = filters.map((f) => /^default:\s*"([^"]*)"$/.exec(f)).find(Boolean);
        if (fallback) return fallback[1];
        if (/^[A-Za-z_][\w.]*$/.test(rawPath)) missing.add(rawPath);
        return '';
      }
      return typeof value === 'object' ? JSON.stringify(value) : String(value);
    });
  };
  const raws = [];
  const text = String(content || '').replace(/\{\{\{\{raw\}\}\}\}([\s\S]*?)\{\{\{\{\/raw\}\}\}\}/g, (_, body) => {
    raws.push(body);
    return `@@HUGO_RAW_${raws.length - 1}@@`;
  });
  const rendered = renderBlock(text, context).replace(/@@HUGO_RAW_(\d+)@@/g, (_, i) => raws[Number(i)]);
  return { rendered, missingVariables: [...missing].sort() };
}

/** Insert `token` into `text` replacing the selection [start, end); returns the new text and caret position. */
export function insertAtCursor(text, token, start, end) {
  const value = String(text || '');
  const s = Math.max(0, Math.min(value.length, start ?? value.length));
  const e = Math.max(s, Math.min(value.length, end ?? s));
  return { text: value.slice(0, s) + token + value.slice(e), caret: s + token.length };
}
