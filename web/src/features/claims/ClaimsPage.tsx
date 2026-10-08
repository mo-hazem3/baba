import { Alert, App, Button, Form, Input, InputNumber, Modal, Select, Space, Table, Tag } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { approveClaim, deleteClaim, payClaim, rejectClaim, saveClaim, submitClaim, unapproveClaim, unpayClaim } from '../../api/generated/baba'
import type { ClaimDto, ClaimInput, ClaimStatus } from '../../api/generated/model'
import { ApiError } from '../../api/http'
import { refreshBooks, useAccounts, useClaims, useCostCenters, useCurrencies, useCurrentCompany, useEmployees, useModules } from '../../api/hooks'
import { AmountText } from '../../layout/AmountText'
import { DateField } from '../../layout/DateField'
import { EmptyState } from '../../layout/EmptyState'
import { errorMessage } from '../../layout/errors'
import { ExportControls } from '../../layout/ExportControls'
import { ListPage } from '../../layout/ListPage'
import { useShortcuts } from '../../layout/useShortcuts'
import { useSettings } from '../../settings/SettingsContext'
import { formatDate, toIsoDate } from '../../utils/format'
import { AccountSelect } from '../accounting/AccountSelect'
import { CostCenterSelect, itemName } from '../accounting/LookupSelects'
import { exportAndShow } from '../reports/exportReport'

const statusColors: Record<ClaimStatus, string | undefined> = { Draft: undefined, Submitted: 'gold', Approved: 'blue', Rejected: 'red', Paid: 'green' }

const problemText = (error: unknown, t: (key: string | string[], options?: Record<string, unknown>) => string): string => {
  const known = error instanceof ApiError ? error.issues[0] : undefined
  return known ? t([`claims.issues.${known.code}`, `voucher.issues.${known.code}`], { defaultValue: known.code }) : errorMessage(error, t as never)
}

/** Expense claims (brief section 10.4): an employee writes down what they spent, someone approves it, and approving it posts it. */
export function ClaimsPage() {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const { message, modal } = App.useApp()
  const queryClient = useQueryClient()
  const query = useClaims()
  const employees = useEmployees().data ?? []
  const company = useCurrentCompany()
  const currencies = useCurrencies()
  const minorUnits = currencies.data?.find((c) => c.code === company.data?.baseCurrencyCode)?.minorUnits ?? 2
  const claims = useMemo(() => query.data ?? [], [query.data])
  const [form, setForm] = useState<{ open: boolean; editing?: ClaimDto }>({ open: false })
  const [rejecting, setRejecting] = useState<ClaimDto>()
  const [paying, setPaying] = useState<ClaimDto>()

  const act = useMutation({
    mutationFn: async ({ claim, action }: { claim: ClaimDto; action: 'submit' | 'approve' | 'unapprove' | 'unpay' | 'delete' }) => {
      if (action === 'submit') await submitClaim(claim.id)
      else if (action === 'approve') await approveClaim(claim.id)
      else if (action === 'unapprove') await unapproveClaim(claim.id)
      else if (action === 'unpay') await unpayClaim(claim.id)
      else await deleteClaim(claim.id)
    },
    onSuccess: () => refreshBooks(queryClient),
    onError: (error) => void message.error(problemText(error, t)),
  })

  useShortcuts({ 'ctrl+n': () => setForm({ open: true }) })

  const employeeName = (id: string) => {
    const e = employees.find((x) => x.id === id)
    return e ? itemName(e, settings.language) : ''
  }

  const columns: ColumnsType<ClaimDto> = [
    { title: t('trade.number'), dataIndex: 'number', width: 140, render: (n: string) => <span dir="ltr">{n}</span> },
    { title: t('voucher.date'), key: 'date', width: 120, render: (_: unknown, c) => formatDate(c.date, settings.digits, settings.hijri) },
    { title: t('employees.employee'), key: 'employee', render: (_: unknown, c) => employeeName(c.employeeId) },
    { title: t('claims.total'), key: 'total', width: 130, align: 'end', render: (_: unknown, c) => <AmountText value={c.total} minorUnits={minorUnits} /> },
    {
      title: t('parties.status'),
      key: 'status',
      width: 170,
      render: (_: unknown, c) => (
        <span>
          <Tag color={statusColors[c.status]}>{t(`claims.status.${c.status}`)}</Tag>
          {c.status === 'Rejected' && c.rejectionReason && <span className="muted">{c.rejectionReason}</span>}
        </span>
      ),
    },
    {
      title: <span className="visually-hidden">{t('voucher.rowActions')}</span>,
      key: 'actions',
      width: 380,
      render: (_: unknown, c) => (
        <Space size={0} wrap>
          {(c.status === 'Draft' || c.status === 'Rejected') && (
            <Button type="link" onClick={() => setForm({ open: true, editing: c })} aria-label={`${t('parties.edit')} ${c.number}`}>
              {t('parties.edit')}
            </Button>
          )}
          {c.status === 'Draft' && (
            <Button type="link" onClick={() => act.mutate({ claim: c, action: 'submit' })} aria-label={`${t('claims.submit')} ${c.number}`}>
              {t('claims.submit')}
            </Button>
          )}
          {c.status === 'Submitted' && (
            <>
              <Button type="link" onClick={() => act.mutate({ claim: c, action: 'approve' })} aria-label={`${t('claims.approve')} ${c.number}`}>
                {t('claims.approve')}
              </Button>
              <Button type="link" onClick={() => setRejecting(c)} aria-label={`${t('claims.reject')} ${c.number}`}>
                {t('claims.reject')}
              </Button>
            </>
          )}
          {c.status === 'Approved' && (
            <>
              <Button type="link" onClick={() => setPaying(c)} aria-label={`${t('claims.pay')} ${c.number}`}>
                {t('claims.pay')}
              </Button>
              <Button type="link" onClick={() => act.mutate({ claim: c, action: 'unapprove' })} aria-label={`${t('claims.unapprove')} ${c.number}`}>
                {t('claims.unapprove')}
              </Button>
            </>
          )}
          {c.status === 'Paid' && (
            <Button type="link" onClick={() => act.mutate({ claim: c, action: 'unpay' })} aria-label={`${t('claims.unpay')} ${c.number}`}>
              {t('claims.unpay')}
            </Button>
          )}
          {(c.status === 'Draft' || c.status === 'Rejected') && (
            <Button
              type="link"
              danger
              aria-label={`${t('parties.delete')} ${c.number}`}
              onClick={() =>
                modal.confirm({
                  title: t('claims.deleteTitle', { number: c.number }),
                  okText: t('parties.delete'),
                  okButtonProps: { danger: true },
                  cancelText: t('common.cancel'),
                  onOk: () => act.mutateAsync({ claim: c, action: 'delete' }).catch(() => undefined),
                })
              }
            >
              {t('parties.delete')}
            </Button>
          )}
        </Space>
      ),
    },
  ]

  return (
    <ListPage
      title={t('claims.title')}
      help="claims"
      newLabel={t('claims.new')}
      onNew={() => setForm({ open: true })}
      actions={<ExportControls run={(format, layout) => exportAndShow('expense-claims', {}, format, layout)} />}
    >
      {query.isSuccess && claims.length === 0 ? (
        <EmptyState title={t('claims.emptyTitle')} body={t('claims.emptyBody')} />
      ) : (
        <Table<ClaimDto> columns={columns} dataSource={claims} rowKey="id" loading={query.isPending} pagination={false} size="middle" bordered />
      )}
      {form.open && <ClaimForm editing={form.editing} onClose={() => setForm({ open: false })} />}
      {rejecting && <RejectModal claim={rejecting} onClose={() => setRejecting(undefined)} />}
      {paying && <PayModal claim={paying} onClose={() => setPaying(undefined)} />}
    </ListPage>
  )
}

function RejectModal({ claim, onClose }: { claim: ClaimDto; onClose: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [reason, setReason] = useState('')
  const [problem, setProblem] = useState<string>()

  const reject = useMutation({
    mutationFn: () => rejectClaim(claim.id, { reason: reason.trim() || null }),
    onSuccess: async () => {
      await refreshBooks(queryClient)
      onClose()
    },
    onError: (error) => setProblem(problemText(error, t)),
  })

  return (
    <Modal open title={t('claims.rejectTitle', { number: claim.number })} onCancel={onClose} onOk={() => reject.mutate()} okText={t('claims.reject')} cancelText={t('common.cancel')} confirmLoading={reject.isPending} maskClosable={false}>
      {problem && <Alert type="error" showIcon message={problem} className="form-alert" />}
      <div className="field field-wide">
        <label htmlFor="reject-reason">{t('claims.reason')}</label>
        <Input id="reject-reason" value={reason} onChange={(e) => setReason(e.target.value)} />
      </div>
    </Modal>
  )
}

function PayModal({ claim, onClose }: { claim: ClaimDto; onClose: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const accounts = useAccounts().data ?? []
  const [date, setDate] = useState<string | undefined>(toIsoDate(new Date()))
  const [cashAccountId, setCashAccountId] = useState<string>()
  const [problem, setProblem] = useState<string>()

  const pay = useMutation({
    mutationFn: () => payClaim(claim.id, { date: date!, cashAccountId: cashAccountId! }),
    onSuccess: async () => {
      await refreshBooks(queryClient)
      onClose()
    },
    onError: (error) => setProblem(problemText(error, t)),
  })

  return (
    <Modal open title={t('claims.payTitle', { number: claim.number })} onCancel={onClose} onOk={() => pay.mutate()} okText={t('claims.pay')} okButtonProps={{ disabled: !date || !cashAccountId }} cancelText={t('common.cancel')} confirmLoading={pay.isPending} maskClosable={false}>
      {problem && <Alert type="error" showIcon message={problem} className="form-alert" />}
      <div className="field">
        <label htmlFor="claim-pay-date">{t('voucher.date')}</label>
        <DateField id="claim-pay-date" value={date} onChange={setDate} ariaLabel={t('voucher.date')} />
      </div>
      <div className="field field-wide">
        <label htmlFor="claim-pay-account">{t('payroll.paidFrom')}</label>
        <AccountSelect id="claim-pay-account" value={cashAccountId} onChange={setCashAccountId} accounts={accounts} cashOnly ariaLabel={t('payroll.paidFrom')} />
      </div>
    </Modal>
  )
}

interface Values {
  employeeId?: string
  date: string
  memo?: string
  lines: { date: string; description?: string; accountId?: string; amount: number | null; costCenterId?: string }[]
}

function ClaimForm({ editing, onClose }: { editing?: ClaimDto; onClose: () => void }) {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const queryClient = useQueryClient()
  const [form] = Form.useForm<Values>()
  const employees = useEmployees().data ?? []
  const accounts = useAccounts().data ?? []
  const costCenters = useCostCenters().data ?? []
  const modules = useModules()
  const [problems, setProblems] = useState<string[]>([])

  useEffect(() => {
    const today = toIsoDate(new Date())
    form.setFieldsValue(
      editing
        ? {
            employeeId: editing.employeeId,
            date: editing.date,
            memo: editing.memo ?? '',
            lines: editing.lines.map((l) => ({ date: l.date, description: l.description ?? '', accountId: l.accountId, amount: l.amount, costCenterId: l.costCenterId ?? undefined })),
          }
        : { date: today, memo: '', lines: [{ date: today, amount: null }] },
    )
  }, [editing, form])

  const save = useMutation({
    mutationFn: (values: Values) => {
      const input: ClaimInput = {
        employeeId: values.employeeId!,
        date: values.date,
        memo: values.memo?.trim() || null,
        lines: (values.lines ?? []).filter((l) => l.accountId || (l.amount ?? 0) > 0).map((l) => ({ date: l.date ?? values.date, description: l.description?.trim() || null, accountId: l.accountId!, amount: l.amount ?? 0, costCenterId: l.costCenterId ?? null })),
      }
      return saveClaim({ id: editing?.id ?? null, input })
    },
    onSuccess: async () => {
      await refreshBooks(queryClient)
      onClose()
    },
    onError: (error) =>
      setProblems(
        error instanceof ApiError && error.issues.length > 0
          ? error.issues.map((issue) => {
              const row = /^lines\[(\d+)\]/.exec(issue.field)
              const text = t([`claims.issues.${issue.code}`], { defaultValue: issue.code })
              return row ? `${t('inventory.line')} ${Number(row[1]) + 1}: ${text}` : text
            })
          : [errorMessage(error, t)],
      ),
  })

  return (
    <Modal open title={editing ? t('claims.edit') : t('claims.new')} onCancel={onClose} onOk={() => form.submit()} okText={t('common.save')} cancelText={t('common.cancel')} confirmLoading={save.isPending} maskClosable={false} width={860}>
      <Form form={form} layout="vertical" onFinish={(values) => save.mutate(values)} requiredMark={false}>
        {problems.length > 0 && <Alert type="error" showIcon className="form-alert" message={problems.length === 1 ? problems[0] : <ul className="problem-list">{problems.map((p) => <li key={p}>{p}</li>)}</ul>} />}
        <p className="muted">{t('claims.intro')}</p>
        <div className="form-row">
          <Form.Item name="employeeId" label={t('employees.employee')} className="grow">
            <Select showSearch optionFilterProp="label" aria-label={t('employees.employee')} options={employees.filter((e) => e.isActive).map((e) => ({ value: e.id, label: `${e.code} — ${itemName(e, settings.language)}` }))} />
          </Form.Item>
          <Form.Item name="date" label={t('voucher.date')}>
            <DateField value={undefined} onChange={() => undefined} ariaLabel={t('voucher.date')} />
          </Form.Item>
        </div>
        <Form.Item name="memo" label={t('inventory.notes')}>
          <Input />
        </Form.Item>
        <Form.List name="lines">
          {(fields, { add, remove }) => (
            <div className="price-lines">
              {fields.map((field, index) => (
                <div className="form-row" key={field.key}>
                  <Form.Item name={[field.name, 'date']} label={index === 0 ? t('voucher.date') : undefined}>
                    <DateField value={undefined} onChange={() => undefined} ariaLabel={`${t('voucher.date')} ${index + 1}`} />
                  </Form.Item>
                  <Form.Item name={[field.name, 'description']} label={index === 0 ? t('claims.description') : undefined} className="grow">
                    <Input aria-label={`${t('claims.description')} ${index + 1}`} />
                  </Form.Item>
                  <Form.Item name={[field.name, 'accountId']} label={index === 0 ? t('claims.expenseAccount') : undefined} className="grow">
                    <AccountSelect value={undefined} onChange={() => undefined} accounts={accounts.filter((a) => a.type === 'Expense' || a.type === 'Asset')} ariaLabel={`${t('claims.expenseAccount')} ${index + 1}`} />
                  </Form.Item>
                  <Form.Item name={[field.name, 'amount']} label={index === 0 ? t('payroll.amount') : undefined}>
                    <InputNumber min={0} precision={3} controls={false} className="amount-input" aria-label={`${t('payroll.amount')} ${index + 1}`} />
                  </Form.Item>
                  {modules.has('cost-centers') && (
                    <Form.Item name={[field.name, 'costCenterId']} label={index === 0 ? t('reports.costCenter') : undefined}>
                      <CostCenterSelect value={undefined} onChange={() => undefined} costCenters={costCenters} ariaLabel={`${t('reports.costCenter')} ${index + 1}`} />
                    </Form.Item>
                  )}
                  <Button type="link" danger onClick={() => remove(field.name)} aria-label={`${t('voucher.removeRow')} ${index + 1}`}>
                    {t('voucher.removeRow')}
                  </Button>
                </div>
              ))}
              <Button onClick={() => add({ date: form.getFieldValue('date'), amount: null })}>{t('inventory.addLine')}</Button>
            </div>
          )}
        </Form.List>
      </Form>
    </Modal>
  )
}
