import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import PageHeader from '../components/PageHeader';
import FormFields from '../components/FormFields';
import DiffViewer from '../components/DiffViewer';
import PromptEditor from '../components/PromptEditor';
import ConfirmModal from '../components/ConfirmModal';
import JsonViewerModal from '../components/JsonViewerModal';
import Modal from '../components/Modal';
import Explainer from '../components/Explainer';
import { LoadingState, ErrorBanner } from '../components/States';
import { Badge } from '../components/StatusBadge';
import { useAuth } from '../context/AuthContext';
import { useApp } from '../context/AppContext';
import { useToast } from '../context/ToastContext';
import { enumOptions } from '../utils/enums';
import { pathFor } from '../config/navConfig';
import { errorMessage } from '../utils/errors';
import { formatDateTime } from '../i18n/formatters';
import { tip } from '../components/Tooltip';
import { resourceTip } from '../utils/tips';
import CloneTemplateModal from '../components/CloneTemplateModal';
import { templateDeletePreview } from './resourceConfigs';

/**
 * Prompt template editor: the PromptEditor (monospace content, variables palette, live preview with a sample context,
 * diff against the saved text), then versions with diff and restore, clone, and reset to default.
 */
export default function PromptTemplateEditorView({ recordId }) {
  const { t, i18n } = useTranslation();
  const { apiClient } = useAuth();
  const { tenantId } = useApp();
  const { addToast } = useToast();
  const navigate = useNavigate();
  const isNew = recordId === 'new';

  const [template, setTemplate] = useState(null);
  const [form, setForm] = useState({ name: '', description: '', kind: 'Task', content: '', changeNote: '' });
  const [versions, setVersions] = useState([]);
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);
  const [diffVersion, setDiffVersion] = useState(null);
  const [confirm, setConfirm] = useState(null);
  const [jsonOpen, setJsonOpen] = useState(false);
  const [cloneOpen, setCloneOpen] = useState(false);
  const [deleteInfo, setDeleteInfo] = useState(null);

  const load = useCallback(() => {
    if (isNew || !apiClient) return;
    apiClient
      .getPromptTemplate(tenantId, recordId)
      .then((tpl) => {
        setTemplate(tpl);
        setForm({ name: tpl.name, description: tpl.description || '', kind: tpl.kind || 'Task', content: tpl.content || '', changeNote: '' });
        setError(null);
      })
      .catch(setError);
    apiClient
      .listPromptTemplateVersions(tenantId, recordId)
      .then((r) => setVersions(Array.isArray(r) ? r : r?.objects || r?.items || []))
      .catch(() => setVersions([]));
  }, [apiClient, tenantId, recordId, isNew]);

  useEffect(load, [load]);

  const readOnly = !!template?.isBuiltIn;

  const renderPreview = useCallback((content, context) => apiClient.renderTemplateContent(tenantId, { content, context }), [apiClient, tenantId]);

  const save = async () => {
    if (!form.name) {
      addToast({ type: 'error', message: t('common.errors.required') });
      return;
    }
    setBusy(true);
    try {
      const body = { name: form.name, description: form.description, kind: form.kind, content: form.content, changeNote: form.changeNote || undefined };
      if (isNew) {
        const created = await apiClient.createPromptTemplate(tenantId, body);
        addToast({ type: 'success', message: t('common.toasts.created') });
        if (created?.id) navigate(pathFor('promptTemplates', created.id), { replace: true });
      } else {
        await apiClient.updatePromptTemplate(tenantId, recordId, body);
        addToast({ type: 'success', message: t('views.templateEditor.savedVersion') });
        load();
      }
    } catch (e) {
      addToast({ type: 'error', message: errorMessage(t, e) });
    } finally {
      setBusy(false);
    }
  };

  const runConfirmed = async () => {
    const action = confirm;
    setConfirm(null);
    try {
      if (action.type === 'restore') {
        await apiClient.restorePromptTemplateVersion(tenantId, recordId, action.version);
        addToast({ type: 'success', message: t('views.templateEditor.restored', { version: action.version }) });
      } else if (action.type === 'reset') {
        await apiClient.resetPromptTemplate(tenantId, recordId);
        addToast({ type: 'success', message: t('views.templateEditor.resetDone') });
      } else if (action.type === 'delete') {
        await apiClient.deletePromptTemplate(tenantId, recordId);
        addToast({ type: 'success', message: t('common.toasts.deleted') });
        navigate(pathFor('promptTemplates'));
        return;
      }
      load();
    } catch (e) {
      addToast({ type: 'error', message: errorMessage(t, e) });
    }
  };

  const clone = () => setCloneOpen(true);

  // Before deleting, say which deployments bind this template; the server refuses while any do (M1-21).
  const askDelete = () => {
    setDeleteInfo(null);
    setConfirm({ type: 'delete' });
    templateDeletePreview(t, apiClient, tenantId, { id: recordId, name: template?.name || form.name })
      .then(setDeleteInfo)
      .catch(() => setDeleteInfo(null));
  };

  if (!isNew && !template && !error) return <LoadingState />;

  const fields = [
    {
      name: 'name',
      type: 'text',
      label: t('resources.promptTemplates.fields.name'),
      tip: resourceTip(t, 'promptTemplates', 'name'),
      required: true,
      readOnly,
      hint: isNew || readOnly ? undefined : t('views.templateEditor.renameHint')
    },
    { name: 'kind', type: 'select', label: t('resources.promptTemplates.fields.kind'), tip: resourceTip(t, 'promptTemplates', 'kind'), required: true, options: enumOptions(t, 'promptTemplateKind'), readOnly },
    { name: 'description', type: 'text', label: t('resources.promptTemplates.fields.description'), tip: resourceTip(t, 'promptTemplates', 'description'), readOnly, span: 'full' }
  ];

  return (
    <div>
      <PageHeader
        title={isNew ? t('views.templateEditor.newTitle') : form.name}
        subtitle={
          isNew ? (
            t('views.templateEditor.subtitle')
          ) : (
            <span className="inline-meta">
              <Badge tone={readOnly ? 'info' : 'neutral'} tip={t(readOnly ? 'tips.badges.builtIn' : 'tips.badges.custom')}>{readOnly ? t('common.generic.builtIn') : t('common.generic.custom')}</Badge> v{template?.currentVersion ?? 1}
            </span>
          )
        }
        actions={
          <>
            {!isNew && (
              <button type="button" className="button-secondary" onClick={() => setJsonOpen(true)} {...tip(t('tips.actions.viewJson'))}>
                {t('common.actions.viewJson')}
              </button>
            )}
            {!isNew && (
              <button type="button" className="button-secondary" onClick={clone} {...tip(t('tips.views.templateEditor.clone'))}>
                {t('common.actions.clone')}
              </button>
            )}
            {readOnly && (
              <button type="button" className="button-secondary" onClick={() => setConfirm({ type: 'reset' })} {...tip(t('tips.views.templateEditor.reset'))}>
                {t('views.templateEditor.reset')}
              </button>
            )}
            {!isNew && !readOnly && (
              <button type="button" className="button-danger" onClick={askDelete} {...tip(t('tips.views.templateEditor.delete'))}>
                {t('common.actions.delete')}
              </button>
            )}
            {!readOnly && (
              <button type="button" className="button-primary" onClick={save} disabled={busy} data-testid="template-save" {...tip(t(isNew ? 'tips.views.templateEditor.create' : 'tips.views.templateEditor.save'))}>
                {busy ? t('common.generic.saving') : t('common.actions.save')}
              </button>
            )}
          </>
        }
      />
      {error && <ErrorBanner message={errorMessage(t, error)} onRetry={load} />}
      <Explainer topic="promptTemplates" />
      {readOnly && (
        <div className="info-banner readonly-note" role="note" data-testid="builtin-readonly">
          <span>{t('views.templateEditor.builtInReadOnly')}</span>
          <button type="button" className="button-primary button-sm" onClick={clone} {...tip(t('tips.views.templateEditor.clone'))}>
            {t('views.templateEditor.cloneToEdit')}
          </button>
        </div>
      )}

      <div className="panel">
        <FormFields fields={fields} data={form} onChange={setForm} idPrefix="tpl" />
      </div>

      <PromptEditor
        id="tpl-content"
        value={form.content}
        readOnly={readOnly}
        onChange={(content) => setForm({ ...form, content })}
        render={renderPreview}
        baseline={template ? { label: t('views.templateEditor.saved'), text: template.content || '' } : null}
      />
      {!readOnly && !isNew && (
        <div className="panel">
          <div className="field">
            <label htmlFor="tpl-note" {...tip(t('tips.views.templateEditor.changeNote'))}>
              {t('views.templateEditor.changeNote')}
            </label>
            <input id="tpl-note" {...tip(t('tips.views.templateEditor.changeNote'))} value={form.changeNote} onChange={(e) => setForm({ ...form, changeNote: e.target.value })} />
          </div>
        </div>
      )}

      <h2 className="section-title">{t('views.templateEditor.versions', { count: versions.length })}</h2>
      <div className="panel">
        {versions.length === 0 ? (
            <div className="text-muted">{t('views.templateEditor.noVersions')}</div>
          ) : (
            <div className="table-scroll">
              <table className="data-table">
                <thead>
                  <tr>
                    <th scope="col" {...tip(t('tips.views.templateEditor.columns.version'))}>{t('views.templateEditor.version')}</th>
                    <th scope="col" {...tip(t('tips.views.templateEditor.columns.note'))}>{t('views.templateEditor.note')}</th>
                    <th scope="col" {...tip(t('tips.views.templateEditor.columns.author'))}>{t('views.templateEditor.author')}</th>
                    <th scope="col" {...tip(t('tips.views.templateEditor.columns.saved'))}>{t('resources.promptTemplates.columns.lastUpdateUtc')}</th>
                    <th scope="col" className="right" {...tip(t('tips.table.actionsColumn'))}>
                      {t('common.generic.actions')}
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {[...versions].sort((a, b) => b.version - a.version).map((v) => (
                    <tr key={v.id || v.version}>
                      <td>v{v.version}</td>
                      <td>{v.changeNote || '-'}</td>
                      <td>{v.createdBy || '-'}</td>
                      <td>{formatDateTime(v.createdUtc, i18n.language)}</td>
                      <td className="actions-cell">
                        <button type="button" className="button-ghost button-sm" onClick={() => setDiffVersion(v)} {...tip(t('tips.views.templateEditor.diff'))}>
                          {t('views.templateEditor.diff')}
                        </button>
                        {!readOnly && v.version !== template?.currentVersion && (
                          <button type="button" className="button-ghost button-sm" onClick={() => setConfirm({ type: 'restore', version: v.version })} {...tip(t('tips.views.templateEditor.restore'))}>
                            {t('views.templateEditor.restore')}
                          </button>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
      </div>

      <Modal open={!!diffVersion} size="xlarge" onClose={() => setDiffVersion(null)} title={t('views.templateEditor.diffTitle', { version: diffVersion?.version })}>
        {diffVersion && <DiffViewer before={diffVersion.content} after={form.content} beforeLabel={`v${diffVersion.version}`} afterLabel={t('views.templateEditor.current')} />}
      </Modal>
      <ConfirmModal
        open={!!confirm}
        danger={confirm?.type === 'delete'}
        title={t(`views.templateEditor.confirm.${confirm?.type || 'reset'}Title`)}
        message={confirm?.type === 'delete' ? deleteInfo?.message || t('resources.promptTemplates.deleteUnbound', { name: form.name }) : t(`views.templateEditor.confirm.${confirm?.type || 'reset'}Message`, { version: confirm?.version, name: form.name })}
        details={confirm?.type === 'delete' ? deleteInfo?.details : null}
        onConfirm={runConfirmed}
        onCancel={() => setConfirm(null)}
      />
      <JsonViewerModal open={jsonOpen} value={template} onClose={() => setJsonOpen(false)} />
      {cloneOpen && template && (
        <CloneTemplateModal
          template={template}
          onClose={() => setCloneOpen(false)}
          onCloned={(created) => {
            setCloneOpen(false);
            addToast({ type: 'success', message: t('common.toasts.cloned') });
            if (created?.id) navigate(pathFor('promptTemplates', created.id));
          }}
        />
      )}
    </div>
  );
}
