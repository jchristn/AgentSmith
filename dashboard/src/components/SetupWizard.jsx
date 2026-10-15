import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import Modal from './Modal';
import FormFields from './FormFields';
import CodeBlock from './CodeBlock';
import ResourceSettingsPanel, { validateResourceConnector, resourceConnectorBody } from './ResourceSettingsPanel';
import { tip } from './Tooltip';
import { useAuth } from '../context/AuthContext';
import { useApp } from '../context/AppContext';
import { useToast } from '../context/ToastContext';
import { STORAGE_KEYS } from '../utils/constants';
import { enumOptions } from '../utils/enums';
import { pathFor } from '../config/navConfig';
import { errorMessage } from '../utils/errors';
import { mcpConfigSnippet } from '../utils/snippets';

export const WIZARD_STEPS = ['welcome', 'inference', 'embedding', 'resource', 'bind', 'connect'];

/** Default base URL per provider (blank where the provider has a fixed public endpoint). */
export const PROVIDER_BASE_URLS = { Ollama: 'http://127.0.0.1:11434', OpenAICompatible: 'http://127.0.0.1:8000/v1' };

/** Model endpoint form fields for the wizard (a subset of the Model Endpoints page). */
function modelFields(t, kind) {
  const f = (name, type, extra = {}) => ({ name, type, label: t(`resources.models.fields.${name}`), tip: t(`tips.resources.models.${name}`), ...extra });
  return [
    f('name', 'text', { required: true }),
    f('provider', 'select', { required: true, options: enumOptions(t, 'modelProvider') }),
    f('baseUrl', 'text'),
    f('model', 'text', { required: true, placeholder: kind === 'Embedding' ? 'nomic-embed-text' : 'llama3.1' }),
    f('apiKey', 'password', { showIf: (d) => d.provider !== 'Ollama', hint: t('wizard.apiKeyHint') })
  ];
}

/**
 * First-run setup (P8.17): an inference endpoint (required for chat), an optional embedding endpoint, a first
 * resource with its secret, binding it to the default Hugo profile, and connecting Claude Code. Every step can be
 * skipped; dismissal is remembered per tenant, and the sidebar footer reopens it.
 */
export default function SetupWizard() {
  const { t } = useTranslation();
  const { apiClient, serverUrl } = useAuth();
  const { tenantId, wizardOpen, setWizardOpen } = useApp();
  const { addToast } = useToast();
  const navigate = useNavigate();
  const [step, setStep] = useState(0);
  const [busy, setBusy] = useState(false);
  const [errors, setErrors] = useState({});
  const [inference, setInference] = useState({ name: 'local-llm', provider: 'Ollama', baseUrl: PROVIDER_BASE_URLS.Ollama, model: '', apiKey: '' });
  const [embedding, setEmbedding] = useState({ name: 'local-embeddings', provider: 'Ollama', baseUrl: PROVIDER_BASE_URLS.Ollama, model: '', apiKey: '' });
  const [resource, setResource] = useState({ name: '', environment: '', connectorType: '', settings: {}, secretBindings: {} });
  const [created, setCreated] = useState({ resource: null, profile: null });
  const [accessMode, setAccessMode] = useState('ReadOnly');

  useEffect(() => {
    if (!wizardOpen || !apiClient) return;
    apiClient
      .listProfiles({ pageNumber: 1, pageSize: 100 })
      .then((r) => setCreated((c) => ({ ...c, profile: (r.items || []).find((p) => p.isDefault) || (r.items || [])[0] || null })))
      .catch(() => {});
  }, [wizardOpen, apiClient]);

  const close = (dismiss) => {
    if (dismiss) localStorage.setItem(`${STORAGE_KEYS.setupDismissed}:${tenantId}`, 'true');
    setWizardOpen(false);
    setStep(0);
    setErrors({});
  };

  const next = () => {
    setErrors({});
    setStep((s) => Math.min(WIZARD_STEPS.length - 1, s + 1));
  };

  const saveModel = async (form, kind) => {
    const problems = {};
    if (!form.name) problems.name = t('common.errors.required');
    if (!form.model) problems.model = t('common.errors.required');
    setErrors(problems);
    if (Object.keys(problems).length) return;
    setBusy(true);
    try {
      let apiKeySecretId = null;
      if (form.apiKey) {
        const secret = await apiClient.createSecret({ name: `${form.name}-api-key`, kind: 'ApiToken', values: { token: form.apiKey } });
        apiKeySecretId = secret?.id || null;
      }
      await apiClient.createModelEndpoint({ name: form.name, kind, provider: form.provider, baseUrl: form.baseUrl || null, model: form.model, apiKeySecretId });
      addToast({ type: 'success', message: t('wizard.modelSaved') });
      next();
    } catch (e) {
      addToast({ type: 'error', message: errorMessage(t, e) });
    } finally {
      setBusy(false);
    }
  };

  const saveResource = async () => {
    const problems = { ...validateResourceConnector(t, resource, resource._connector) };
    if (!resource.name) problems.name = t('common.errors.required');
    setErrors(problems);
    if (Object.keys(problems).length) return;
    setBusy(true);
    try {
      const r = await apiClient.createResource({ name: resource.name, environment: resource.environment || null, visibility: 'Tenant', ...resourceConnectorBody(resource, resource._connector) });
      setCreated((c) => ({ ...c, resource: r }));
      addToast({ type: 'success', message: t('common.toasts.created') });
      next();
    } catch (e) {
      addToast({ type: 'error', message: errorMessage(t, e) });
    } finally {
      setBusy(false);
    }
  };

  const bind = async () => {
    if (!created.resource || !created.profile) {
      next();
      return;
    }
    setBusy(true);
    try {
      await apiClient.createBinding(created.profile.id, { resourceId: created.resource.id, alias: created.resource.name, accessMode, requireApprovalForDestructive: true, instructionMode: 'Inherit', enabled: true });
      addToast({ type: 'success', message: t('wizard.bound', { alias: created.resource.name, profile: created.profile.name }) });
      next();
    } catch (e) {
      addToast({ type: 'error', message: errorMessage(t, e) });
    } finally {
      setBusy(false);
    }
  };

  const current = WIZARD_STEPS[step];
  const optional = ['embedding', 'resource', 'bind'].includes(current);

  const primary = {
    welcome: { label: t('wizard.start'), tipKey: 'tips.wizard.start', run: next },
    inference: { label: t('wizard.saveModel'), tipKey: 'tips.wizard.saveModel', run: () => saveModel(inference, 'Inference') },
    embedding: { label: t('wizard.saveModel'), tipKey: 'tips.wizard.saveModel', run: () => saveModel(embedding, 'Embedding') },
    resource: { label: t('wizard.saveResource'), tipKey: 'tips.wizard.saveResource', run: saveResource },
    bind: { label: t('wizard.bind'), tipKey: 'tips.wizard.bind', run: bind },
    connect: {
      label: t('wizard.finish'),
      tipKey: 'tips.wizard.finish',
      run: () => {
        close(true);
        navigate(pathFor('chat'));
      }
    }
  }[current];

  return (
    <Modal
      open={wizardOpen}
      onClose={() => close(false)}
      size="large"
      title={t('wizard.title')}
      headerMeta={t('wizard.stepOf', { step: step + 1, total: WIZARD_STEPS.length })}
      testId="setup-wizard"
      footer={
        <>
          <button type="button" className="button-ghost" onClick={() => close(true)} {...tip(t('tips.wizard.skip'))}>
            {t('wizard.skip')}
          </button>
          {step > 0 && (
            <button type="button" className="button-secondary" onClick={() => setStep((s) => s - 1)} disabled={busy} {...tip(t('tips.wizard.back'))}>
              {t('common.actions.back')}
            </button>
          )}
          {(optional || current === 'inference') && (
            <button type="button" className="button-secondary" onClick={next} disabled={busy} data-testid="wizard-skip-step" {...tip(t('tips.wizard.skipStep'))}>
              {t('wizard.skipStep')}
            </button>
          )}
          <button type="button" className="button-primary" onClick={primary.run} disabled={busy} data-testid="wizard-primary" {...tip(t(primary.tipKey))}>
            {primary.label}
          </button>
        </>
      }
    >
      <ol className="wizard-steps" aria-label={t('wizard.title')}>
        {WIZARD_STEPS.map((s, i) => (
          <li key={s} className={i === step ? 'active' : i < step ? 'done' : ''}>
            {t(`wizard.steps.${s}`)}
          </li>
        ))}
      </ol>
      <div className="wizard-body" data-step={current}>
        <p>{t(`wizard.bodies.${current}`)}</p>
        {current === 'welcome' && (
          <ul className="plain-list">
            <li>{t('wizard.welcomeItem1')}</li>
            <li>{t('wizard.welcomeItem2')}</li>
            <li>{t('wizard.welcomeItem3')}</li>
          </ul>
        )}
        {current === 'inference' && (
          <FormFields
            fields={modelFields(t, 'Inference')}
            data={inference}
            errors={errors}
            idPrefix="wiz-inference"
            onChange={(d) => setInference(d.provider !== inference.provider ? { ...d, baseUrl: PROVIDER_BASE_URLS[d.provider] || '' } : d)}
          />
        )}
        {current === 'embedding' && (
          <FormFields
            fields={modelFields(t, 'Embedding')}
            data={embedding}
            errors={errors}
            idPrefix="wiz-embedding"
            onChange={(d) => setEmbedding(d.provider !== embedding.provider ? { ...d, baseUrl: PROVIDER_BASE_URLS[d.provider] || '' } : d)}
          />
        )}
        {current === 'resource' && (
          <>
            <FormFields
              fields={[
                { name: 'name', type: 'text', required: true, label: t('resources.resources.fields.name'), tip: t('tips.resources.resources.name') },
                { name: 'environment', type: 'text', label: t('resources.resources.fields.environment'), tip: t('tips.resources.resources.environment'), placeholder: 'prod' }
              ]}
              data={resource}
              errors={errors}
              idPrefix="wiz-resource"
              onChange={setResource}
            />
            <ResourceSettingsPanel data={resource} onChange={setResource} errors={errors} />
          </>
        )}
        {current === 'bind' &&
          (created.resource && created.profile ? (
            <FormFields
              fields={[{ name: 'accessMode', type: 'select', required: true, label: t('views.profile.bindings.fields.accessMode'), tip: t('tips.resources.bindings.accessMode'), options: enumOptions(t, 'accessMode') }]}
              data={{ accessMode }}
              idPrefix="wiz-bind"
              onChange={(d) => setAccessMode(d.accessMode)}
            />
          ) : (
            <p className="text-muted">{t('wizard.nothingToBind')}</p>
          ))}
        {current === 'connect' && <CodeBlock value={mcpConfigSnippet({ serverUrl, profile: created.profile?.name })} />}
      </div>
    </Modal>
  );
}
