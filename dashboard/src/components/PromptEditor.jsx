import { useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import CodeBlock from './CodeBlock';
import DiffViewer from './DiffViewer';
import Tabs from './Tabs';
import { tip } from './Tooltip';
import useDebounce from '../hooks/useDebounce';
import { extractVariables, renderTemplateLocal, insertAtCursor } from '../utils/templates';
import { errorMessage } from '../utils/errors';

/** Variables Hugo's instruction templates can use (plan section 10.3), offered in the palette. */
export const HUGO_TEMPLATE_VARIABLES = [
  'persona.name',
  'persona.userAddress',
  'profile.name',
  'profile.displayName',
  'tenant.name',
  'caller.displayName',
  'caller.principalType',
  'surface',
  'resource.alias',
  'resource.name',
  'resource.connectorType',
  'resource.environment',
  'resource.accessMode',
  'resource.endpoint',
  'resource.notes',
  'binding.accessMode',
  'binding.requireApprovalForDestructive',
  'now'
];

/** Sample context for previews: Agent Smith addressing Mr. Anderson, with one bound PostgreSQL resource. */
export const SAMPLE_TEMPLATE_CONTEXT = {
  persona: { name: 'Agent Smith', userAddress: 'Mr. Anderson' },
  profile: { name: 'Hugo', displayName: 'Hugo' },
  tenant: { name: 'Default' },
  caller: { displayName: 'anderson@hugo.local', principalType: 'User' },
  surface: 'Chat',
  resource: { alias: 'prod-db', name: 'prod-db', connectorType: 'postgresql', environment: 'prod', accessMode: 'ReadOnly', endpoint: 'db.example.com:5432', notes: 'Primary database for checkout.' },
  binding: { accessMode: 'ReadOnly', requireApprovalForDestructive: true },
  now: '2026-10-14T12:00:00Z'
};

/**
 * Prompt editor (P8.13): a monospace editor, a variables palette that inserts {{variable}} at the cursor, the
 * variables the text uses, a live preview rendered with an editable sample context, and a diff against a baseline
 * (for example the built-in default or a saved version).
 *
 * Props: value, onChange, readOnly, id, label, rows, variables (palette, default HUGO_TEMPLATE_VARIABLES),
 * render(content, context) => Promise<{ rendered, missingVariables }> (server preview; local fallback when absent or
 * failing), baseline ({ label, text }) for the Diff tab.
 */
export default function PromptEditor({ value = '', onChange, readOnly = false, id = 'prompt-editor', label, rows = 18, variables = HUGO_TEMPLATE_VARIABLES, render, baseline, initialContext = SAMPLE_TEMPLATE_CONTEXT }) {
  const { t } = useTranslation();
  const textRef = useRef(null);
  const [tab, setTab] = useState('preview');
  const [contextText, setContextText] = useState(() => JSON.stringify(initialContext, null, 2));
  const [preview, setPreview] = useState({ rendered: '', missingVariables: [] });
  const [previewError, setPreviewError] = useState(null);
  const [usedServer, setUsedServer] = useState(false);
  const debouncedValue = useDebounce(value, 400);
  const debouncedContext = useDebounce(contextText, 400);
  const used = useMemo(() => extractVariables(value), [value]);

  useEffect(() => {
    if (tab !== 'preview') return undefined;
    let context;
    try {
      context = JSON.parse(debouncedContext);
    } catch {
      setPreviewError(t('common.errors.invalidJson'));
      return undefined;
    }
    let active = true;
    const local = () => {
      if (!active) return;
      setPreview(renderTemplateLocal(debouncedValue, context));
      setUsedServer(false);
      setPreviewError(null);
    };
    if (!render) {
      local();
      return () => {
        active = false;
      };
    }
    Promise.resolve(render(debouncedValue, context))
      .then((r) => {
        if (!active) return;
        setPreview({ rendered: r?.rendered ?? r?.text ?? (typeof r === 'string' ? r : ''), missingVariables: r?.missingVariables || [] });
        setUsedServer(true);
        setPreviewError(null);
      })
      .catch((e) => {
        // A server without the render route still gets a preview from the local renderer.
        if (e?.status === 404) local();
        else if (active) setPreviewError(errorMessage(t, e));
      });
    return () => {
      active = false;
    };
  }, [debouncedValue, debouncedContext, tab, render, t]);

  const insert = (name) => {
    if (readOnly) return;
    const el = textRef.current;
    const token = `{{${name}}}`;
    const { text, caret } = insertAtCursor(value, token, el?.selectionStart ?? value.length, el?.selectionEnd ?? value.length);
    onChange?.(text);
    requestAnimationFrame(() => {
      if (!el) return;
      el.focus();
      el.setSelectionRange(caret, caret);
    });
  };

  const tabs = [
    { id: 'preview', label: t('components.promptEditor.preview'), tip: t('tips.promptEditor.preview') },
    ...(baseline ? [{ id: 'diff', label: t('components.promptEditor.diff'), tip: t('tips.promptEditor.diff') }] : [])
  ];

  return (
    <div className="prompt-editor" data-testid="prompt-editor">
      <div className="editor-layout">
        <section className="panel editor-main" aria-label={label || t('components.promptEditor.content')}>
          <label htmlFor={id} className="section-title" {...tip(t('tips.promptEditor.content'))}>
            {label || t('components.promptEditor.content')}
          </label>
          <textarea ref={textRef} id={id} {...tip(t('tips.promptEditor.content'))} className="monospace template-textarea" spellCheck={false} value={value} readOnly={readOnly} onChange={(e) => onChange?.(e.target.value)} rows={rows} />
          <div className="field-hint">{t('components.promptEditor.syntaxHint')}</div>
        </section>
        <aside className="panel editor-side" aria-label={t('components.promptEditor.variables')}>
          <h3 className="section-title">{t('components.promptEditor.palette')}</h3>
          <div className="variable-palette">
            {variables.map((v) => (
              <button key={v} type="button" className="chip-button monospace" disabled={readOnly} onClick={() => insert(v)} data-variable={v} {...tip(t('tips.promptEditor.insert', { variable: v }))}>
                {v}
              </button>
            ))}
          </div>
          <h3 className="section-title">{t('components.promptEditor.used')}</h3>
          {used.length === 0 ? (
            <div className="text-muted">{t('components.promptEditor.noVariables')}</div>
          ) : (
            <ul className="plain-list" data-testid="used-variables">
              {used.map((v) => (
                <li key={v}>
                  <code>{v}</code>
                </li>
              ))}
            </ul>
          )}
        </aside>
      </div>
      {tabs.length > 1 && <Tabs label={t('components.promptEditor.content')} active={tab} onChange={setTab} tabs={tabs} />}
      <div className="panel">
        {tab === 'preview' && (
          <div className="preview-layout">
            <div className="field">
              <label htmlFor={`${id}-context`} {...tip(t('tips.promptEditor.sampleContext'))}>
                {t('components.promptEditor.sampleContext')}
              </label>
              <textarea id={`${id}-context`} {...tip(t('tips.promptEditor.sampleContext'))} className="monospace" rows={14} spellCheck={false} value={contextText} onChange={(e) => setContextText(e.target.value)} />
            </div>
            <div>
              <div className="section-title">
                {t('components.promptEditor.rendered')}{' '}
                <span className="text-muted" {...tip(t(usedServer ? 'tips.promptEditor.serverRender' : 'tips.promptEditor.localRender'))}>
                  ({t(usedServer ? 'components.promptEditor.serverRender' : 'components.promptEditor.localRender')})
                </span>
              </div>
              {previewError ? <div className="error-banner">{previewError}</div> : <CodeBlock value={preview.rendered} emptyText={t('components.promptEditor.previewEmpty')} />}
              {preview.missingVariables?.length > 0 && <div className="warning-banner">{t('components.promptEditor.missing', { list: preview.missingVariables.join(', ') })}</div>}
            </div>
          </div>
        )}
        {tab === 'diff' && baseline && <DiffViewer before={baseline.text} after={value} beforeLabel={baseline.label} afterLabel={t('components.promptEditor.current')} />}
      </div>
    </div>
  );
}
