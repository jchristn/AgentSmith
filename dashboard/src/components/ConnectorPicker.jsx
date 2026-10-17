import { useId, useState } from 'react';
import { useTranslation } from 'react-i18next';
import ConnectorIcon from './ConnectorIcon';
import { LoadingState } from './States';
import { tip } from './Tooltip';
import './ConnectorPicker.css';

/** Visible connector choices in normal document flow, including descriptors supplied by plugins. */
export default function ConnectorPicker({ connectors = [], value, onChange, disabled = false, error, loading = false }) {
  const { t } = useTranslation();
  const id = useId();
  const [search, setSearch] = useState('');
  const query = search.trim().toLocaleLowerCase();
  const choices = connectors.filter((connector) =>
    [connector.displayName, connector.type, connector.family, connector.category, connector.description].some((part) =>
      String(part || '')
        .toLocaleLowerCase()
        .includes(query)
    )
  );
  return (
    <fieldset
      className="connector-picker"
      id="resource-connectorType"
      disabled={disabled}
      data-testid="connector-picker"
      aria-describedby={error ? `${id}-error` : undefined}
    >
      <legend>{t('views.resources.form.chooseConnector')}</legend>
      <label className="connector-picker-search" htmlFor={`${id}-search`}>
        <span>{t('views.connectors.search')}</span>
        <input
          id={`${id}-search`}
          type="search"
          value={search}
          onChange={(event) => setSearch(event.target.value)}
          {...tip(t('tips.views.connectors.search'))}
        />
      </label>
      {loading ? (
        <LoadingState />
      ) : choices.length === 0 ? (
        <p className="text-muted">{t('views.connectors.emptyTitle')}</p>
      ) : (
        <div className="connector-picker-grid">
          {choices.map((connector) => (
            <label key={connector.type} className={`connector-picker-card${value === connector.type ? ' selected' : ''}`}>
              <input
                type="radio"
                name={`${id}-connector`}
                value={connector.type}
                checked={value === connector.type}
                onChange={() => onChange(connector.type)}
                data-connector-type={connector.type}
              />
              <ConnectorIcon type={connector.type} iconKey={connector.iconKey} size={21} />
              <span>{connector.displayName || connector.type}</span>
            </label>
          ))}
        </div>
      )}
      {error && (
        <div className="field-error" id={`${id}-error`}>
          {error}
        </div>
      )}
    </fieldset>
  );
}
