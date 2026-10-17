import {
  BotIcon,
  BoxIcon,
  BucketIcon,
  ChartIcon,
  CloudIcon,
  ClusterIcon,
  DatabaseIcon,
  FileTextIcon,
  GlobeIcon,
  PlugIcon,
  SearchIcon,
  TerminalIcon,
  ZapIcon
} from './Icons';

/**
 * Icon per descriptor iconKey (ConnectorDescriptor.IconKey on the server). Unknown keys fall back to the plug.
 */
export const CONNECTOR_ICON_KEYS = {
  terminal: TerminalIcon,
  bucket: BucketIcon,
  database: DatabaseIcon,
  logs: FileTextIcon,
  cache: ZapIcon,
  cloud: CloudIcon,
  cluster: ClusterIcon,
  container: BoxIcon,
  globe: GlobeIcon,
  chart: ChartIcon,
  search: SearchIcon,
  bot: BotIcon,
  plug: PlugIcon
};

/**
 * Icon per built-in connector type. A type listed here wins over the descriptor's iconKey, so built-in connectors
 * keep a stable icon even when a server omits the key; plugin connectors use their iconKey.
 */
export const CONNECTOR_TYPE_ICONS = {
  ssh: TerminalIcon,
  s3: BucketIcon,
  'azure-blob': BucketIcon,
  gcs: BucketIcon,
  sqlite: DatabaseIcon,
  mysql: DatabaseIcon,
  sqlserver: DatabaseIcon,
  postgresql: DatabaseIcon,
  'solarwinds-observability': FileTextIcon,
  redis: ZapIcon,
  aws: CloudIcon,
  kubernetes: ClusterIcon,
  docker: BoxIcon,
  http: GlobeIcon,
  prometheus: ChartIcon,
  loki: FileTextIcon,
  elasticsearch: SearchIcon,
  mcp: PlugIcon,
  'a2a-agent': BotIcon
};

/** Resolve the icon component for a connector type and optional descriptor iconKey. */
export function connectorIconFor(type, iconKey) {
  return CONNECTOR_TYPE_ICONS[type] || CONNECTOR_ICON_KEYS[iconKey] || PlugIcon;
}

/** Connector icon. Props: type (connector type), iconKey (descriptor icon key), size. Decorative: hidden from AT. */
export default function ConnectorIcon({ type, iconKey, size = 18 }) {
  const Icon = connectorIconFor(type, iconKey);
  return (
    <span className="connector-icon" aria-hidden="true" data-connector-icon={type || iconKey || 'plug'}>
      <Icon size={size} />
    </span>
  );
}
