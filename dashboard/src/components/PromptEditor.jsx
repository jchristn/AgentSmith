import { useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import CodeBlock from './CodeBlock';
import DiffViewer from './DiffViewer';
import MarkdownView from './MarkdownView';
import Tabs from './Tabs';
import { tip } from './Tooltip';
import useDebounce from '../hooks/useDebounce';
import { extractVariables, renderTemplateLocal, insertAtCursor } from '../utils/templates';
import { errorMessage } from '../utils/errors';

/**
 * Variables Hugo's instruction templates can use (plan section 10.3 and the server's TemplateSampleContext), offered
 * in the palette. `this.*` is the current binding inside {{#each bindings}} and in section.resource.
 */
export const HUGO_TEMPLATE_VARIABLES = [
  'profile.name',
  'profile.displayName',
  'profile.personaName',
  'profile.userAddress',
  'profile.description',
  'user.firstName',
  'user.lastName',
  'user.email',
  'tenant.name',
  'surface',
  'now.utc',
  'limits.maxIterations',
  'limits.maxToolCallsPerTurn',
  'this.alias',
  'this.connectorDisplayName',
  'this.environment',
  'this.accessMode',
  'this.endpoint',
  'this.notes',
  'this.instructions'
];

/**
 * Sample context for previews: Agent Smith addressing Mr. Anderson, with one bound PostgreSQL resource. The server
 * merges this over its own sample (TemplateSampleContext), so editing it changes the preview.
 */
export const SAMPLE_TEMPLATE_CONTEXT = {
  profile: { name: 'Hugo', displayName: 'Hugo', personaName: 'Agent Smith', userAddress: 'Mr. Anderson', description: 'Default operations agent.' },
  user: { firstName: 'Thomas', lastName: 'Anderson', email: 'anderson@hugo.local' },
  tenant: { name: 'Default' },
  surface: 'Chat',
  limits: { maxIterations: 12, maxToolCallsPerTurn: 24 },
  this: {
    alias: 'prod-db',
    connectorDisplayName: 'PostgreSQL',
    environment: 'prod',
    accessMode: 'ReadOnly',
    endpoint: 'db.example.com:5432',
    notes: 'Primary database for checkout.',
    instructions: 'Use sql_query for read-only queries.'
  },
  now: { utc: '2026-10-14T12:00:00Z' }
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
function TemplatePromptEditor({
  value = '',
  onChange,
  readOnly = false,
  id = 'prompt-editor',
  label,
  rows = 18,
  variables = HUGO_TEMPLATE_VARIABLES,
  render,
  baseline,
  initialContext = SAMPLE_TEMPLATE_CONTEXT
}) {
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
        // The server answers 200 with success false and the parse or render errors when the template is invalid.
        setPreviewError(r && r.success === false && (r.errors || []).length ? r.errors.join(' ') : null);
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
          <textarea
            ref={textRef}
            id={id}
            {...tip(t('tips.promptEditor.content'))}
            className="monospace template-textarea"
            spellCheck={false}
            value={value}
            readOnly={readOnly}
            onChange={(e) => onChange?.(e.target.value)}
            rows={rows}
          />
          <div className="field-hint">{t('components.promptEditor.syntaxHint')}</div>
        </section>
        <aside className="panel editor-side" aria-label={t('components.promptEditor.variables')}>
          <h3 className="section-title">{t('components.promptEditor.palette')}</h3>
          <div className="variable-palette">
            {variables.map((v) => (
              <button
                key={v}
                type="button"
                className="chip-button monospace"
                disabled={readOnly}
                onClick={() => insert(v)}
                data-variable={v}
                {...tip(t('tips.promptEditor.insert', { variable: v }))}
              >
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
              <textarea
                id={`${id}-context`}
                {...tip(t('tips.promptEditor.sampleContext'))}
                className="monospace"
                rows={14}
                spellCheck={false}
                value={contextText}
                onChange={(e) => setContextText(e.target.value)}
              />
            </div>
            <div>
              <div className="section-title">
                {t('components.promptEditor.rendered')}{' '}
                <span className="text-muted" {...tip(t(usedServer ? 'tips.promptEditor.serverRender' : 'tips.promptEditor.localRender'))}>
                  ({t(usedServer ? 'components.promptEditor.serverRender' : 'components.promptEditor.localRender')})
                </span>
              </div>
              {previewError ? (
                <div className="error-banner">{previewError}</div>
              ) : (
                <CodeBlock value={preview.rendered} emptyText={t('components.promptEditor.previewEmpty')} />
              )}
              {preview.missingVariables?.length > 0 && (
                <div className="warning-banner">{t('components.promptEditor.missing', { list: preview.missingVariables.join(', ') })}</div>
              )}
            </div>
          </div>
        )}
        {tab === 'diff' && baseline && (
          <DiffViewer before={baseline.text} after={value} beforeLabel={baseline.label} afterLabel={t('components.promptEditor.current')} />
        )}
      </div>
    </div>
  );
}

/** Literal document mode never evaluates Hugo templates or suggests template variables. */
export default function PromptEditor(props) {
  if (!props.literal) return <TemplatePromptEditor {...props} />;
  return (
    <div className="field span-full">
      <label htmlFor={props.id}>{props.label}</label>
      <textarea
        id={props.id}
        className="monospace code-input"
        rows={props.rows || 18}
        value={props.value || ''}
        disabled={props.readOnly}
        onChange={(e) => props.onChange?.(e.target.value)}
      />
      {props.error && (
        <p className="field-error" role="alert">
          {props.error}
        </p>
      )}
      {props.showPreview && <MarkdownView text={props.value} blockImages rawToggle />}
    </div>
  );
}
