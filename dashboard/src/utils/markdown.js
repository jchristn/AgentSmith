// Markdown to sanitized HTML for model output (P5.52).
//
// marked parses GitHub-flavored Markdown (tables, fenced code, task lists, strikethrough, autolinks) and passes
// inline HTML through; DOMPurify then removes everything that could run code or escape the card: script and style
// elements, event handler attributes, forms and embedded frames, inline styles, SVG and MathML, and any URL whose
// scheme is not http, https, or mailto. Sanitizing happens after parsing, so Markdown cannot smuggle anything past
// it. Both libraries are small, widely used, and maintained, and marked is fast enough to re-parse a growing
// stream on every throttled update.

import { Marked } from 'marked';
import DOMPurify from 'dompurify';

/** Schemes a link or image may use. Relative URLs and fragments are allowed too. */
const SAFE_URL = /^(?:(?:https?|mailto):|[^a-z]|[a-z+.-]+(?:[^a-z+.\-:]|$))/i;

const FORBID_TAGS = ['style', 'script', 'form', 'input', 'button', 'textarea', 'select', 'option', 'iframe', 'frame', 'frameset', 'object', 'embed', 'base', 'link', 'meta', 'dialog', 'template'];

const escapeHtml = (s) => String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');

const marked = new Marked({
  gfm: true,
  breaks: false,
  renderer: {
    // Fenced and indented code: the same frame as the dashboard's CodeBlock.
    code({ text, lang }) {
      const language = String(lang || '').split(/\s+/)[0];
      const cls = language ? ` class="language-${escapeHtml(language)}"` : '';
      return `<div class="code-block markdown-code"><pre><code${cls}>${escapeHtml(text)}</code></pre></div>\n`;
    }
  }
});

let purifier = null;
let currentLinkTip = null;
let currentHeaderTip = null;

function getPurifier() {
  if (purifier) return purifier;
  if (typeof window === 'undefined') return null;
  purifier = DOMPurify(window);
  purifier.addHook('afterSanitizeAttributes', (node) => {
    if (node.tagName === 'A' && node.hasAttribute('href')) {
      node.setAttribute('target', '_blank');
      node.setAttribute('rel', 'noopener noreferrer');
      if (currentLinkTip) node.setAttribute('data-tip', currentLinkTip(node.getAttribute('href')));
    }
    if (node.tagName === 'TH' && currentHeaderTip) node.setAttribute('data-tip', currentHeaderTip(node.textContent.trim()));
  });
  return purifier;
}

/**
 * Sanitize an HTML string with the Markdown policy above. linkTip(href) returns the tooltip text for each link and
 * headerTip(text) the tooltip for each table column header (tooltips are added after sanitizing, so model output
 * cannot set its own).
 * Returns an empty string when no DOM is available (server-side rendering is not used by the dashboard).
 */
export function sanitizeHtml(html, { linkTip, headerTip } = {}) {
  const p = getPurifier();
  if (!p) return '';
  currentLinkTip = linkTip || null;
  currentHeaderTip = headerTip || null;
  try {
    return p.sanitize(String(html || ''), {
      USE_PROFILES: { html: true },
      FORBID_TAGS,
      FORBID_ATTR: ['style', 'srcset', 'action', 'formaction', 'xlink:href'],
      ALLOW_DATA_ATTR: false,
      ALLOWED_URI_REGEXP: SAFE_URL,
      ADD_ATTR: ['target'],
      SANITIZE_NAMED_PROPS: true
    });
  } finally {
    currentLinkTip = null;
    currentHeaderTip = null;
  }
}

/** Render GitHub-flavored Markdown (with inline HTML) to sanitized HTML. */
export function renderMarkdown(text, options = {}) {
  if (!text) return '';
  // Wide tables scroll inside their own frame instead of stretching the card.
  const html = marked.parse(String(text), { async: false }).replace(/<table>/g, '<div class="markdown-table"><table>').replace(/<\/table>/g, '</table></div>');
  return sanitizeHtml(html, options);
}
