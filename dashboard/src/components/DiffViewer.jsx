import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { diffLines } from '../utils/diff';

/** Line diff between two texts (used for prompt template versions and manifest dry runs). */
export default function DiffViewer({ before, after, beforeLabel, afterLabel }) {
  const { t } = useTranslation();
  const lines = useMemo(() => diffLines(before || '', after || ''), [before, after]);
  return (
    <div className="diff-viewer">
      <div className="diff-legend">
        <span className="diff-del">- {beforeLabel || t('components.diff.before')}</span>
        <span className="diff-add">+ {afterLabel || t('components.diff.after')}</span>
      </div>
      <pre className="diff-body monospace">
        {lines.map((l, i) => (
          <div key={i} className={`diff-line diff-${l.type}`}>
            <span className="diff-gutter" aria-hidden="true">
              {l.type === 'add' ? '+' : l.type === 'del' ? '-' : ' '}
            </span>
            {l.text || ' '}
          </div>
        ))}
      </pre>
    </div>
  );
}
