import { useTranslation } from 'react-i18next';
import CodeBlock from './CodeBlock';
import CopyButton from './CopyButton';
import MarkdownView from './MarkdownView';
import { tip } from './Tooltip';

/** Browse advertised capabilities and prepare a message without executing a skill automatically. */
export default function AgentCardView({ card, onTrySkill, onExample, disabled }) {
  const { t } = useTranslation();
  const label = (key) => t(`views.a2aExplorer.${key}`);
  const modes = (items) =>
    (items || []).map((mode) => (
      <code key={mode} className="badge">
        {mode}
      </code>
    ));
  const interfaces = card.supportedInterfaces || (card.url ? [{ url: card.url, protocolBinding: card.preferredTransport || 'JSONRPC' }] : []);
  return (
    <section className="panel agent-card" data-testid="a2a-card-body">
      <header className="agent-card-heading">
        <h3 className="section-title">{card.name}</h3>
        {card.version && <span className="badge">{card.version}</span>}
      </header>
      {card.description && <MarkdownView text={card.description} />}
      <div className="agent-card-grid">
        {card.provider && (
          <div>
            <h4>{label('provider')}</h4>
            <p>{card.provider.organization}</p>
            {card.provider.url && (
              <div className="secret-row">
                <code>{card.provider.url}</code>
                <CopyButton value={card.provider.url} />
              </div>
            )}
          </div>
        )}
        <div>
          <h4>{label('inputModes')}</h4>
          <div className="agent-card-tags">{modes(card.defaultInputModes)}</div>
        </div>
        <div>
          <h4>{label('outputModes')}</h4>
          <div className="agent-card-tags">{modes(card.defaultOutputModes)}</div>
        </div>
        <div>
          <h4>{label('capabilities')}</h4>
          <dl className="agent-card-facts">
            {Object.entries(card.capabilities || {})
              .filter(([, value]) => typeof value === 'boolean')
              .map(([key, value]) => (
                <div key={key}>
                  <dt>
                    <code>{key}</code>
                  </dt>
                  <dd>{t(value ? 'common.boolean.yes' : 'common.boolean.no')}</dd>
                </div>
              ))}
          </dl>
        </div>
      </div>
      <h4>{label('interfaces')}</h4>
      {interfaces.map((endpoint, index) => (
        <div className="agent-card-endpoint" key={index}>
          <span className="badge">{endpoint.protocolBinding || endpoint.transport || 'JSONRPC'}</span>
          {endpoint.protocolVersion && <span>{endpoint.protocolVersion}</span>}
          <code>{endpoint.url}</code>
          <CopyButton value={endpoint.url || ''} />
        </div>
      ))}
      {card.securitySchemes && (
        <>
          <h4>{label('authentication')}</h4>
          <dl className="agent-card-facts">
            {Object.entries(card.securitySchemes).map(([name, entry]) => {
              const scheme =
                entry.httpAuthSecurityScheme || entry.apiKeySecurityScheme || entry.oauth2SecurityScheme || entry.openIdConnectSecurityScheme || entry;
              return (
                <div key={name}>
                  <dt>
                    <code>{name}</code>
                  </dt>
                  <dd>
                    {scheme.scheme || scheme.type || entry.type || name}
                    {scheme.description && <p className="field-hint">{scheme.description}</p>}
                  </dd>
                </div>
              );
            })}
          </dl>
        </>
      )}
      {card.securityRequirements?.length > 0 && (
        <details>
          <summary>{label('securityRequirements')}</summary>
          <CodeBlock value={card.securityRequirements} prettyJson />
        </details>
      )}
      <h4>{label('skills')}</h4>
      <p className="field-hint">{label('skillsHint')}</p>
      <div className="agent-card-skills">
        {(card.skills || []).map((skill) => (
          <article className="agent-skill" key={skill.id} data-testid="a2a-skill">
            <h5>{skill.name || skill.id}</h5>
            <div className="secret-row">
              <code>{skill.id}</code>
              <CopyButton value={skill.id} />
            </div>
            {skill.description && <MarkdownView text={skill.description} />}
            <div className="agent-card-tags">
              {(skill.tags || []).map((tag) => (
                <span className="badge" key={tag}>
                  {tag}
                </span>
              ))}
            </div>
            {skill.inputModes?.length > 0 && (
              <p>
                {label('inputModes')}: {modes(skill.inputModes)}
              </p>
            )}
            {skill.outputModes?.length > 0 && (
              <p>
                {label('outputModes')}: {modes(skill.outputModes)}
              </p>
            )}
            <button type="button" className="button-secondary" disabled={disabled} onClick={() => onTrySkill(skill)} {...tip(label('skillsHint'))}>
              {label('trySkill')}
            </button>
            {(skill.examples || []).map((example, index) => (
              <button
                type="button"
                className="agent-skill-example"
                disabled={disabled}
                key={index}
                onClick={() => onExample(example)}
                {...tip(label('useExample'))}
              >
                {example}
              </button>
            ))}
          </article>
        ))}
      </div>
      {!card.skills?.length && <p className="text-muted">{label('noSkills')}</p>}
      <details data-testid="a2a-card-json">
        <summary>{label('rawCard')}</summary>
        <CodeBlock value={card} prettyJson />
      </details>
    </section>
  );
}
