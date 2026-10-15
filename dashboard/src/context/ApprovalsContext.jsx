import { createContext, useContext } from 'react';

/**
 * Everything waiting for a person in the signed-in tenant, provided by the dashboard shell: permission approvals
 * (P12.3) and pipeline stage reviews (P12.9). `pending` is their sum for the one sidebar badge, or null when both are
 * unknown (not loaded yet, or the caller may not read approvals). `approvalsPending` and `reviewsPending` are the two
 * parts (each null when unknown). `refresh()` re-reads the counts, for example right after a decision.
 */
const ApprovalsContext = createContext({ pending: null, approvalsPending: null, reviewsPending: null, refresh: () => {} });

export const ApprovalsProvider = ApprovalsContext.Provider;

/** Current pending counts (combined, approvals, stage reviews) and a refresh function. */
export function usePendingApprovals() {
  return useContext(ApprovalsContext);
}

export default ApprovalsContext;
