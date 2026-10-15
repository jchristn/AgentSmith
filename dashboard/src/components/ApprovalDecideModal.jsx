import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import Modal from './Modal';
import ApprovalCard from './ApprovalCard';
import { tip } from './Tooltip';
import { useAuth } from '../context/AuthContext';
import { useToast } from '../context/ToastContext';
import { usePendingApprovals } from '../context/ApprovalsContext';
import { errorMessage } from '../utils/errors';

/**
 * Decide one approval from the inbox: the approval card with Approve and Reject and an optional note. A 409 (already
 * decided or expired) and a 403 end in a toast; the list and the badges refresh either way.
 */
export default function ApprovalDecideModal({ approval, onClose, onDecided }) {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const { addToast } = useToast();
  const { refresh } = usePendingApprovals();
  const [busy, setBusy] = useState(false);

  const decide = async (approve, note) => {
    setBusy(true);
    try {
      if (approve) await apiClient.approveApproval(approval.id, note);
      else await apiClient.rejectApproval(approval.id, note);
      addToast({ type: 'success', message: t(approve ? 'resources.approvals.approved' : 'resources.approvals.rejected') });
    } catch (e) {
      // 409 is either NotPending (already decided or expired) or SelfApprovalNotAllowed (four eyes required).
      const notPending = e?.status === 409 && e?.code !== 'SelfApprovalNotAllowed';
      addToast({
        type: e?.status === 409 ? 'warning' : 'error',
        message: notPending
          ? t('resources.approvals.notPending')
          : e?.code === 'SelfApprovalNotAllowed'
            ? t('resources.approvals.selfApproval')
            : errorMessage(t, e)
      });
    } finally {
      setBusy(false);
      refresh?.();
      onDecided?.();
      onClose?.();
    }
  };

  return (
    <Modal
      open
      onClose={onClose}
      size="large"
      title={t('resources.approvals.actions.decide')}
      testId="approval-decide"
      footer={
        <button type="button" className="button-secondary" onClick={onClose} disabled={busy} {...tip(t('tips.modal.close'))}>
          {t('common.actions.close')}
        </button>
      }
    >
      <ApprovalCard approval={approval} busy={busy} onApprove={(note) => decide(true, note)} onReject={(note) => decide(false, note)} />
    </Modal>
  );
}
