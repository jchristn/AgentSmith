import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { FormField } from './FormFields';
import { tip } from './Tooltip';
import { UploadIcon, PlusIcon } from './Icons';
import { secretFieldsFor, describeSecretFile, secretsForSlot, validateSecretValues, secretValuesBody } from '../utils/secrets';
import { enumLabel, enumOptions } from '../utils/enums';
import { errorMessage } from '../utils/errors';
import { formatList } from '../i18n/formatters';

/** What an imported file contains, in one translated line (never the key material itself). */
function FileSummary({ info }) {
  const { t, i18n } = useTranslation();
  if (!info) return null;
  let text = '';
  if (info.kind === 'pem') text = t('components.secretField.file.pem', { type: info.type });
  else if (info.kind === 'serviceAccount')
    text = t('components.secretField.file.serviceAccount', { project: info.projectId || '-', email: info.clientEmail || '-' });
  else if (info.kind === 'kubeconfig')
    text = info.contexts.length
      ? t('components.secretField.file.kubeconfig', { contexts: formatList(info.contexts, i18n.language) })
      : t('components.secretField.file.kubeconfigNone');
  else if (info.kind === 'invalidJson') text = t('components.secretField.file.invalidJson');
  return (
    <div className={`field-hint ${info.kind === 'invalidJson' ? 'text-danger' : ''}`} data-testid="secret-file-summary">
      {text}
    </div>
  );
}

/**
 * The value fields of one secret kind (Password: username and password; AccessKeyPair: access key ID, secret
 * access key, session token; and so on). Multi-line fields accept a dropped or chosen file, read in the browser and
 * sent only to the encrypted field; a non-secret summary of the file is shown before saving.
 */
export function SecretValuesForm({ kind, values = {}, onChange, errors = {}, disabled = false, idPrefix = 'secret', replace = false }) {
  const { t } = useTranslation();
  const [fileInfo, setFileInfo] = useState({});

  const readFile = (name, file) => {
    if (!file) return;
    const reader = new FileReader();
    reader.onload = () => {
      const text = String(reader.result || '');
      onChange({ ...values, [name]: text });
      setFileInfo((prev) => ({ ...prev, [name]: describeSecretFile(name, text) }));
    };
    reader.readAsText(file);
  };

  return (
    <div className="secret-values" data-kind={kind}>
      {secretFieldsFor(kind).map((f) => {
        const id = `${idPrefix}-${f.name}`;
        const label = t(`components.secretField.fields.${f.name}`);
        const field = {
          name: f.name,
          label,
          required: f.required && !replace,
          type: f.multiline ? 'textarea' : f.secret ? 'password' : 'text',
          rows: f.multiline ? 6 : undefined,
          tip: t(`tips.secretField.fields.${f.name}`),
          hint: replace ? t('components.secretField.replaceHint') : undefined,
          span: f.multiline ? 'full' : undefined
        };
        return (
          <div
            key={f.name}
            className={`secret-value-field ${f.file ? 'droppable' : ''}`}
            onDragOver={f.file ? (e) => e.preventDefault() : undefined}
            onDrop={
              f.file
                ? (e) => {
                    e.preventDefault();
                    readFile(f.name, e.dataTransfer?.files?.[0]);
                  }
                : undefined
            }
          >
            <FormField
              id={id}
              field={field}
              value={values[f.name] ?? ''}
              error={errors[f.name]}
              disabled={disabled}
              onChange={(v) => onChange({ ...values, [f.name]: v })}
            />
            {f.file && !disabled && (
              <label className="button-secondary button-sm file-pick" {...tip(t('tips.secretField.import'))}>
                <UploadIcon size={14} /> {t('components.secretField.import')}
                <input
                  type="file"
                  className="sr-only"
                  aria-label={t('components.secretField.importNamed', { field: label })}
                  onChange={(e) => readFile(f.name, e.target.files?.[0])}
                />
              </label>
            )}
            {f.file && <FileSummary info={fileInfo[f.name]} />}
          </div>
        );
      })}
    </div>
  );
}

/**
 * Secret slot picker (P8.12): choose an existing secret whose kind the connector slot accepts, or create one inline.
 * value: { secretId, secretFieldName }. slot: SecretSlotDescriptor { name, label, help, acceptedKinds, required }.
 * `createSecret(body)` persists a new secret and resolves to it; the picker then selects it.
 */
export default function SecretSlotPicker({ slot, value, secrets = [], onChange, createSecret, disabled = false, idPrefix = 'slot', error }) {
  const { t } = useTranslation();
  const [creating, setCreating] = useState(false);
  const accepted = Array.isArray(slot?.acceptedKinds) && slot.acceptedKinds.length ? slot.acceptedKinds : null;
  const [draft, setDraft] = useState({ name: '', kind: accepted ? accepted[0] : 'Generic', values: {} });
  const [draftErrors, setDraftErrors] = useState({});
  const [busy, setBusy] = useState(false);
  const options = secretsForSlot(secrets, slot);
  const label = slot?.label || slot?.name;
  const kindsText = accepted ? accepted.map((k) => enumLabel(t, 'secretKind', k)).join(', ') : t('components.secretField.anyKind');

  const save = async () => {
    const problems = validateSecretValues(draft.kind, draft.values);
    if (!draft.name) problems.name = 'required';
    setDraftErrors(Object.fromEntries(Object.entries(problems).map(([k]) => [k, t('common.errors.required')])));
    if (Object.keys(problems).length) return;
    setBusy(true);
    try {
      const created = await createSecret({ name: draft.name, kind: draft.kind, values: secretValuesBody(draft.kind, draft.values) });
      onChange({ secretId: created?.id, secretFieldName: null });
      setCreating(false);
      setDraft({ name: '', kind: accepted ? accepted[0] : 'Generic', values: {} });
    } catch (e) {
      setDraftErrors({ _form: errorMessage(t, e) });
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="secret-slot" data-slot={slot?.name}>
      <FormField
        id={`${idPrefix}-${slot?.name}`}
        field={{
          name: slot?.name,
          type: 'select',
          label,
          required: !!slot?.required,
          tip: slot?.help || t('tips.secretField.slot', { slot: label }),
          hint: t('components.secretField.accepts', { kinds: kindsText }),
          noneLabel: t('components.secretField.noneSelected'),
          allowNone: true,
          options: options.map((s) => ({ value: s.id, label: `${s.name} (${enumLabel(t, 'secretKind', s.kind)})` }))
        }}
        value={value?.secretId || ''}
        error={error}
        disabled={disabled}
        onChange={(v) => onChange({ secretId: v, secretFieldName: null })}
      />
      {!disabled && createSecret && !creating && (
        <button
          type="button"
          className="button-ghost button-sm"
          onClick={() => setCreating(true)}
          data-testid={`create-secret-${slot?.name}`}
          {...tip(t('tips.secretField.createInline'))}
        >
          <PlusIcon size={14} /> {t('components.secretField.createInline')}
        </button>
      )}
      {creating && (
        <div className="inline-panel" data-testid="inline-secret">
          {draftErrors._form && <div className="error-banner">{draftErrors._form}</div>}
          <div className="form-grid">
            <FormField
              id={`${idPrefix}-new-name`}
              field={{ name: 'name', type: 'text', label: t('resources.secrets.fields.name'), required: true, tip: t('tips.resources.secrets.name') }}
              value={draft.name}
              error={draftErrors.name}
              onChange={(v) => setDraft((d) => ({ ...d, name: v }))}
            />
            <FormField
              id={`${idPrefix}-new-kind`}
              field={{
                name: 'kind',
                type: 'select',
                label: t('resources.secrets.fields.kind'),
                required: true,
                tip: t('tips.resources.secrets.kind'),
                options: enumOptions(t, 'secretKind', accepted || undefined)
              }}
              value={draft.kind}
              onChange={(v) => setDraft((d) => ({ ...d, kind: v, values: {} }))}
            />
          </div>
          <SecretValuesForm
            kind={draft.kind}
            values={draft.values}
            errors={draftErrors}
            idPrefix={`${idPrefix}-new`}
            onChange={(v) => setDraft((d) => ({ ...d, values: v }))}
          />
          <div className="inline-panel-actions">
            <button
              type="button"
              className="button-secondary button-sm"
              onClick={() => setCreating(false)}
              disabled={busy}
              {...tip(t('tips.modal.cancelForm'))}
            >
              {t('common.actions.cancel')}
            </button>
            <button type="button" className="button-primary button-sm" onClick={save} disabled={busy} {...tip(t('tips.secretField.saveInline'))}>
              {t('components.secretField.saveInline')}
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
