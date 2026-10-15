import { useState, useEffect, useCallback, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import PageHeader from '../components/PageHeader';
import ConfirmModal from '../components/ConfirmModal';
import JsonViewerModal from '../components/JsonViewerModal';
import LanguageSelector from '../components/LanguageSelector';
import { LoadingState, ErrorBanner, EmptyState } from '../components/States';
import { Badge } from '../components/StatusBadge';
import { EyeIcon, EyeOffIcon } from '../components/Icons';
import { useAuth } from '../context/AuthContext';
import { useToast } from '../context/ToastContext';
import ApiClient from '../utils/api';
import { normalizeSettings, groupSettings, validateSetting, settingsSaveFailed, settingsUpdateBody } from '../utils/settings';
import { errorMessage, fieldErrorMessages } from '../utils/errors';
import { tip } from '../components/Tooltip';
import { optionalText } from '../utils/tips';

function SecretInput({ id, value, isSet, onChange, disabled, t, tipText }) {
  const [reveal, setReveal] = useState(false);
  return (
    <div className="password-wrap">
      <input id={id} {...tip(tipText)} type={reveal ? 'text' : 'password'} value={value ?? ''} disabled={disabled} placeholder={isSet ? t('views.settings.secretSet') : t('views.settings.secretNotSet')} onChange={(e) => onChange(e.target.value)} autoComplete="new-password" />
      <button type="button" className="icon-button" onClick={() => setReveal((v) => !v)} aria-label={reveal ? t('common.actions.hide') : t('common.actions.reveal')} {...tip(t(reveal ? 'tips.form.hide' : 'tips.form.reveal'))}>
        {reveal ? <EyeOffIcon /> : <EyeIcon />}
      </button>
    </div>
  );
}

/** Human label for a setting (M1-24): views.settings.fields.<Group>.<Name>, else the name split into words. */
function settingLabel(t, f) {
  const fallback = f.label || String(f.path || '').split('.').pop().replace(/([a-z0-9])([A-Z])/g, '$1 $2');
  return t(`views.settings.fields.${f.path}`, { defaultValue: fallback });
}

/** Form-based server and cluster settings with sources, live versus restart-required badges, and Restart Server. */
export default function SettingsView() {
  const { t } = useTranslation();
  const { apiClient, serverUrl, principal } = useAuth();
  const { addToast } = useToast();
  const [fields, setFields] = useState(null);
  const [raw, setRaw] = useState(null);
  const [edits, setEdits] = useState({});
  const [errors, setErrors] = useState({});
  const [error, setError] = useState(null);
  const [saving, setSaving] = useState(false);
  const [restartPending, setRestartPending] = useState(false);
  const [restarting, setRestarting] = useState(false);
  const [confirmRestart, setConfirmRestart] = useState(false);
  const [jsonOpen, setJsonOpen] = useState(false);
  const [dbTest, setDbTest] = useState(null);
  const canEdit = !!principal?.isAdmin;

  const load = useCallback(() => {
    if (!apiClient) return;
    apiClient
      .getSettings()
      .then((r) => {
        setRaw(r);
        setFields(normalizeSettings(r));
        setRestartPending((prev) => prev || !!r?.restartPending);
        setEdits({});
        setErrors({});
        setError(null);
      })
      .catch(setError);
  }, [apiClient]);

  useEffect(load, [load]);

  const groups = useMemo(() => groupSettings(fields || []), [fields]);
  const dirty = Object.keys(edits).length > 0;

  const fieldError = (f, v) => {
    const problem = validateSetting(f, v);
    return problem ? t(`views.settings.errors.${problem.key}`, problem.params) : null;
  };

  // Validate as the operator types so an out-of-range value is flagged at once; nothing is clamped (M1-01).
  const setValue = (f, v) => {
    setEdits((prev) => ({ ...prev, [f.path]: v }));
    setErrors((prev) => {
      const next = { ...prev };
      const message = f.isSecret && !v ? null : fieldError(f, v);
      if (message) next[f.path] = message;
      else delete next[f.path];
      return next;
    });
  };

  const save = async () => {
    const local = {};
    for (const f of fields || []) {
      if (!(f.path in edits)) continue;
      if (f.isSecret && !edits[f.path]) continue;
      const message = fieldError(f, edits[f.path]);
      if (message) local[f.path] = message;
    }
    if (Object.keys(local).length) {
      setErrors(local);
      addToast({ type: 'error', message: t('views.settings.notSaved') });
      return;
    }
    const values = {};
    for (const [path, v] of Object.entries(edits)) {
      const f = fields.find((x) => x.path === path);
      if (f?.isSecret && !v) continue;
      values[path] = v;
    }
    setSaving(true);
    try {
      const result = await apiClient.updateSettings(settingsUpdateBody(values));
      if (settingsSaveFailed(result)) {
        setErrors(fieldErrorMessages(t, { fields: result.errors || {}, fieldCodes: result.fieldCodes }) || {});
        addToast({ type: 'error', message: t('views.settings.notSaved') });
        return;
      }
      const needsRestart = result?.restartRequired ?? Object.keys(values).some((p) => fields.find((f) => f.path === p)?.applyMode === 'RestartRequired');
      if (needsRestart) setRestartPending(true);
      addToast({ type: 'success', message: needsRestart ? t('views.settings.savedRestart') : t('views.settings.savedLive') });
      load();
    } catch (e) {
      // 400 with errors keyed by setting path: show each next to its field. Nothing was saved.
      // 400 with errors and fieldCodes keyed by setting path: translate each code, using the field's own range.
      const perField = fieldErrorMessages(t, e, (path, code) => {
        const f = (fields || []).find((x) => x.path === path);
        if (!f) return null;
        if (code === 'OutOfRange' && f.min !== undefined && f.min !== null && f.max !== undefined && f.max !== null) return t('views.settings.errors.range', { min: f.min, max: f.max });
        if (code === 'NotAllowedValue' && f.options) return t('views.settings.errors.allowed', { values: f.options.join(', ') });
        return null;
      });
      if (perField) setErrors(perField);
      addToast({ type: 'error', message: perField ? t('views.settings.notSaved') : errorMessage(t, e) });
    } finally {
      setSaving(false);
    }
  };

  const restart = async () => {
    setConfirmRestart(false);
    setRestarting(true);
    try {
      await apiClient.restartServer();
    } catch {
      /* the connection may drop as the server exits */
    }
    const probe = new ApiClient(serverUrl, null);
    const started = Date.now();
    await new Promise((r) => setTimeout(r, 1500));
    for (;;) {
      try {
        await probe.healthCheck();
        break;
      } catch {
        if (Date.now() - started > 180000) {
          addToast({ type: 'error', message: t('views.settings.restartTimeout') });
          setRestarting(false);
          return;
        }
        await new Promise((r) => setTimeout(r, 2000));
      }
    }
    setRestarting(false);
    setRestartPending(false);
    addToast({ type: 'success', message: t('views.settings.restarted') });
    load();
  };

  const testDb = async () => {
    setDbTest({ busy: true });
    try {
      const body = {};
      for (const f of fields || []) if (f.path.startsWith('Database.')) body[f.path.slice(9).charAt(0).toLowerCase() + f.path.slice(10)] = f.path in edits ? edits[f.path] : f.value;
      const r = await apiClient.testDatabaseConnection(body);
      setDbTest({ ok: r?.ok ?? true, message: r?.message });
    } catch (e) {
      setDbTest({ ok: false, message: errorMessage(t, e) });
    }
  };

  if (error && !fields) {
    return (
      <div>
        <PageHeader title={t('views.settings.title')} subtitle={t('views.settings.subtitle')} />
        <ErrorBanner message={errorMessage(t, error)} onRetry={load} />
        <EmptyState title={t('views.settings.unavailableTitle')} body={t('views.settings.unavailableBody')} />
      </div>
    );
  }
  if (!fields) return <LoadingState />;

  return (
    <div className={restarting ? 'is-restarting' : ''}>
      <PageHeader
        title={t('views.settings.title')}
        subtitle={t('views.settings.subtitle')}
        actions={
          <>
            <LanguageSelector />
            <button type="button" className="button-secondary" onClick={() => setJsonOpen(true)} {...tip(t('tips.views.settings.viewJson'))}>
              {t('common.actions.viewJson')}
            </button>
            {canEdit && (
              <button type="button" className="button-danger" onClick={() => setConfirmRestart(true)} disabled={restarting} data-testid="restart-server" {...tip(t('tips.views.settings.restart'))}>
                {t('views.settings.restart')}
              </button>
            )}
            {canEdit && (
              <button type="button" className="button-primary" onClick={save} disabled={!dirty || saving || restarting} data-testid="save-settings" {...tip(t('tips.views.settings.save'))}>
                {saving ? t('common.generic.saving') : t('common.actions.save')}
              </button>
            )}
          </>
        }
      />
      {!canEdit && <div className="warning-banner">{t('views.settings.readOnly')}</div>}
      {restartPending && !restarting && <div className="warning-banner" role="status">{t('views.settings.restartBanner')}</div>}
      {restarting && (
        <div className="info-banner" role="status">
          <span className="loading-spinner small" /> {t('views.settings.restarting')}
        </div>
      )}
      {Object.keys(errors).length > 0 && <div className="error-banner">{t('common.errors.fixFields')}</div>}

      {groups.map((g) => (
        <fieldset key={g.name} className="panel settings-group">
          <legend className="section-title" {...tip(t(`tips.views.settings.groups.${g.name}`, { defaultValue: t('tips.views.settings.groupGeneric', { group: g.name }) }))}>{t(`views.settings.groups.${g.name}`, { defaultValue: g.name })}</legend>
          <div className="form-grid">
            {g.fields.map((f) => {
              const id = `setting-${f.path}`;
              const value = f.path in edits ? edits[f.path] : f.isSecret ? '' : f.value;
              const disabled = !canEdit || !f.editable || restarting;
              const label = settingLabel(t, f);
              const fieldTip = [optionalText(t, `tips.views.settings.fields.${f.path}`) || f.description || t('tips.views.settings.fieldGeneric', { path: f.path }), t('tips.views.settings.settingPath', { path: f.path }), t(f.applyMode === 'Live' ? 'tips.views.settings.applyLive' : 'tips.views.settings.applyRestart'), optionalText(t, `tips.views.settings.source.${f.source}`)]
                .filter(Boolean)
                .join(' ');
              return (
                <div key={f.path} className={`field ${errors[f.path] ? 'has-error' : ''} ${f.type === 'list' ? 'span-full' : ''}`}>
                  <label htmlFor={id} className="settings-label" {...tip(fieldTip)}>
                    <span>
                      {label} <code className="settings-path">{f.path}</code>
                    </span>
                    <span className="settings-badges">
                      <Badge tone={f.applyMode === 'Live' ? 'success' : 'warning'} tip={t(f.applyMode === 'Live' ? 'tips.views.settings.applyLive' : 'tips.views.settings.applyRestart')}>{f.applyMode === 'Live' ? t('views.settings.live') : t('views.settings.restartRequired')}</Badge>
                      <Badge tip={t(`tips.views.settings.source.${f.source}`, { defaultValue: t('tips.views.settings.sourceGeneric') })}>{t(`views.settings.sources.${f.source}`, { defaultValue: f.source })}</Badge>
                    </span>
                  </label>
                  {f.type === 'boolean' ? (
                    <input id={id} {...tip(fieldTip)} type="checkbox" checked={!!value} disabled={disabled} onChange={(e) => setValue(f, e.target.checked)} />
                  ) : f.isSecret ? (
                    <SecretInput id={id} value={value} isSet={f.isSet} disabled={disabled} onChange={(v) => setValue(f, v)} t={t} tipText={fieldTip} />
                  ) : f.options ? (
                    <select id={id} {...tip(fieldTip)} value={value ?? ''} disabled={disabled} onChange={(e) => setValue(f, e.target.value)}>
                      {f.options.map((o) => (
                        <option key={o} value={o}>
                          {o}
                        </option>
                      ))}
                    </select>
                  ) : f.type === 'number' || f.type === 'integer' ? (
                    <input id={id} {...tip(fieldTip)} type="number" min={f.min ?? undefined} max={f.max ?? undefined} value={value ?? ''} disabled={disabled} onChange={(e) => setValue(f, e.target.value === '' ? '' : Number(e.target.value))} />
                  ) : f.type === 'list' ? (
                    <textarea id={id} {...tip(fieldTip)} className="monospace" rows={3} value={(Array.isArray(value) ? value : []).join('\n')} disabled={disabled} onChange={(e) => setValue(f, e.target.value.split('\n'))} />
                  ) : (
                    <input id={id} {...tip(fieldTip)} type="text" value={value ?? ''} disabled={disabled} onChange={(e) => setValue(f, e.target.value)} />
                  )}
                  {(f.min !== undefined && f.min !== null) || (f.max !== undefined && f.max !== null) ? <div className="field-hint">{t('views.settings.range', { min: f.min ?? '-', max: f.max ?? '-' })}</div> : null}
                  {!f.editable && <div className="field-hint">{t(`views.settings.lockedBy.${f.source}`, { defaultValue: t('views.settings.lockedBy.other') })}</div>}
                  {errors[f.path] && (
                    <div className="field-error" role="alert" data-testid={`setting-error-${f.path}`}>
                      {errors[f.path]}
                    </div>
                  )}
                </div>
              );
            })}
          </div>
          {g.name === 'Database' && canEdit && (
            <div className="settings-panel-actions">
              <button type="button" className="button-secondary button-sm" onClick={testDb} disabled={dbTest?.busy} {...tip(t('tips.views.settings.testDatabase'))}>
                {dbTest?.busy ? t('views.settings.testing') : t('views.settings.testDatabase')}
              </button>
              {dbTest && !dbTest.busy && <Badge tone={dbTest.ok ? 'success' : 'danger'}>{dbTest.ok ? t('views.settings.dbOk') : dbTest.message || t('views.settings.dbFailed')}</Badge>}
            </div>
          )}
        </fieldset>
      ))}

      <ConfirmModal open={confirmRestart} danger title={t('views.settings.restartConfirmTitle')} message={t('views.settings.restartConfirmMessage')} confirmLabel={t('views.settings.restart')} confirmTip={t('tips.views.settings.restart')} onConfirm={restart} onCancel={() => setConfirmRestart(false)} />
      <JsonViewerModal open={jsonOpen} value={raw} onClose={() => setJsonOpen(false)} />
    </div>
  );
}
