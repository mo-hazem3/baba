import { Alert, App, Button, Input, Modal, Space, Table, Tag } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { approveRequest, rejectRequest } from '../../api/generated/baba'
import type { ApprovalDto } from '../../api/generated/model'
import { refreshApprovals, refreshBooks, useApprovals, useCurrentCompany, useCurrencies } from '../../api/hooks'
import { AmountText } from '../../layout/AmountText'
import { EmptyState } from '../../layout/EmptyState'
import { PageHeader } from '../../layout/PageHeader'
import { useSettings } from '../../settings/SettingsContext'
import { formatDate, formatDateTime, parseIsoDate } from '../../utils/format'
import { securityMessage } from './messages'

/** What a request is about, in words: "Payment", "Sales invoice" ... */
export const approvalKindLabel = (request: Pick<ApprovalDto, 'subject' | 'kind'>, t: (key: string) => string): string =>
  request.subject === 'Voucher' ? t(`voucher.title.${request.kind}`) : t(`trade.title.${request.kind}`)

/** The screen of the thing the request is about. */
export const approvalPath = (request: Pick<ApprovalDto, 'subject' | 'subjectId'>): string =>
  request.subject === 'Voucher' ? `/vouchers/open/${request.subjectId}` : `/documents/open/${request.subjectId}`

/**
 * Vouchers and invoices sent for approval (brief section 10.4). People who may approve see what waits for them and post it with one
 * click or send it back with a reason; everyone sees what they sent and what became of it.
 */
export function ApprovalsPage() {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const company = useCurrentCompany()
  const currencies = useCurrencies()
  const approvals = useApprovals()
  const [rejecting, setRejecting] = useState<ApprovalDto>()
  const [reason, setReason] = useState('')

  const minorUnitsOf = (code: string) => currencies.data?.find((c) => c.code === code)?.minorUnits ?? 2
  const baseCurrency = company.data?.baseCurrencyCode

  const approve = useMutation({
    mutationFn: (request: ApprovalDto) => approveRequest(request.id),
    onSuccess: async (response) => {
      await refreshBooks(queryClient)
      void message.success(t('security.approvals.approved', { number: response.data.number ?? '' }))
    },
    onError: async (error) => {
      await refreshApprovals(queryClient)
      void message.error(securityMessage(error, t))
    },
  })
  const reject = useMutation({
    mutationFn: (request: ApprovalDto) => rejectRequest(request.id, { note: reason.trim() || null }),
    onSuccess: async () => {
      await refreshApprovals(queryClient)
      setRejecting(undefined)
      setReason('')
      void message.success(t('security.approvals.rejected'))
    },
    onError: (error) => void message.error(securityMessage(error, t)),
  })

  const rows = approvals.data ?? []
  const columns: ColumnsType<ApprovalDto> = [
    {
      title: t('security.approvals.what'),
      key: 'what',
      render: (_: unknown, r) => (
        <Link to={approvalPath(r)}>
          {approvalKindLabel(r, t)} {r.number ?? t('voucher.draft')}
        </Link>
      ),
    },
    { title: t('voucher.date'), key: 'date', width: 120, render: (_: unknown, r) => formatDate(parseIsoDate(r.date), settings.digits, settings.hijri) },
    {
      title: t('security.approvals.amount'),
      key: 'amount',
      align: 'end',
      render: (_: unknown, r) => (
        <span>
          <AmountText value={r.amount} minorUnits={minorUnitsOf(r.currencyCode)} /> <small>{r.currencyCode || baseCurrency}</small>
        </span>
      ),
    },
    {
      title: t('security.approvals.sentBy'),
      key: 'by',
      render: (_: unknown, r) => (
        <span>
          {r.submittedBy}
          <br />
          <small className="muted">{formatDateTime(r.submittedAt, settings.digits)}</small>
          {r.note && (
            <>
              <br />
              <small>{r.note}</small>
            </>
          )}
        </span>
      ),
    },
    {
      title: t('parties.status'),
      key: 'state',
      render: (_: unknown, r) => (
        <span>
          {r.state === 'Pending' && <Tag color="gold">{t('security.approvals.state.Pending')}</Tag>}
          {r.state === 'Approved' && <Tag color="green">{t('security.approvals.state.Approved')}</Tag>}
          {r.state === 'Rejected' && <Tag color="red">{t('security.approvals.state.Rejected')}</Tag>}
          {r.decidedBy && (
            <>
              <br />
              <small className="muted">{t('security.approvals.decidedBy', { name: r.decidedBy })}</small>
            </>
          )}
          {r.decisionNote && (
            <>
              <br />
              <small>{r.decisionNote}</small>
            </>
          )}
        </span>
      ),
    },
    {
      title: <span className="visually-hidden">{t('voucher.rowActions')}</span>,
      key: 'actions',
      render: (_: unknown, r) =>
        r.canDecide && (
          <Space size={4} wrap>
            <Button type="primary" size="small" onClick={() => approve.mutate(r)} loading={approve.isPending && approve.variables?.id === r.id} disabled={approve.isPending || reject.isPending} aria-label={`${t('security.approvals.approve')} ${approvalKindLabel(r, t)} ${r.number ?? ''}`}>
              {t('security.approvals.approve')}
            </Button>
            <Button size="small" onClick={() => setRejecting(r)} aria-label={`${t('security.approvals.reject')} ${approvalKindLabel(r, t)} ${r.number ?? ''}`}>
              {t('security.approvals.reject')}
            </Button>
          </Space>
        ),
    },
  ]

  return (
    <div>
      <PageHeader title={t('security.approvals.title')} help="approvals" crumbs={[{ label: t('breadcrumb.home'), to: '/' }, { label: t('security.approvals.title') }]} />
      <Alert type="info" showIcon message={t('security.approvals.intro')} className="form-alert" />
      {approvals.isSuccess && rows.length === 0 ? (
        <EmptyState title={t('security.approvals.emptyTitle')} body={t('security.approvals.emptyBody')} />
      ) : (
        <Table<ApprovalDto> columns={columns} dataSource={[...rows]} rowKey="id" loading={approvals.isPending} pagination={false} size="middle" bordered />
      )}
      <Modal
        open={rejecting !== undefined}
        title={t('security.approvals.rejectTitle')}
        onCancel={() => setRejecting(undefined)}
        onOk={() => rejecting && reject.mutate(rejecting)}
        okText={t('security.approvals.reject')}
        cancelText={t('common.cancel')}
        confirmLoading={reject.isPending}
        maskClosable={false}
        destroyOnHidden
      >
        <p>{t('security.approvals.rejectBody')}</p>
        <Input.TextArea value={reason} onChange={(e) => setReason(e.target.value)} rows={3} maxLength={300} aria-label={t('security.approvals.reason')} placeholder={t('security.approvals.reason')} autoFocus />
      </Modal>
    </div>
  )
}
