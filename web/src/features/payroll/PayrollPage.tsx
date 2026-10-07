import { Alert, App, Button, Modal, Segmented, Space, Table, Tag } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { accrueEndOfService, createPayrollRun, deletePayrollRun, undoEndOfService } from '../../api/generated/baba'
import type { EndOfServiceLine, PayrollRunSummary } from '../../api/generated/model'
import { ApiError } from '../../api/http'
import { refreshBooks, useCurrencies, useCurrentCompany, useEmployees, useEndOfService, usePayrollRuns, usePayrollSettings } from '../../api/hooks'
import { AmountText } from '../../layout/AmountText'
import { DateField } from '../../layout/DateField'
import { EmptyState } from '../../layout/EmptyState'
import { errorMessage } from '../../layout/errors'
import { ExportControls } from '../../layout/ExportControls'
import { ListPage } from '../../layout/ListPage'
import { QuantityText } from '../../layout/QuantityText'
import { useShortcuts } from '../../layout/useShortcuts'
import { useSettings } from '../../settings/SettingsContext'
import { toIsoDate } from '../../utils/format'
import { itemName } from '../accounting/LookupSelects'
import { exportAndShow } from '../reports/exportReport'
import { PayrollSettingsModal } from './PayrollSettingsModal'

type Tab = 'runs' | 'end-of-service'

/** The first day of the month before the current one, as a date to type: the last month that has ended. */
const lastMonthEnd = () => {
  const now = new Date()
  return toIsoDate(new Date(now.getFullYear(), now.getMonth(), 0))
}

const problemText = (error: unknown, t: (key: string | string[], options?: Record<string, unknown>) => string): string => {
  const known = error instanceof ApiError ? error.issues[0] : undefined
  return known ? t([`payroll.issues.${known.code}`, `voucher.issues.${known.code}`], { defaultValue: known.code }) : errorMessage(error, t as never)
}

/** Monthly payroll (brief section 10.4): every month's run, the settings, and the end-of-service provision. */
export function PayrollPage() {
  const { t } = useTranslation()
  const [params, setParams] = useSearchParams()
  const tab: Tab = params.get('tab') === 'end-of-service' ? 'end-of-service' : 'runs'
  const [creating, setCreating] = useState(false)
  const [settingsOpen, setSettingsOpen] = useState(false)

  const tabs = (
    <Segmented
      value={tab}
      onChange={(value) => setParams(value === 'runs' ? {} : { tab: value as string })}
      options={[
        { value: 'runs', label: t('payroll.tabs.runs') },
        { value: 'end-of-service', label: t('payroll.tabs.endOfService') },
      ]}
      aria-label={t('payroll.tabs.label')}
    />
  )

  useShortcuts({ 'ctrl+n': () => tab === 'runs' && setCreating(true) })

  return (
    <ListPage
      title={t('payroll.title')}
      help="payroll"
      newLabel={t('payroll.newMonth')}
      onNew={tab === 'runs' ? () => setCreating(true) : undefined}
      actions={
        <Space wrap>
          <ExportControls
            run={(format, layout) => {
              const year = new Date().getFullYear()
              return exportAndShow('payroll-summary', { From: `${year}-01-01`, To: `${year}-12-31` }, format, layout)
            }}
          />
          <Button onClick={() => setSettingsOpen(true)}>{t('payroll.settings')}</Button>
        </Space>
      }
      filters={tabs}
    >
      {tab === 'runs' ? <RunsTable /> : <EndOfServiceTab />}
      {creating && <NewMonthModal onClose={() => setCreating(false)} />}
      {settingsOpen && <PayrollSettingsModal onClose={() => setSettingsOpen(false)} />}
    </ListPage>
  )
}

function RunsTable() {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const { message, modal } = App.useApp()
  const queryClient = useQueryClient()
  const query = usePayrollRuns()
  const payroll = usePayrollSettings()
  const company = useCurrentCompany()
  const currencies = useCurrencies()
  const minorUnits = currencies.data?.find((c) => c.code === company.data?.baseCurrencyCode)?.minorUnits ?? 2
  const runs = useMemo(() => query.data ?? [], [query.data])

  const remove = useMutation({
    mutationFn: (r: PayrollRunSummary) => deletePayrollRun(r.id),
    onSuccess: () => refreshBooks(queryClient),
    onError: (error) => void message.error(problemText(error, t)),
  })

  const monthLabel = (month: string) => new Date(`${month}T00:00:00`).toLocaleDateString(settings.language === 'ar' ? 'ar' : 'en', { month: 'long', year: 'numeric' })
  const columns: ColumnsType<PayrollRunSummary> = [
    { title: t('payroll.month'), key: 'month', width: 170, render: (_: unknown, r) => <Link to={`/payroll/${r.id}`}>{monthLabel(r.month)}</Link> },
    {
      title: t('parties.status'),
      key: 'status',
      width: 120,
      render: (_: unknown, r) => (r.status === 'Draft' ? <Tag>{t('payroll.status.Draft')}</Tag> : r.paidDate ? <Tag color="green">{t('payroll.status.Paid')}</Tag> : <Tag color="blue">{t('payroll.status.Posted')}</Tag>),
    },
    { title: t('payroll.employees'), dataIndex: 'employees', width: 100, align: 'end' },
    { title: t('payroll.earnings'), key: 'earnings', width: 130, align: 'end', render: (_: unknown, r) => <AmountText value={r.earnings} minorUnits={minorUnits} /> },
    { title: t('payroll.deductions'), key: 'deductions', width: 130, align: 'end', render: (_: unknown, r) => <AmountText value={r.deductions + r.employeeInsurance} minorUnits={minorUnits} /> },
    { title: t('payroll.net'), key: 'net', width: 130, align: 'end', render: (_: unknown, r) => <AmountText value={r.net} minorUnits={minorUnits} /> },
    {
      title: <span className="visually-hidden">{t('voucher.rowActions')}</span>,
      key: 'actions',
      width: 180,
      render: (_: unknown, r) => (
        <Space size={0}>
          <Link to={`/payroll/${r.id}`}>
            <Button type="link" aria-label={`${t('common.open')} ${monthLabel(r.month)}`}>
              {t('common.open')}
            </Button>
          </Link>
          {r.status === 'Draft' && (
            <Button
              type="link"
              danger
              aria-label={`${t('parties.delete')} ${monthLabel(r.month)}`}
              onClick={() =>
                modal.confirm({
                  title: t('payroll.deleteTitle', { month: monthLabel(r.month) }),
                  okText: t('parties.delete'),
                  okButtonProps: { danger: true },
                  cancelText: t('common.cancel'),
                  onOk: () => remove.mutateAsync(r).catch(() => undefined),
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
    <>
      {payroll.data && !payroll.data.startMonth && runs.length === 0 && <Alert type="info" showIcon className="form-alert" message={t('payroll.startHint')} />}
      {query.isSuccess && runs.length === 0 ? (
        <EmptyState title={t('payroll.emptyTitle')} body={t('payroll.emptyBody')} />
      ) : (
        <Table<PayrollRunSummary> columns={columns} dataSource={runs} rowKey="id" loading={query.isPending} pagination={false} size="middle" bordered />
      )}
    </>
  )
}

function NewMonthModal({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const [month, setMonth] = useState<string | undefined>(lastMonthEnd())
  const [problem, setProblem] = useState<string>()

  const create = useMutation({
    mutationFn: () => createPayrollRun({ month: month! }),
    onSuccess: async (response) => {
      await refreshBooks(queryClient)
      navigate(`/payroll/${response.data.summary.id}`)
    },
    onError: (error) => setProblem(problemText(error, t)),
  })

  return (
    <Modal open title={t('payroll.newMonth')} onCancel={onClose} onOk={() => create.mutate()} okText={t('payroll.make')} okButtonProps={{ disabled: !month }} cancelText={t('common.cancel')} confirmLoading={create.isPending} maskClosable={false}>
      <p>{t('payroll.newMonthIntro')}</p>
      {problem && <Alert type="error" showIcon message={problem} className="form-alert" />}
      <div className="field">
        <label htmlFor="payroll-month">{t('payroll.anyDayOfMonth')}</label>
        <DateField id="payroll-month" value={month} onChange={setMonth} ariaLabel={t('payroll.anyDayOfMonth')} />
      </div>
    </Modal>
  )
}

function EndOfServiceTab() {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const asOf = useMemo(() => lastMonthEnd(), [])
  const position = useEndOfService(asOf)
  const employees = useEmployees().data ?? []
  const company = useCurrentCompany()
  const currencies = useCurrencies()
  const minorUnits = currencies.data?.find((c) => c.code === company.data?.baseCurrencyCode)?.minorUnits ?? 2
  const [month, setMonth] = useState<string | undefined>(asOf)
  const [problem, setProblem] = useState<string>()

  const accrue = useMutation({
    mutationFn: () => accrueEndOfService({ month: month! }),
    onSuccess: async (response) => {
      await refreshBooks(queryClient)
      setProblem(undefined)
      void message.success(response.data.voucherId ? t('payroll.provisionPosted') : t('payroll.provisionAlreadyRight'))
    },
    onError: (error) => setProblem(problemText(error, t)),
  })
  const undo = useMutation({
    mutationFn: () => undoEndOfService(),
    onSuccess: async () => {
      await refreshBooks(queryClient)
      setProblem(undefined)
    },
    onError: (error) => setProblem(problemText(error, t)),
  })

  const data = position.data
  if (data && !data.applicable) return <Alert type="info" showIcon message={t('payroll.noEndOfService')} />

  const name = (id: string) => {
    const e = employees.find((x) => x.id === id)
    return e ? `${e.code} — ${itemName(e, settings.language)}` : ''
  }
  const columns: ColumnsType<EndOfServiceLine> = [
    { title: t('employees.employee'), key: 'employee', render: (_: unknown, l) => name(l.employeeId) },
    { title: t('payroll.years'), key: 'years', width: 120, align: 'end', render: (_: unknown, l) => <QuantityText value={Math.round(l.yearsOfService * 100) / 100} /> },
    { title: t('payroll.wage'), key: 'wage', width: 140, align: 'end', render: (_: unknown, l) => <AmountText value={l.wage} minorUnits={minorUnits} /> },
    { title: t('payroll.owed'), key: 'gratuity', width: 140, align: 'end', render: (_: unknown, l) => <AmountText value={l.gratuity} minorUnits={minorUnits} /> },
  ]

  return (
    <>
      <p className="muted">{t('payroll.endOfServiceIntro')}</p>
      {problem && <Alert type="error" showIcon className="form-alert" message={problem} />}
      <Table<EndOfServiceLine>
        columns={columns}
        dataSource={[...(data?.lines ?? [])]}
        rowKey="employeeId"
        loading={position.isPending}
        pagination={false}
        size="middle"
        bordered
        summary={() =>
          data ? (
            <>
              <Table.Summary.Row>
                <Table.Summary.Cell index={0} colSpan={3}>
                  <strong>{t('payroll.totalOwed')}</strong>
                </Table.Summary.Cell>
                <Table.Summary.Cell index={3} align="end">
                  <AmountText value={data.required} minorUnits={minorUnits} />
                </Table.Summary.Cell>
              </Table.Summary.Row>
              <Table.Summary.Row>
                <Table.Summary.Cell index={0} colSpan={3}>
                  {t('payroll.setAside')}
                </Table.Summary.Cell>
                <Table.Summary.Cell index={3} align="end">
                  <AmountText value={data.provision} minorUnits={minorUnits} />
                </Table.Summary.Cell>
              </Table.Summary.Row>
            </>
          ) : null
        }
      />
      <div className="filter-bar">
        <div className="field">
          <label htmlFor="eos-month">{t('payroll.provisionFor')}</label>
          <DateField id="eos-month" value={month} onChange={setMonth} ariaLabel={t('payroll.provisionFor')} />
        </div>
        <Button type="primary" onClick={() => accrue.mutate()} loading={accrue.isPending} disabled={!month}>
          {t('payroll.postProvision')}
        </Button>
        <Button onClick={() => undo.mutate()} loading={undo.isPending}>
          {t('payroll.undoProvision')}
        </Button>
      </div>
    </>
  )
}
