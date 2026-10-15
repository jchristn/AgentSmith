import { createContext, useContext } from 'react';

/**
 * Set by HubView around the active tab panel. Views rendered inside a hub tab read it (through
 * PageHeader) to drop their own page title and document.title, since the hub already shows both.
 */
const HubContext = createContext({ embedded: false });

export const HubProvider = HubContext.Provider;

/** Current hub embedding state: { embedded: boolean }. */
export function useHub() {
  return useContext(HubContext);
}

export default HubContext;
