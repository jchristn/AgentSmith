import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import Modal from './Modal';
import { FormField } from './FormFields';
import { tip } from './Tooltip';
import { errorMessage } from '../utils/errors';
import { useAuth } from '../context/AuthContext';
import { useApp } from '../context/AppContext';

/** Suggested name for a copy: the source name without builtin/, plus -custom, then -custom-2 and so on until free. */
export function suggestCloneName(sourceName, takenNames = []) {
  const base = `${String(sourceName || 'template').replace(/^builtin\//, '')}-custom`;
  const taken = new Set(takenNames);
  if (!taken.has(base)) return base;
  for (let i = 2; i < 1000; i += 1) if (!taken.has(`${base}-${i}`)) return `${base}-${i}`;
  return `${base}-${Date.now()}`;
}

/**
 * Asks for the name of a prompt template copy (M1-07), prefilled with a free suggestion, and clones on Save.
 * `onCloned(created)` receives the new template.
 */
export default function CloneTemplateModal({ template, onClose, onCloned }) {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const { tenantId } = useApp();
  const [name, setName] = useState(() => suggestCloneName(template?.name));
  const [taken, setTaken] = useState([]);
  // A ref, not state: the name list may arrive after the operator started typing, and must not overwrite it.
  const touched = useRef(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);

  useEffect(() => {
    if (!apiClient) return;
    apiClient
      .listPromptTemplates(tenantId, { pageNumber: 1, pageSize: 1000, includeBuiltIn: true })
      .then((r) => {
        const names = (r.items || []).map((x) => x.name);
        setTaken(names);
        if (!touched.current) setName(suggestCloneName(template?.name, names));
      })
      .catch(() => setTaken([]));
  }, [apiClient, tenantId, template?.name]);

  const validate = (value) => {
    const v = value.trim();
    if (!v) return t('common.errors.required');
    if (v.startsWith('builtin/')) return t('views.templateEditor.cloneDialog.reserved');
    if (taken.includes(v)) return t('views.templateEditor.cloneDialog.taken', { name: v });
    return null;
  };

  const submit = async () => {
    const problem = validate(name);
    if (problem) {
      setError(problem);
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const created = await apiClient.clonePromptTemplate(tenantId, template.id, { name: name.trim() });
      onCloned?.(created);
    } catch (e) {
      setError(errorMessage(t, e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal
      open
      size="small"
      onClose={onClose}
      title={t('views.templateEditor.cloneDialog.title', { name: template?.name })}
      footer={
        <>
          <button type="button" className="button-secondary" onClick={onClose} disabled={busy} {...tip(t('tips.modal.cancelForm'))}>
            {t('common.actions.cancel')}
          </button>
          <button type="button" className="button-primary" onClick={submit} disabled={busy} data-testid="clone-confirm" {...tip(t('tips.views.templateEditor.cloneConfirm'))}>
            {busy ? t('common.generic.saving') : t('common.actions.clone')}
          </button>
        </>
      }
    >
      <p className="text-muted">{t('views.templateEditor.cloneDialog.body')}</p>
      <FormField
        id="clone-name"
        field={{ name: 'name', type: 'text', label: t('views.templateEditor.cloneDialog.name'), required: true, tip: t('tips.views.templateEditor.cloneName'), hint: t('views.templateEditor.cloneDialog.hint') }}
        value={name}
        onChange={(v) => {
          touched.current = true;
          setName(v);
          setError(null);
        }}
        error={error}
        disabled={busy}
      />
    </Modal>
  );
}
