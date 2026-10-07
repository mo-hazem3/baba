import { Alert, App, Button, Card, Input, InputNumber, Modal, Select, Space, Statistic, Switch, Table, Tag } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Navigate, useNavigate, useParams } from 'react-router'
import {
  deletePayrollRun,
  payPayrollRun,
  postPayrollRun,
  printPayslip,
  printPayslips,
  refreshPayrollRun,
  removePayslip,
  savePayslip,
  unpayPayrollRun,
  unpostPayrollRun,
} from '../../api/generated/baba'
import type { PayslipDto, PayslipItemInput, SalaryComponentKind } from '../../api/generated/model'
import { ApiError, asBlob } from '../../api/http'
import { refreshBooks, useAccounts, useCurrencies, useCurrentCompany, useEmployees, usePayrollRun, usePrintSettings } from '../../api/hooks'
import { AmountText } from '../../layout/AmountText'
import { DateField } from '../../layout/DateField'
import { errorMessage } from '../../layout/errors'
import { PageHeader } from '../../layout/PageHeader'
import { useSettings } from '../../settings/SettingsContext'
import { openPdf } from '../../utils/download'
import { toIsoDate } from '../../utils/format'
import { AccountSelect } from '../accounting/AccountSelect'
import { itemName } from '../accounting/LookupSelects'

const problemText = (error: unknown, t: (key: string | string[], options?: Record<string, unknown>) => string): string => {
  const known = error instanceof ApiError ? error.issues[0] : undefined
  return known ? t([`payroll.issues.${known.code}`, `voucher.issues.${known.code}`], { defaultValue: known.code }) : errorMessage(error, t as never)
}

/** One month's payroll: its payslips, and the steps of the month (post, pay, take back). */
export function PayrollRunPage() {
  const { id } = useParams()
  const { t } = useTranslation()
  const { settings } = useSettings()
  const { message, modal } = App.useApp()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const query = usePayrollRun(id)
  const employees = useEmployees().data ?? []
  const printSettings = usePrintSettings()
  const company = useCurrentCompany()
  const currencies = useCurrencies()
  const minorUnits = currencies.data?.find((c) => c.code === company.data?.baseCurrencyCode)?.minorUnits ?? 2
  const [editing, setEditing] = useState<PayslipDto>()
  const [paying, setPaying] = useState(false)

  const act = useMutation({
    mutationFn: (action: 'post' | 'unpost' | 'unpay' | 'refresh') =>
      action === 'post' ? postPayrollRun(id!) : action === 'unpost' ? unpostPayrollRun(id!) : action === 'unpay' ? unpayPayrollRun(id!) : refreshPayrollRun(id!),
    onSuccess: () => refreshBooks(queryClient),
    onError: (error) => void message.error(problemText(error, t)),
  })
  const remove = useMutation({
    mutationFn: () => deletePayrollRun(id!),
    onSuccess: async () => {
      await refreshBooks(queryClient)
      navigate('/payroll')
    },
    onError: (error) => void message.error(problemText(error, t)),
  })
  const removeSlip = useMutation({
    mutationFn: (slip: PayslipDto) => removePayslip(id!, slip.id),
    onSuccess: () => refreshBooks(queryClient),
    onError: (error) => void message.error(problemText(error, t)),
  })
  const print = useMutation({
    mutationFn: async (slip?: PayslipDto) => {
      const params = { layout: printSettings.data?.defaultLayout }
      const response = slip ? await printPayslip(id!, slip.id, params) : await printPayslips(id!, params)
      if (response.status !== 200) throw new ApiError(response.status, 'NotImplemented', 'This host cannot make PDFs.')
      openPdf(asBlob(response.data))
    },
    onError: (error) => void message.error(error instanceof ApiError && error.status === 501 ? t('export.pdfUnavailable') : errorMessage(error, t)),
  })

  if (!id) return <Navigate to="/payroll" replace />
  if (query.isError || (query.isSuccess && !query.data)) return <Navigate to="/payroll" replace />
  const run = query.data
  if (!run) return null

  const summary = run.summary
  const month = new Date(`${summary.month}T00:00:00`).toLocaleDateString(settings.language === 'ar' ? 'ar' : 'en', { month: 'long', year: 'numeric' })
  const draft = summary.status === 'Draft'
  const paid = summary.paidDate !== null
  const nameOf = (employeeId: string) => {
    const e = employees.find((x) => x.id === employeeId)
    return e ? `${e.code} — ${itemName(e, settings.language)}` : ''
  }

  const columns: ColumnsType<PayslipDto> = [
    { title: t('employees.employee'), key: 'employee', render: (_: unknown, s) => nameOf(s.employeeId) },
    { title: t('payroll.earnings'), key: 'earnings', width: 130, align: 'end', render: (_: unknown, s) => <AmountText value={s.earnings} minorUnits={minorUnits} /> },
    { title: t('payroll.deductions'), key: 'deductions', width: 130, align: 'end', render: (_: unknown, s) => <AmountText value={s.deductions} minorUnits={minorUnits} /> },
    { title: t('payroll.employeeInsurance'), key: 'insurance', width: 130, align: 'end', render: (_: unknown, s) => <AmountText value={s.employeeInsurance} minorUnits={minorUnits} blankZero /> },
    { title: t('payroll.net'), key: 'net', width: 130, align: 'end', render: (_: unknown, s) => <AmountText value={s.net} minorUnits={minorUnits} /> },
    {
      title: <span className="visually-hidden">{t('voucher.rowActions')}</span>,
      key: 'actions',
      width: 290,
      render: (_: unknown, s) => (
        <Space size={0} wrap>
          <Button type="link" disabled={paid} onClick={() => setEditing(s)} aria-label={`${t('parties.edit')} ${nameOf(s.employeeId)}`}>
            {t('parties.edit')}
          </Button>
          <Button type="link" onClick={() => print.mutate(s)} aria-label={`${t('payroll.payslip')} ${nameOf(s.employeeId)}`}>
            {t('payroll.payslip')}
          </Button>
          <Button type="link" danger disabled={paid} onClick={() => removeSlip.mutate(s)} aria-label={`${t('payroll.leaveOut')} ${nameOf(s.employeeId)}`}>
            {t('payroll.leaveOut')}
          </Button>
        </Space>
      ),
    },
  ]

  return (
    <div>
      <PageHeader
        title={`${t('payroll.title')} — ${month}`}
        help="payroll"
        crumbs={[{ label: t('breadcrumb.home'), to: '/' }, { label: t('payroll.title'), to: '/payroll' }, { label: month }]}
        action={
          <Space wrap>
            {draft ? <Tag>{t('payroll.status.Draft')}</Tag> : paid ? <Tag color="green">{t('payroll.status.Paid')}</Tag> : <Tag color="blue">{t('payroll.status.Posted')}</Tag>}
            <Button onClick={() => print.mutate(undefined)} loading={print.isPending}>
              {t('payroll.printAll')}
            </Button>
            {draft && <Button onClick={() => act.mutate('refresh')}>{t('payroll.refresh')}</Button>}
            {draft && (
              <Button type="primary" onClick={() => act.mutate('post')} loading={act.isPending}>
                {t('payroll.post')}
              </Button>
            )}
            {!draft && !paid && <Button onClick={() => setPaying(true)} type="primary">{t('payroll.paySalaries')}</Button>}
            {!draft && !paid && <Button onClick={() => act.mutate('unpost')}>{t('payroll.unpost')}</Button>}
            {paid && <Button onClick={() => act.mutate('unpay')}>{t('payroll.unpay')}</Button>}
            {!paid && (
              <Button
                danger
                onClick={() =>
                  modal.confirm({
                    title: t('payroll.deleteTitle', { month }),
                    okText: t('parties.delete'),
                    okButtonProps: { danger: true },
                    cancelText: t('common.cancel'),
                    onOk: () => remove.mutateAsync().catch(() => undefined),
                  })
                }
              >
                {t('parties.delete')}
              </Button>
            )}
          </Space>
        }
      />
      {!draft && !paid && <Alert type="info" showIcon className="form-alert" message={t('payroll.postedNote')} />}
      <div className="stat-row">
        <Card><Statistic title={t('payroll.earnings')} valueRender={() => <AmountText value={summary.earnings} minorUnits={minorUnits} />} /></Card>
        <Card><Statistic title={t('payroll.deductions')} valueRender={() => <AmountText value={summary.deductions + summary.employeeInsurance} minorUnits={minorUnits} />} /></Card>
        <Card><Statistic title={t('payroll.employerInsurance')} valueRender={() => <AmountText value={summary.employerInsurance} minorUnits={minorUnits} />} /></Card>
        <Card><Statistic title={t('payroll.net')} valueRender={() => <AmountText value={summary.net} minorUnits={minorUnits} />} /></Card>
      </div>
      <Table<PayslipDto> columns={columns} dataSource={run.payslips} rowKey="id" pagination={false} size="middle" bordered />
      {editing && <PayslipModal runId={id} payslip={editing} name={nameOf(editing.employeeId)} onClose={() => setEditing(undefined)} />}
      {paying && <PayModal runId={id} onClose={() => setPaying(false)} />}
    </div>
  )
}

interface ItemRow extends PayslipItemInput {
  key: string
}

function PayslipModal({ runId, payslip, name, onClose }: { runId: string; payslip: PayslipDto; name: string; onClose: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const accounts = useAccounts().data ?? []
  // The window only exists while one payslip is being changed, so the lines start from that payslip.
  const [rows, setRows] = useState<ItemRow[]>(() =>
    payslip.items.map((i, index) => ({ key: `r${index}`, kind: i.kind, label: i.label, labelAr: i.labelAr, amount: i.amount, accountId: i.accountId, isInsurable: i.isInsurable, inEndOfService: i.inEndOfService, componentId: i.componentId, isBasic: i.isBasic })),
  )
  const [problem, setProblem] = useState<string>()

  const update = (key: string, change: Partial<ItemRow>) => setRows((current) => current.map((r) => (r.key === key ? { ...r, ...change } : r)))

  const save = useMutation({
    mutationFn: () => savePayslip(runId, payslip.id, { items: rows.filter((r) => r.label.trim() || r.amount > 0).map((r): PayslipItemInput => ({ kind: r.kind, label: r.label, labelAr: r.labelAr, amount: r.amount, accountId: r.accountId, isInsurable: r.isInsurable, inEndOfService: r.inEndOfService, componentId: r.componentId, isBasic: r.isBasic })) }),
    onSuccess: async () => {
      await refreshBooks(queryClient)
      onClose()
    },
    onError: (error) => setProblem(error instanceof ApiError && error.issues.length > 0 ? error.issues.map((i) => t([`payroll.issues.${i.code}`], { defaultValue: i.code })).join(' ') : errorMessage(error, t)),
  })

  return (
    <Modal open title={t('payroll.editPayslip', { name })} onCancel={onClose} onOk={() => save.mutate()} okText={t('common.save')} cancelText={t('common.cancel')} confirmLoading={save.isPending} maskClosable={false} width={860}>
      <p className="muted">{t('payroll.editPayslipHelp')}</p>
      {problem && <Alert type="error" showIcon message={problem} className="form-alert" />}
      {rows.map((row, index) => (
        <div className="form-row" key={row.key}>
          <div className="field grow">
            {index === 0 && <label>{t('payroll.itemLabel')}</label>}
            <Input value={row.label} disabled={row.isBasic} onChange={(e) => update(row.key, { label: e.target.value, labelAr: e.target.value })} aria-label={`${t('payroll.itemLabel')} ${index + 1}`} />
          </div>
          <div className="field">
            {index === 0 && <label>{t('assets.type')}</label>}
            <Select
              value={row.kind}
              disabled={row.isBasic}
              onChange={(kind: SalaryComponentKind) => update(row.key, { kind })}
              options={(['Earning', 'Deduction'] as const).map((k) => ({ value: k, label: t(`employees.componentKinds.${k}`) }))}
              aria-label={`${t('assets.type')} ${index + 1}`}
            />
          </div>
          <div className="field">
            {index === 0 && <label>{t('payroll.amount')}</label>}
            <InputNumber value={row.amount} min={0} precision={3} controls={false} className="amount-input" onChange={(v) => update(row.key, { amount: v ?? 0 })} aria-label={`${t('payroll.amount')} ${index + 1}`} />
          </div>
          <div className="field">
            {index === 0 && <label>{t('payroll.insurableShort')}</label>}
            <Switch checked={row.isInsurable} disabled={row.kind === 'Deduction'} onChange={(checked) => update(row.key, { isInsurable: checked })} aria-label={`${t('payroll.insurableShort')} ${index + 1}`} />
          </div>
          {!row.isBasic && (
            <>
              <div className="field field-wide">
                {index === 0 && <label>{t('payroll.account')}</label>}
                <AccountSelect value={row.accountId ?? undefined} onChange={(accountId) => update(row.key, { accountId: accountId ?? null })} accounts={accounts} ariaLabel={`${t('payroll.account')} ${index + 1}`} />
              </div>
              <Button type="link" danger onClick={() => setRows((current) => current.filter((r) => r.key !== row.key))} aria-label={`${t('voucher.removeRow')} ${index + 1}`}>
                {t('voucher.removeRow')}
              </Button>
            </>
          )}
        </div>
      ))}
      <Button onClick={() => setRows((current) => [...current, { key: `n${current.length}-${Date.now()}`, kind: 'Earning', label: '', labelAr: '', amount: 0, accountId: null, isInsurable: false, inEndOfService: false }])}>{t('payroll.addItem')}</Button>
    </Modal>
  )
}

function PayModal({ runId, onClose }: { runId: string; onClose: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const accounts = useAccounts().data ?? []
  const [date, setDate] = useState<string | undefined>(toIsoDate(new Date()))
  const [cashAccountId, setCashAccountId] = useState<string>()
  const [problem, setProblem] = useState<string>()

  const pay = useMutation({
    mutationFn: () => payPayrollRun(runId, { date: date!, cashAccountId: cashAccountId! }),
    onSuccess: async () => {
      await refreshBooks(queryClient)
      onClose()
    },
    onError: (error) => setProblem(problemText(error, t)),
  })

  return (
    <Modal open title={t('payroll.paySalaries')} onCancel={onClose} onOk={() => pay.mutate()} okText={t('payroll.pay')} okButtonProps={{ disabled: !date || !cashAccountId }} cancelText={t('common.cancel')} confirmLoading={pay.isPending} maskClosable={false}>
      <p>{t('payroll.payIntro')}</p>
      {problem && <Alert type="error" showIcon message={problem} className="form-alert" />}
      <div className="field">
        <label htmlFor="pay-date">{t('voucher.date')}</label>
        <DateField id="pay-date" value={date} onChange={setDate} ariaLabel={t('voucher.date')} />
      </div>
      <div className="field field-wide">
        <label htmlFor="pay-account">{t('payroll.paidFrom')}</label>
        <AccountSelect id="pay-account" value={cashAccountId} onChange={setCashAccountId} accounts={accounts} cashOnly ariaLabel={t('payroll.paidFrom')} />
      </div>
    </Modal>
  )
}
