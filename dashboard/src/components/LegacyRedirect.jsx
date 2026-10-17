import { Navigate, useLocation } from 'react-router-dom';
import { TAB_PARAM } from '../config/navConfig';

/**
 * Replace-redirect a pre-hub route to its hub and tab, keeping the original query string and hash.
 * The target tab wins unless `tabs` explicitly allows a tab from a previous hub URL.
 * Omitting `tab` keeps the hub default.
 */
export default function LegacyRedirect({ to, tab, tabs }) {
  const location = useLocation();
  const params = new URLSearchParams();
  const requested = new URLSearchParams(location.search).get(TAB_PARAM);
  const target = tabs?.includes(requested) ? requested : tab;
  if (target) params.set(TAB_PARAM, target);
  new URLSearchParams(location.search).forEach((value, key) => {
    if (key !== TAB_PARAM) params.append(key, value);
  });
  const qs = params.toString();
  return <Navigate to={`${to}${qs ? `?${qs}` : ''}${location.hash || ''}`} replace />;
}
