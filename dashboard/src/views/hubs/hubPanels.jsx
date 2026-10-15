import ResourceView from '../../components/ResourceView';
import HomeView from '../HomeView';
import ChatView from '../ChatView';
import ProfileDetailView from '../ProfileDetailView';
import ConnectorsView from '../ConnectorsView';
import ConnectorDetailView from '../ConnectorDetailView';
import ResourceDetailView from '../ResourceDetailView';
import PromptTemplateEditorView from '../PromptTemplateEditorView';
import RequestHistoryView from '../RequestHistoryView';
import AnalyticsView from '../AnalyticsView';
import ApiExplorerView from '../ApiExplorerView';
import McpExplorerView from '../McpExplorerView';
import A2aExplorerView from '../A2aExplorerView';
import RolesView from '../RolesView';
import AssignmentsView from '../AssignmentsView';
import SettingsView from '../SettingsView';
import ServerInfoView from '../ServerInfoView';
import { tenantsConfig, usersConfig, credentialsConfig, promptTemplatesConfig, auditConfig } from '../resourceConfigs';
import {
  profilesConfig,
  approvalsConfig,
  conversationsConfig,
  resourcesConfig,
  secretsConfig,
  modelsConfig,
  knowledgeConfig,
  toolInvocationsConfig,
  sessionsConfig,
  pluginsConfig
} from '../hugoConfigs';

/** A record page rendered from a declarative configuration. */
function recordView(config, name) {
  function RecordView() {
    return <ResourceView config={config} />;
  }
  RecordView.displayName = name;
  return RecordView;
}

/**
 * View components keyed by page id (see config/navConfig.js). Labels, ordering, hub membership,
 * and gating live in the nav config; this only maps ids to components.
 */
export const PAGE_VIEWS = {
  home: HomeView,
  chat: ChatView,
  profiles: recordView(profilesConfig, 'ProfilesView'),
  approvals: recordView(approvalsConfig, 'ApprovalsView'),
  conversations: recordView(conversationsConfig, 'ConversationsView'),
  resources: recordView(resourcesConfig, 'ResourcesView'),
  secrets: recordView(secretsConfig, 'SecretsView'),
  connectors: ConnectorsView,
  models: recordView(modelsConfig, 'ModelsView'),
  promptTemplates: recordView(promptTemplatesConfig, 'PromptTemplatesView'),
  knowledge: recordView(knowledgeConfig, 'KnowledgeView'),
  toolInvocations: recordView(toolInvocationsConfig, 'ToolInvocationsView'),
  requests: RequestHistoryView,
  analytics: AnalyticsView,
  audit: recordView(auditConfig, 'AuditView'),
  explorer: ApiExplorerView,
  mcpExplorer: McpExplorerView,
  a2aExplorer: A2aExplorerView,
  tenants: recordView(tenantsConfig, 'TenantsView'),
  users: recordView(usersConfig, 'UsersView'),
  credentials: recordView(credentialsConfig, 'CredentialsView'),
  roles: RolesView,
  assignments: AssignmentsView,
  sessions: recordView(sessionsConfig, 'SessionsView'),
  settings: SettingsView,
  serverInfo: ServerInfoView,
  plugins: recordView(pluginsConfig, 'PluginsView')
};

/** Detail views for `/dashboard/<slug>/<id>`, keyed by page id (pages marked `detail: true`). */
export const DETAIL_VIEWS = {
  chat: ChatView,
  profiles: ProfileDetailView,
  connectors: ConnectorDetailView,
  resources: ResourceDetailView,
  promptTemplates: PromptTemplateEditorView
};
