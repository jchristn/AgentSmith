#!/usr/bin/env node
// i18n checks for CI (I18N.md "Testing, CI, And Workflow Requirements"):
//   1. missing keys: every static t('key') in src exists in the English catalog;
//   2. parity: every locale catalog has exactly the English keys, keeps every {{placeholder}}, and has no empty values;
//   3. orphans: English keys that no source file can reach (static keys, template-literal key patterns, and the
//      namespaces generated from configuration);
//   4. hard-coded strings: JSX text and user-facing attributes written in English outside the catalogs;
//   5. encoding and punctuation: catalogs are valid UTF-8 JSON without em-dashes or en-dashes.
// `--missing` writes, per locale, the English text of keys the locale lacks to i18n-missing/<code>.json (translator
// input) and exits 0. Run with `npm run i18n:check`.

import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const SRC = path.join(ROOT, 'src');
const LOCALES = path.join(SRC, 'i18n', 'locales');
const PLURAL = /_(zero|one|two|few|many|other)$/;

// Namespaces whose keys are built from configuration or server data rather than written literally in source.
const GENERATED_PREFIXES = [
  'resources.',
  'tips.resources.',
  'enums.',
  'tips.enums.',
  'explainers.',
  'errorCodes.',
  'common.fieldCodes.',
  'nav.',
  'tips.nav.',
  'views.settings.fields.',
  'views.settings.groups.',
  'tips.views.settings.',
  'views.settings.sources.',
  'views.settings.lockedBy.',
  'connectors.',
  'components.secretField.fields.',
  'tips.secretField.fields.',
  'components.schemaForm.',
  'tour.steps.',
  'wizard.',
  'tips.common.fields.'
];

// JSX text that is not English prose: punctuation, numbers, product names, and protocol terms.
const TEXT_ALLOW = /^[\s\d.,:;!?()[\]{}/\\|+*=<>#@&%$'"`~^_·…→←↑↓-]*$|^(MCP|A2A|OpenAI|JSON|REST|SSE|ID|v)$/;

function leafKeys(node, prefix = '', out = new Set()) {
  for (const [k, v] of Object.entries(node || {})) {
    const key = prefix ? `${prefix}.${k}` : k;
    if (v && typeof v === 'object') leafKeys(v, key, out);
    else out.add(key);
  }
  return out;
}

function lookup(catalog, key) {
  return key.split('.').reduce((n, part) => (n && typeof n === 'object' ? n[part] : undefined), catalog);
}

function exists(catalog, key) {
  if (typeof lookup(catalog, key) === 'string') return true;
  return typeof lookup(catalog, `${key}_one`) === 'string' || typeof lookup(catalog, `${key}_other`) === 'string';
}

function sourceFiles(dir, out = []) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      if (entry.name === 'tests' || entry.name === 'locales') continue;
      sourceFiles(full, out);
    } else if (/\.(js|jsx)$/.test(entry.name)) out.push(full);
  }
  return out;
}

function readCatalog(file) {
  const buf = fs.readFileSync(file);
  const text = new TextDecoder('utf-8', { fatal: true }).decode(buf);
  return JSON.parse(text);
}

const en = readCatalog(path.join(LOCALES, 'en.json'));
const enKeys = leafKeys(en);
const base = (keys) => new Set([...keys].map((k) => k.replace(PLURAL, '')));
const enBase = base(enKeys);
const files = sourceFiles(SRC);
const problems = [];

// ---------------------------------------------------------------- 1. missing static keys, and key patterns
const staticKeys = new Set();
const patterns = [];
const staticRe = /\bt\(\s*'([A-Za-z0-9_.]+)'\s*(,\s*\{\s*defaultValue)?/g;
const templateRe = /\bt\(\s*`([^`]+)`/g;
const literalKeyRe = /'((?:common|components|views|tips|login|topbar|roles|wizard|tour)\.[A-Za-z0-9_.]+)'/g;
for (const file of files) {
  const text = fs.readFileSync(file, 'utf8');
  let m;
  while ((m = staticRe.exec(text))) {
    if (!m[1].includes('.')) continue;
    staticKeys.add(m[1]);
    if (!m[2] && !exists(en, m[1])) problems.push(`missing key: ${m[1]} (${path.relative(ROOT, file)})`);
  }
  while ((m = templateRe.exec(text))) {
    const source = m[1].replace(/\$\{[^}]+\}/g, '@@VAR@@');
    const escaped = source.replace(/[.*+?^()|[\]\\]/g, '\\$&').replace(/@@VAR@@/g, '[A-Za-z0-9_.]+');
    // A pattern that starts with a variable (`${prefix}.title`) says nothing about the namespace; those keys are
    // covered by GENERATED_PREFIXES instead.
    if (!escaped.startsWith('[A-Za-z0-9_.]+')) patterns.push(new RegExp(`^${escaped}$`));
  }
  // Keys passed around as string literals (for example tipKey: 'tips.wizard.start').
  while ((m = literalKeyRe.exec(text))) staticKeys.add(m[1]);
}

// ---------------------------------------------------------------- 2. parity across locales
const localeFiles = fs.readdirSync(LOCALES).filter((f) => f.endsWith('.json') && f !== 'en.json');
const vars = (s) => [...new Set((String(s).match(/\{\{\s*([A-Za-z0-9_.]+)\s*\}\}/g) || []).map((v) => v.replace(/[{}\s]/g, '')))].sort();
const missingByLocale = {};
for (const file of localeFiles) {
  const code = file.replace('.json', '');
  let catalog;
  try {
    catalog = readCatalog(path.join(LOCALES, file));
  } catch (e) {
    problems.push(`${code}: not valid UTF-8 JSON (${e.message})`);
    continue;
  }
  const keys = leafKeys(catalog);
  const have = base(keys);
  const lacking = [...enBase].filter((k) => !have.has(k));
  missingByLocale[code] = lacking;
  for (const k of lacking) problems.push(`${code}: missing ${k}`);
  for (const k of have) if (!enBase.has(k)) problems.push(`${code}: extra ${k}`);
  for (const k of keys) {
    const target = lookup(catalog, k);
    if (typeof target !== 'string' || !target.trim()) {
      problems.push(`${code}: empty ${k}`);
      continue;
    }
    const source = lookup(en, k) ?? lookup(en, k.replace(PLURAL, '_other'));
    if (typeof source !== 'string') continue;
    const expected = vars(source).filter((v) => v !== 'count' || /\{\{\s*count/.test(target));
    if (JSON.stringify(vars(target)) !== JSON.stringify(expected)) problems.push(`${code}: placeholders differ in ${k}`);
  }
}

// ---------------------------------------------------------------- 3. orphans
for (const key of enKeys) {
  const k = key.replace(PLURAL, '');
  if (staticKeys.has(k)) continue;
  if (GENERATED_PREFIXES.some((p) => k.startsWith(p))) continue;
  if (patterns.some((re) => re.test(k))) continue;
  problems.push(`orphaned key: ${key}`);
}

// ---------------------------------------------------------------- 4. hard-coded strings in JSX
const jsxText = /(?<![=-])>([^<>{}\n]*[A-Za-z][^<>{}\n]*)</g;
const attrRe = /\s(placeholder|title|aria-label|alt)="([^"]*[A-Za-z][^"]*)"/g;
const stripComments = (code) => code.replace(/\/\*[\s\S]*?\*\//g, '').replace(/(^|[^:'"`])\/\/.*$/gm, '$1');
for (const file of files.filter((f) => f.endsWith('.jsx'))) {
  const text = stripComments(fs.readFileSync(file, 'utf8'));
  let m;
  while ((m = jsxText.exec(text))) {
    const value = m[1].trim();
    // Skip code (arrow functions, comparisons) that the regex can catch between angle brackets.
    if (!value || TEXT_ALLOW.test(value) || /[=;]|=>|&&|\|\||\(\)/.test(value) || /^[a-z][A-Za-z0-9]*$/.test(value)) continue;
    problems.push(`hard-coded text: "${value}" (${path.relative(ROOT, file)})`);
  }
  while ((m = attrRe.exec(text))) {
    if (/^(access_\.\.\.|prod|https?:)/.test(m[2])) continue;
    problems.push(`hard-coded ${m[1]}: "${m[2]}" (${path.relative(ROOT, file)})`);
  }
}

// ---------------------------------------------------------------- 5. punctuation
for (const file of ['en.json', ...localeFiles]) {
  const text = fs.readFileSync(path.join(LOCALES, file), 'utf8');
  if (text.includes('—')) problems.push(`${file}: contains an em-dash`);
  if (text.includes('–')) problems.push(`${file}: contains an en-dash`);
}

if (process.argv.includes('--missing')) {
  const out = path.join(ROOT, 'i18n-missing');
  fs.mkdirSync(out, { recursive: true });
  for (const [code, keys] of Object.entries(missingByLocale)) {
    const obj = {};
    for (const k of keys) {
      for (const full of [k, `${k}_one`, `${k}_other`]) {
        const v = lookup(en, full);
        if (typeof v === 'string') obj[full] = v;
      }
    }
    fs.writeFileSync(path.join(out, `${code}.json`), JSON.stringify(obj, null, 2) + '\n');
    console.log(`${code}: ${keys.length} missing keys written to i18n-missing/${code}.json`);
  }
  process.exit(0);
}

if (problems.length) {
  for (const p of problems.slice(0, 400)) console.error(p);
  if (problems.length > 400) console.error(`... and ${problems.length - 400} more`);
  console.error(`\ni18n check failed: ${problems.length} problem(s).`);
  process.exit(1);
}
console.log(`i18n check passed: ${enKeys.size} English keys, ${localeFiles.length} translated catalogs, ${files.length} source files.`);
