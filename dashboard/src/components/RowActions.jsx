import ActionMenu from './ActionMenu';
import { tip } from './Tooltip';

/**
 * Row actions: up to two primary actions as inline buttons (the most common ones, such as Approve), and everything
 * else in the portaled action menu, so menus are never clipped by the table. `primary` and `items` use the
 * ActionMenu item shape { label, tip, onClick, danger?, hidden? }.
 */
export default function RowActions({ primary = [], items = [], label = '' }) {
  const inline = primary.filter((a) => !a.hidden).slice(0, 2);
  return (
    <span className="row-actions" data-ignore-row-click>
      {inline.map((a) => (
        <button key={a.label} type="button" className={`button-sm ${a.danger ? 'button-danger' : 'button-secondary'}`} onClick={a.onClick} {...tip(a.tip)}>
          {a.label}
        </button>
      ))}
      {items.some((a) => !a.hidden) && <ActionMenu items={items} label={label} />}
    </span>
  );
}
