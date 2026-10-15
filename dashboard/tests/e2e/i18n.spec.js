// Locale and right-to-left coverage (P5.48, P5.49), against mocked APIs.
//
// 1. Every beta locale: the selector shows the autonym and the localized beta label on the login page and in the
//    top bar; switching updates lang, dir, and visible text, survives a reload, and no route renders a raw catalog
//    key (text such as views.home.title) in visible text or in tooltip, aria-label, or placeholder attributes.
// 2. RTL: every route in the zz pseudo-locale (RTL) at desktop and phone widths, plus English forced to
//    dir="rtl", fails on horizontal page overflow, on stylesheet or inline physical properties outside the
//    allowlist, on a sidebar that does not move to the inline start (right), and on directional icons that do not
//    mirror.

import { test, expect } from '@playwright/test';
import { installMocks } from './mocks.js';
import { PAGES, pathFor } from '../../src/config/navConfig.js';
import { LOCALES, getLocaleLabel } from '../../src/i18n/localeRegistry.js';
import de from '../../src/i18n/locales/de.js';
import es from '../../src/i18n/locales/es.js';
import fr from '../../src/i18n/locales/fr.js';
import it from '../../src/i18n/locales/it.js';
import ja from '../../src/i18n/locales/ja.js';
import zhHans from '../../src/i18n/locales/zh-Hans.js';
import zhHant from '../../src/i18n/locales/zh-Hant.js';
import yueHant from '../../src/i18n/locales/yue-Hant.js';

const CATALOGS = { de, es, fr, it, ja, 'zh-Hans': zhHans, 'zh-Hant': zhHant, 'yue-Hant': yueHant };
const BETA = LOCALES.filter((l) => l.beta);

// A raw key: a known top-level namespace followed by at least two dotted segments.
const RAW_KEY_SOURCE =
  '\\b(?:common|errorCodes|enums|nav|login|topbar|roles|components|resources|views|wizard|tour|tips|explainers)\\.[A-Za-z0-9_]+\\.[A-Za-z0-9_.]+\\b';

async function login(page) {
  await page.goto('/');
  await page.locator('#tenantId').fill('default');
  await page.locator('#email').fill('admin@garrison');
  await page.locator('#password').fill('password');
  await page.getByTestId('login-submit').click();
  await expect(page.locator('.dashboard')).toBeVisible();
}

async function settle(page) {
  await page.waitForLoadState('networkidle');
  await expect(page.locator('.loading-spinner')).toHaveCount(0);
}

/** Visible text and user-facing attributes that look like untranslated catalog keys. */
async function rawKeys(page) {
  return page.evaluate((source) => {
    const re = new RegExp(source, 'g');
    const found = new Set();
    for (const m of document.body.innerText.match(re) || []) found.add(m);
    for (const el of document.querySelectorAll('[data-tip], [aria-label], [placeholder], [title]')) {
      for (const attr of ['data-tip', 'aria-label', 'placeholder', 'title']) {
        for (const m of (el.getAttribute(attr) || '').match(re) || []) found.add(`${attr}: ${m}`);
      }
    }
    return [...found];
  }, RAW_KEY_SOURCE);
}

test.beforeEach(async ({ page }) => {
  page.on('dialog', (dialog) => {
    throw new Error(`Native dialog used: ${dialog.message()}`);
  });
  await installMocks(page);
});

test('language selectors list every locale by autonym with beta labels', async ({ page }) => {
  await page.goto('/');
  const loginOptions = page.locator('.login-lang select option');
  await expect(loginOptions).toHaveText(LOCALES.map((l) => getLocaleLabel(l.code)));
  for (const l of BETA) {
    await expect(page.locator(`.login-lang select option[value="${l.code}"]`)).toHaveAttribute('data-beta', 'true');
    await expect(page.locator(`.login-lang select option[value="${l.code}"]`)).toContainText(l.betaLabel);
  }
  await expect(page.locator('.login-lang select option[value="en"]')).toHaveText('English');

  // The login page selector switches language before sign-in.
  await page.locator('.login-lang select').selectOption('fr');
  await expect(page.locator('html')).toHaveAttribute('lang', 'fr');
  await expect(page.getByTestId('login-submit')).toHaveText(fr.login.submit);
  await page.locator('.login-lang select').selectOption('en');

  await login(page);
  await expect(page.locator('.topbar .topbar-lang select option')).toHaveText(LOCALES.map((l) => getLocaleLabel(l.code)));
});

for (const meta of BETA) {
  const catalog = CATALOGS[meta.code];
  test(`switches to ${meta.code}, renders translations, persists, and shows no raw keys`, async ({ page }) => {
    await login(page);
    await page.locator('.topbar .topbar-lang select').selectOption(meta.code);
    await expect(page.locator('html')).toHaveAttribute('lang', meta.code);
    await expect(page.locator('html')).toHaveAttribute('dir', 'ltr');
    await expect(page.locator('.page-header-title').first()).toContainText(catalog.views.home.title);
    await expect(page.locator('.sidebar-group-label')).toHaveText([catalog.nav.sections.operate, catalog.nav.sections.platform, catalog.nav.sections.administration]);
    await expect(page.locator('.sidebar-item[data-nav="agents"]')).toContainText(catalog.nav.hubs.agents.label);
    if (meta.fontFamily) {
      const font = await page.evaluate(() => getComputedStyle(document.body).fontFamily);
      // The body font must start with the locale's preferred family.
      expect(font).toContain(meta.fontFamily.split(',')[0].trim().replace(/['"]/g, ''));
    }

    await page.reload();
    await expect(page.locator('html')).toHaveAttribute('lang', meta.code);
    await expect(page.locator('.page-header-title').first()).toContainText(catalog.views.home.title);

    const problems = [];
    for (const p of PAGES) {
      await page.goto(pathFor(p.id));
      await settle(page);
      await expect(page.locator('.page-header-title').first(), p.id).toBeVisible();
      for (const k of await rawKeys(page)) problems.push(`${p.id}: ${k}`);
    }
    await page.goto(pathFor('runs'));
    await settle(page);
    await expect(page.locator('.sidebar-item[data-nav="runs"]')).toContainText(catalog.nav.hubs.runs.label);
    expect(problems).toEqual([]);
  });
}

// Selectors whose physical properties are intentional: fixed-position popups placed from measured viewport
// coordinates (the JavaScript resolves the inline direction), the visually hidden off-screen pattern, and the
// mobile drawer transform, which has explicit [dir='rtl'] overrides.
const PHYSICAL_ALLOW = [/^\.tooltip$/, /\.sr-only/, /\.visually-hidden/, /dashboard-sidebar/];

/** Stylesheet rules and inline styles that use physical left/right properties. */
async function physicalProperties(page) {
  return page.evaluate((source) => {
    const allow = source.map((s) => new RegExp(s));
    const out = new Set();
    const neutral = (prop, value) => {
      const v = value.trim();
      if (/^(0|0px|auto|none|initial|unset)$/.test(v)) return true;
      if (prop === 'float' || prop === 'clear') return v === 'none';
      if ((prop.startsWith('border-left') || prop.startsWith('border-right')) && /^(0|none)/.test(v)) return true;
      return false;
    };
    const walk = (rules) => {
      for (const rule of rules) {
        if (rule.cssRules && !rule.selectorText) {
          walk(rule.cssRules);
          continue;
        }
        if (!rule.selectorText || !rule.style) continue;
        if (allow.some((a) => a.test(rule.selectorText))) continue;
        // cssText keeps shorthands (padding, border, inset) intact, so only explicitly physical declarations match.
        const re = /(?:^|;)\s*(margin-left|margin-right|padding-left|padding-right|border-left(?:-width|-color|-style)?|border-right(?:-width|-color|-style)?|left|right|float|clear|text-align)\s*:\s*([^;]+)/g;
        const decls = new Map();
        let m;
        while ((m = re.exec(rule.style.cssText))) decls.set(m[1], m[2].trim());
        // A matching left and right pair is symmetric (a shorthand the browser expanded), so it is not directional.
        const mirror = (prop) => (prop.includes('left') ? prop.replace('left', 'right') : prop.replace('right', 'left'));
        for (const [prop, value] of decls) {
          if (prop !== 'text-align' && decls.has(mirror(prop)) && decls.get(mirror(prop)) === value) continue;
          if (prop === 'text-align') {
            if (/^(left|right)$/.test(value.trim())) out.add(`${rule.selectorText} { text-align: ${value.trim()} }`);
          } else if (!neutral(prop, value)) {
            out.add(`${rule.selectorText} { ${prop}: ${value.trim()} }`);
          }
        }
      }
    };
    for (const sheet of document.styleSheets) {
      try {
        walk(sheet.cssRules);
      } catch {
        // Cross-origin sheets cannot be read; the dashboard has none.
      }
    }
    for (const el of document.querySelectorAll('[style]')) {
      if (getComputedStyle(el).position === 'fixed') continue;
      const re = /(?:^|;)\s*(margin-left|margin-right|padding-left|padding-right|border-left(?:-width)?|border-right(?:-width)?|left|right|float|text-align)\s*:\s*([^;]+)/g;
      let m;
      while ((m = re.exec(el.getAttribute('style') || ''))) {
        const [, prop, value] = m;
        const bad = prop === 'text-align' ? /^(left|right)$/.test(value.trim()) : !neutral(prop, value);
        if (bad) out.add(`inline <${el.tagName.toLowerCase()} class="${el.className}"> ${prop}: ${value.trim()}`);
      }
    }
    return [...out];
  }, PHYSICAL_ALLOW.map((r) => r.source));
}

/** Horizontal overflow of the page itself (scrolling inside a table or code block is fine). */
async function pageOverflow(page) {
  return page.evaluate(() => {
    const doc = document.documentElement;
    const over = Math.max(doc.scrollWidth, document.body.scrollWidth) - doc.clientWidth;
    if (over <= 1) return null;
    const vw = doc.clientWidth;
    const culprits = [];
    for (const el of document.querySelectorAll('body *')) {
      const r = el.getBoundingClientRect();
      if (!r.width || !el.getClientRects().length) continue;
      if (getComputedStyle(el).position === 'fixed') continue;
      if (r.right > vw + 1 || r.left < -1) {
        culprits.push(`${el.tagName.toLowerCase()}.${String(el.className).split(' ')[0]} [${Math.round(r.left)}, ${Math.round(r.right)}]`);
        if (culprits.length >= 5) break;
      }
    }
    return `page overflows by ${over}px: ${culprits.join(', ')}`;
  });
}

async function rtlAudit(page, label, force = false) {
  const problems = [];
  for (const p of PAGES) {
    await page.goto(pathFor(p.id));
    await settle(page);
    if (force) await page.evaluate(() => document.documentElement.setAttribute('dir', 'rtl'));
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    await expect(page.locator('.page-header-title').first(), p.id).toBeVisible();
    const overflow = await pageOverflow(page);
    if (overflow) problems.push(`[${label}] ${p.id}: ${overflow}`);
  }
  return problems;
}

const RTL_VIEWPORTS = [
  { name: 'desktop', width: 1280, height: 860 },
  { name: 'mobile', width: 390, height: 844 }
];

for (const vp of RTL_VIEWPORTS) {
  test(`every route lays out right to left in zz at ${vp.name} width`, async ({ page }) => {
    test.setTimeout(240000);
    await page.setViewportSize({ width: vp.width, height: vp.height });
    await login(page);
    await page.evaluate(() => localStorage.setItem('garrison.locale', 'zz'));
    await page.goto(pathFor('home'));
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    await expect(page.locator('html')).toHaveAttribute('lang', 'zz');

    const problems = await rtlAudit(page, `zz ${vp.name}`);
    expect(problems).toEqual([]);

    if (vp.name === 'desktop') {
      // The sidebar sits at the inline start (the right in RTL) and keeps its divider on the inline end.
      const sidebar = await page.locator('.dashboard-sidebar').boundingBox();
      const main = await page.locator('.page-header-title').first().boundingBox();
      expect(sidebar.x).toBeGreaterThan(main.x);
      const divider = await page.locator('.dashboard-sidebar').evaluate((el) => getComputedStyle(el).borderLeftWidth);
      expect(divider).not.toBe('0px');
    }
  });
}

test('stylesheets and inline styles use logical properties, and directional icons mirror in RTL', async ({ page }) => {
  await login(page);
  await page.locator('.topbar .topbar-lang select').selectOption('zz');
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');

  const physical = new Set();
  for (const p of PAGES) {
    await page.goto(pathFor(p.id));
    await settle(page);
    for (const x of await physicalProperties(page)) physical.add(x);
  }
  expect([...physical]).toEqual([]);

  // Pagination chevrons point along the reading direction.
  await page.goto(pathFor('runs'));
  await settle(page);
  const chevrons = page.locator('svg.icon-mirror');
  expect(await chevrons.count()).toBeGreaterThan(0);
  const transform = await chevrons.first().evaluate((el) => getComputedStyle(el).transform);
  expect(transform).toMatch(/^matrix\(-1,/);

  // The row action menu opens inside the viewport, aligned to its trigger's inline end (left in RTL).
  const trigger = page.locator('.action-menu-trigger').first();
  if (await trigger.count()) {
    await trigger.click();
    const menu = page.locator('.action-menu-dropdown').first();
    await expect(menu).toBeVisible();
    const box = await menu.boundingBox();
    const tb = await trigger.boundingBox();
    const vw = page.viewportSize().width;
    expect(box.x).toBeGreaterThanOrEqual(0);
    expect(box.x + box.width).toBeLessThanOrEqual(vw);
    expect(Math.abs(box.x - Math.max(8, Math.min(tb.x, vw - box.width - 8)))).toBeLessThanOrEqual(2);
    await page.keyboard.press('Escape');
  }

  // Charts keep a left-to-right time axis.
  await page.goto(pathFor('analytics'));
  await settle(page);
  const svgDir = await page.locator('.chart-canvas svg').first().evaluate((el) => getComputedStyle(el).direction);
  expect(svgDir).toBe('ltr');
});

test('English with dir="rtl" forced has no page overflow on any route', async ({ page }) => {
  test.setTimeout(240000);
  await login(page);
  // Force RTL on English to separate layout bugs from pseudo-locale text expansion.
  const problems = await rtlAudit(page, 'en rtl', true);
  expect(problems).toEqual([]);
});
