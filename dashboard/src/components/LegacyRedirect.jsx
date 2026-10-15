import { Navigate, useLocation } from 'react-router-dom';
import { TAB_PARAM } from '../config/navConfig';

/**
 * Replace-redirect a pre-hub route to its hub and tab, keeping the original query string and hash.
 * The target tab wins over any `tab` parameter on the old URL. Omitting `tab` keeps the hub default.
 */
export default function LegacyRedirect({ to, tab }) {
  const location = useLocation();
  const params = new URLSearchParams();
  if (tab) params.set(TAB_PARAM, tab);
  new URLSearchParams(location.search).forEach((value, key) => {
    if (key !== TAB_PARAM) params.append(key, value);
  });
  const qs = params.toString();
  return <Navigate to={`${to}${qs ? `?${qs}` : ''}${location.hash || ''}`} replace />;
}
