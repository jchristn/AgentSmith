import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router-dom';
import ResourceView from '../components/ResourceView';
import Tabs from '../components/Tabs';
import { userAssignmentsConfig, credentialAssignmentsConfig } from './resourceConfigs';

/** Assignments page with tabs: user role assignments and credential scope assignments (the assignment wizard). */
export default function AssignmentsView() {
  const { t } = useTranslation();
  const [searchParams] = useSearchParams();
  // ?view=credentials (or a credentialId) opens the credential scope tab, for links from the Credentials page.
  const [tab, setTab] = useState(() =>
    searchParams.get('view') === 'credentials' || searchParams.get('credentialId') ? 'credentialAssignments' : 'userAssignments'
  );
  return (
    <div>
      <Tabs
        label={t('nav.items.assignments.label')}
        active={tab}
        onChange={setTab}
        tabs={[
          { id: 'userAssignments', label: t('resources.userAssignments.title'), tip: t('tips.nav.subtabs.userAssignments') },
          { id: 'credentialAssignments', label: t('resources.credentialAssignments.title'), tip: t('tips.nav.subtabs.credentialAssignments') }
        ]}
      />
      {tab === 'userAssignments' ? (
        <ResourceView key="userAssignments" config={userAssignmentsConfig} />
      ) : (
        <ResourceView key="credentialAssignments" config={credentialAssignmentsConfig} />
      )}
    </div>
  );
}
