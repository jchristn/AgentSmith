import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import ResourceView from '../components/ResourceView';
import Tabs from '../components/Tabs';
import { rolesConfig, permissionsConfig } from './resourceConfigs';

/** Roles page with tabs. */
export default function RolesView() {
  const { t } = useTranslation();
  const [tab, setTab] = useState('roles');
  return (
    <div>
      <Tabs
        label={t('nav.items.roles.label')}
        active={tab}
        onChange={setTab}
        tabs={[
          { id: 'roles', label: t('resources.roles.title'), tip: t('tips.nav.subtabs.roles') },
          { id: 'permissions', label: t('resources.permissions.title'), tip: t('tips.nav.subtabs.permissions') }
        ]}
      />
      {tab === 'roles' ? <ResourceView key="roles" config={rolesConfig} /> : <ResourceView key="permissions" config={permissionsConfig} />}
    </div>
  );
}
