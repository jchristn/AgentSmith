import { useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { useHub } from '../context/HubContext';

/**
 * Route page header. Sets document.title and renders title, summary, and actions. Inside a hub tab
 * (see HubView) the hub already renders the title and document.title, so the header collapses to a
 * compact row with just the summary and actions.
 */
export default function PageHeader({ title, subtitle, actions }) {
  const { t } = useTranslation();
  const { embedded } = useHub();
  useEffect(() => {
    if (title && !embedded) document.title = `${title} | ${t('common.appName')}`;
  }, [title, t, embedded]);

  if (embedded) {
    if (!subtitle && !actions) return null;
    return (
      <div className="page-header embedded">
        <div className="page-header-text">{subtitle && <div className="page-header-subtitle">{subtitle}</div>}</div>
        {actions && <div className="page-header-actions">{actions}</div>}
      </div>
    );
  }

  return (
    <header className="page-header">
      <div className="page-header-text">
        <h1 className="page-header-title">{title}</h1>
        {subtitle && <div className="page-header-subtitle">{subtitle}</div>}
      </div>
      {actions && <div className="page-header-actions">{actions}</div>}
    </header>
  );
}
