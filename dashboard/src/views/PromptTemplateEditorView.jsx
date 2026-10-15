import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import PageHeader from '../components/PageHeader';
import Explainer from '../components/Explainer';
import LayeredTemplateEditor from '../components/LayeredTemplateEditor';
import { tip } from '../components/Tooltip';
import { pathFor } from '../config/navConfig';

/**
 * One prompt template key (P8.35, plan section 10.2): the layered editor for its Tenant and Global layers, with the
 * Built-in for reference, server preview, versions, diff, and reset. The route id is the template key, for example
 * /dashboard/prompts/connector.ssh.instructions.
 */
export default function PromptTemplateEditorView({ recordId }) {
  const { t } = useTranslation();
  const navigate = useNavigate();

  return (
    <div>
      <PageHeader
        title={recordId}
        subtitle={t('views.templateEditor.layeredSubtitle')}
        actions={
          <button type="button" className="button-secondary" onClick={() => navigate(pathFor('promptTemplates'))} {...tip(t('tips.views.templateEditor.back'))}>
            {t('common.actions.back')}
          </button>
        }
      />
      <Explainer topic="promptTemplates" />
      <LayeredTemplateEditor templateKey={recordId} testId="template-layered-editor" />
    </div>
  );
}
