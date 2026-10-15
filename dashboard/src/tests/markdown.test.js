// Markdown renderer (P5.52): GitHub-flavored features render, and nothing that can run code survives sanitizing.

import { describe, it, expect } from 'vitest';
import { renderMarkdown, sanitizeHtml } from '../utils/markdown';

const dom = (html) => {
  const div = document.createElement('div');
  div.innerHTML = html;
  return div;
};

describe('renderMarkdown', () => {
  it('renders GFM tables inside a scrolling frame', () => {
    const el = dom(renderMarkdown('| Name | Value |\n|---|---|\n| a | 1 |\n| b | 2 |', { headerTip: (name) => `Column ${name}` }));
    expect(el.querySelector('.markdown-table > table')).not.toBeNull();
    expect(el.querySelectorAll('th')).toHaveLength(2);
    expect(el.querySelectorAll('tbody tr')).toHaveLength(2);
    expect(el.querySelector('th').getAttribute('data-tip')).toBe('Column Name');
  });

  it('renders fenced code blocks with the CodeBlock frame and escapes their content', () => {
    const el = dom(renderMarkdown('```js\nconst x = "<b>hi</b>";\n```'));
    const code = el.querySelector('.code-block pre code.language-js');
    expect(code).not.toBeNull();
    expect(code.textContent).toBe('const x = "<b>hi</b>";');
    expect(el.querySelector('b')).toBeNull();
  });

  it('renders lists, emphasis, inline code, and strikethrough', () => {
    const el = dom(renderMarkdown('- **bold**\n- `code`\n- ~~gone~~\n\n1. one\n2. two'));
    expect(el.querySelectorAll('ul li')).toHaveLength(3);
    expect(el.querySelectorAll('ol li')).toHaveLength(2);
    expect(el.querySelector('strong').textContent).toBe('bold');
    expect(el.querySelector('li code').textContent).toBe('code');
    expect(el.querySelector('del').textContent).toBe('gone');
  });

  it('opens links in a new tab with rel=noopener and a tooltip', () => {
    const el = dom(renderMarkdown('[Docs](https://example.com/docs) and <a href="https://example.org">raw</a>', { linkTip: (href) => `Opens ${href}` }));
    const links = el.querySelectorAll('a');
    expect(links).toHaveLength(2);
    for (const a of links) {
      expect(a.getAttribute('target')).toBe('_blank');
      expect(a.getAttribute('rel')).toContain('noopener');
      expect(a.getAttribute('data-tip')).toMatch(/^Opens https:\/\/example\./);
    }
  });

  it('keeps harmless inline HTML', () => {
    const el = dom(renderMarkdown('Press <kbd>Ctrl</kbd> and see <details><summary>More</summary>Body</details>'));
    expect(el.querySelector('kbd').textContent).toBe('Ctrl');
    expect(el.querySelector('details summary').textContent).toBe('More');
  });

  it('removes script tags', () => {
    const html = renderMarkdown('Hello <script>window.__pwned = 1</script>\n\n<script src="https://evil.example/x.js"></script>');
    expect(html).not.toMatch(/<script/i);
    expect(html).not.toContain('__pwned');
  });

  it('removes event handler attributes such as onerror and onclick', () => {
    const el = dom(
      renderMarkdown('<img src="x" onerror="alert(1)"> <a href="https://example.com" onclick="alert(2)">x</a> <div onmouseover="alert(3)">y</div>')
    );
    for (const node of el.querySelectorAll('*')) {
      for (const attr of node.attributes) expect(attr.name.startsWith('on')).toBe(false);
    }
    expect(el.innerHTML).not.toContain('alert');
  });

  it('removes javascript:, vbscript:, and data: URLs from links', () => {
    const el = dom(
      renderMarkdown(
        '[a](javascript:alert(1)) <a href="JaVaScRiPt:alert(2)">b</a> <a href="vbscript:msgbox(1)">c</a> <a href="data:text/html,<script>alert(3)</script>">d</a>'
      )
    );
    for (const a of el.querySelectorAll('a')) {
      const href = a.getAttribute('href') || '';
      expect(href).not.toMatch(/^\s*(javascript|vbscript|data):/i);
    }
    expect(el.innerHTML.toLowerCase()).not.toContain('javascript:');
  });

  it('removes iframes, forms, styles, inline style attributes, and SVG', () => {
    const el = dom(
      renderMarkdown(
        '<iframe src="https://example.com"></iframe><form action="/x"><input name="a"></form><style>body{display:none}</style><p style="position:fixed">p</p><svg><script>alert(1)</script></svg>'
      )
    );
    expect(el.querySelector('iframe, form, input, style, svg, script')).toBeNull();
    expect(el.querySelector('p').hasAttribute('style')).toBe(false);
  });

  it('returns an empty string for empty input', () => {
    expect(renderMarkdown('')).toBe('');
    expect(renderMarkdown(null)).toBe('');
    expect(sanitizeHtml('<b>ok</b>')).toBe('<b>ok</b>');
  });
});
