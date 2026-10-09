import { Alert } from 'antd'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { useApprovals } from '../../api/hooks'
import { useAccess } from './useAccess'

/** On a voucher or document: says it is waiting for approval, or why it came back. Nothing at all when it was never sent. */
export function ApprovalNotice({ subjectId }: { subjectId: string | undefined }) {
  const { t } = useTranslation()
  const { accountsOn } = useAccess()
  const approvals = useApprovals(accountsOn && subjectId !== undefined)
  const request = approvals.data?.find((a) => a.subjectId === subjectId && a.state !== 'Approved')
  if (!request) return null

  return request.state === 'Pending' ? (
    <Alert
      type="info"
      showIcon
      className="form-alert"
      message={t('security.approvals.waiting', { name: request.submittedBy })}
      description={<Link to="/approvals">{t('security.approvals.seeAll')}</Link>}
    />
  ) : (
    <Alert
      type="warning"
      showIcon
      className="form-alert"
      message={t('security.approvals.sentBack', { name: request.decidedBy ?? '' })}
      description={request.decisionNote ?? t('security.approvals.noReason')}
    />
  )
}
