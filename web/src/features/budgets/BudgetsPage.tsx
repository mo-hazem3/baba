import { Alert, App, Button, InputNumber, Modal, Select, Space, Table } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { copyBudget, saveBudget } from '../../api/generated/baba'
import type { AccountDto, BudgetDto } from '../../api/generated/model'
import { ApiError } from '../../api/http'
import { refreshBooks, useAccounts, useBudget, useBudgetYears, useCostCenters, useCurrencies, useCurrentCompany, useModules } from '../../api/hooks'
import { errorMessage } from '../../layout/errors'
import { ExportControls } from '../../layout/ExportControls'
import { ImportModal } from '../../layout/ImportModal'
import { PageHeader } from '../../layout/PageHeader'
import { useSettings } from '../../settings/SettingsContext'
import { AccountSelect } from '../accounting/AccountSelect'
import { CostCenterSelect, itemName } from '../accounting/LookupSelects'
import { exportAndShow } from '../reports/exportReport'

interface Row {
  accountId: string
  amounts: (number | null)[]
}

const round = (value: number) => Math.round(value * 1000) / 1000

/** The fiscal year (named by the calendar year it starts in) that today falls in. */
const currentFiscalYear = (startMonth: number) => {
  const now = new Date()
  return now.getMonth() + 1 >= startMonth ? now.getFullYear() : now.getFullYear() - 1
}

/** Budgets (brief section 10.4): a plan for each revenue and expense account, month by month, read against what happened. */
export function BudgetsPage() {
  const { t } = useTranslation()
  const company = useCurrentCompany()
  const startMonth = company.data?.fiscalYearStartMonth ?? 1
  const yearsQuery = useBudgetYears()
  const years = useMemo(() => yearsQuery.data ?? [], [yearsQuery.data])
  const costCenters = useCostCenters().data ?? []
  const modules = useModules()
  const [year, setYear] = useState<number>()
  const [costCenterId, setCostCenterId] = useState<string>()
  const [copying, setCopying] = useState(false)
  const [importing, setImporting] = useState(false)

  const fiscalYear = year ?? currentFiscalYear(startMonth)
  const query = useBudget(fiscalYear, costCenterId)
  const yearOptions = useMemo(() => {
    const current = currentFiscalYear(startMonth)
    return [...new Set([...years, current - 1, current, current + 1])].sort((a, b) => b - a)
  }, [years, startMonth])

  const importUrl = `/api/import/budget?fiscalYear=${fiscalYear}${costCenterId ? `&costCenterId=${costCenterId}` : ''}`
  const range = query.data ? `From=${query.data.start}&To=${query.data.end}` : ''

  return (
    <div>
      <PageHeader
        title={t('budgets.title')}
        help="budgets"
        crumbs={[{ label: t('breadcrumb.home'), to: '/' }, { label: t('budgets.title') }]}
        action={
          <Space wrap>
            <ExportControls run={(format, layout) => exportAndShow('budget', { FiscalYear: fiscalYear, CostCenterId: costCenterId }, format, layout)} />
            <Button onClick={() => setImporting(true)}>{t('budgets.import')}</Button>
            <Button onClick={() => setCopying(true)}>{t('budgets.copy')}</Button>
            <Link to={`/reports/budget-vs-actual?${range}${costCenterId ? `&costCenterId=${costCenterId}` : ''}`}>
              <Button>{t('budgets.versusActual')}</Button>
            </Link>
          </Space>
        }
      />
      <div className="filter-bar">
        <div className="field">
          <label htmlFor="budget-year">{t('budgets.fiscalYear')}</label>
          <Select id="budget-year" value={fiscalYear} onChange={(value) => setYear(value)} options={yearOptions.map((y) => ({ value: y, label: String(y) }))} aria-label={t('budgets.fiscalYear')} />
        </div>
        {modules.has('cost-centers') && costCenters.length > 0 && (
          <div className="field field-wide">
            <label htmlFor="budget-cost-center">{t('reports.costCenter')}</label>
            <CostCenterSelect id="budget-cost-center" value={costCenterId} onChange={setCostCenterId} costCenters={costCenters} placeholder={t('budgets.wholeCompany')} ariaLabel={t('reports.costCenter')} />
          </div>
        )}
      </div>
      {query.data ? <BudgetGrid key={`${fiscalYear}|${costCenterId ?? ''}`} budget={query.data} startMonth={startMonth} /> : null}
      {copying && <CopyModal toYear={fiscalYear} years={years} costCenterId={costCenterId} onClose={() => setCopying(false)} />}
      <ImportModal
        open={importing}
        onClose={() => setImporting(false)}
        title={t('budgets.importTitle')}
        intro={t('budgets.importIntro')}
        columns="Account code, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12"
        url={importUrl}
        templateKey="budget"
      />
    </div>
  )
}

function BudgetGrid({ budget, startMonth }: { budget: BudgetDto; startMonth: number }) {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const accountsQuery = useAccounts()
  const accounts = useMemo(() => accountsQuery.data ?? [], [accountsQuery.data])
  const company = useCurrentCompany()
  const currencies = useCurrencies()
  const minorUnits = currencies.data?.find((c) => c.code === company.data?.baseCurrencyCode)?.minorUnits ?? 2
  const [rows, setRows] = useState<Row[]>(() => budget.lines.map((l) => ({ accountId: l.accountId, amounts: [...l.amounts] })))
  const [problems, setProblems] = useState<string[]>([])
  const [adding, setAdding] = useState<string>()

  const byId = useMemo(() => new Map(accounts.map((a) => [a.id, a])), [accounts])
  const monthName = (period: number) => new Date(2000, (startMonth - 1 + period) % 12, 1).toLocaleDateString(settings.language === 'ar' ? 'ar' : 'en', { month: 'short' })
  const total = (row: Row) => row.amounts.reduce<number>((sum, a) => sum + (a ?? 0), 0)

  const setMonth = (accountId: string, month: number, value: number | null) =>
    setRows((current) => current.map((r) => (r.accountId === accountId ? { ...r, amounts: r.amounts.map((a, i) => (i === month ? value : a)) } : r)))
  const spread = (accountId: string, value: number | null) => {
    const yearly = value ?? 0
    const each = round(yearly / 12)
    setRows((current) => current.map((r) => (r.accountId === accountId ? { ...r, amounts: Array.from({ length: 12 }, (_, i) => (i === 11 ? round(yearly - each * 11) : each)) } : r)))
  }

  const save = useMutation({
    mutationFn: () => saveBudget({ fiscalYear: budget.fiscalYear, costCenterId: budget.costCenterId, lines: rows.map((r) => ({ accountId: r.accountId, amounts: r.amounts.map((a) => a ?? 0) })) }),
    onSuccess: async () => {
      setProblems([])
      await refreshBooks(queryClient)
      void message.success(t('budgets.saved'))
    },
    onError: (error) =>
      setProblems(
        error instanceof ApiError && error.issues.length > 0
          ? error.issues.map((i) => {
              const row = /^lines\[(\d+)\]/.exec(i.field)
              const account = row ? byId.get(rows[Number(row[1])]?.accountId ?? '') : undefined
              const text = t([`budgets.issues.${i.code}`], { defaultValue: i.code })
              return account ? `${account.code}: ${text}` : text
            })
          : [errorMessage(error, t)],
      ),
  })

  const columns: ColumnsType<Row> = [
    {
      title: t('accounting.account'),
      key: 'account',
      fixed: 'left',
      width: 220,
      render: (_: unknown, r) => {
        const a = byId.get(r.accountId)
        return a ? `${a.code} — ${itemName(a, settings.language)}` : ''
      },
    },
    ...Array.from({ length: 12 }, (_, month) => ({
      title: monthName(month),
      key: `m${month}`,
      width: 100,
      render: (_: unknown, r: Row) => (
        <InputNumber
          value={r.amounts[month]}
          min={0}
          precision={Math.max(minorUnits, 0)}
          controls={false}
          className="amount-input"
          onChange={(value) => setMonth(r.accountId, month, value)}
          aria-label={`${byId.get(r.accountId)?.code ?? ''} ${monthName(month)}`}
        />
      ),
    })),
    {
      title: t('budgets.yearly'),
      key: 'total',
      width: 130,
      render: (_: unknown, r: Row) => (
        <InputNumber
          value={total(r)}
          min={0}
          precision={Math.max(minorUnits, 0)}
          controls={false}
          className="amount-input"
          onChange={(value) => spread(r.accountId, value)}
          aria-label={`${byId.get(r.accountId)?.code ?? ''} ${t('budgets.yearly')}`}
        />
      ),
    },
    {
      title: <span className="visually-hidden">{t('voucher.rowActions')}</span>,
      key: 'remove',
      width: 90,
      render: (_: unknown, r: Row) => (
        <Button type="link" danger onClick={() => setRows((current) => current.filter((x) => x.accountId !== r.accountId))} aria-label={`${t('voucher.removeRow')} ${byId.get(r.accountId)?.code ?? ''}`}>
          {t('voucher.removeRow')}
        </Button>
      ),
    },
  ]

  const addable = accounts.filter((a: AccountDto) => a.isPosting && (a.type === 'Revenue' || a.type === 'Expense') && !rows.some((r) => r.accountId === a.id))
  return (
    <>
      <p className="muted">{t('budgets.intro')}</p>
      {problems.length > 0 && <Alert type="error" showIcon className="form-alert" message={problems.join(' ')} />}
      <Table<Row> columns={columns} dataSource={rows} rowKey="accountId" pagination={false} size="small" bordered scroll={{ x: 1700 }} />
      <div className="filter-bar">
        <div className="field field-wide">
          <label htmlFor="budget-add">{t('budgets.addAccount')}</label>
          <AccountSelect
            id="budget-add"
            value={adding}
            onChange={(id) => {
              if (id) setRows((current) => [...current, { accountId: id, amounts: Array.from({ length: 12 }, () => null) }])
              setAdding(undefined)
            }}
            accounts={addable}
            ariaLabel={t('budgets.addAccount')}
          />
        </div>
        <Button type="primary" onClick={() => save.mutate()} loading={save.isPending}>
          {t('common.save')}
        </Button>
      </div>
    </>
  )
}

function CopyModal({ toYear, years, costCenterId, onClose }: { toYear: number; years: number[]; costCenterId?: string; onClose: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [from, setFrom] = useState<number | undefined>(years.find((y) => y !== toYear))
  const [percent, setPercent] = useState<number | null>(0)
  const [problem, setProblem] = useState<string>()

  const copy = useMutation({
    mutationFn: () => copyBudget({ fromFiscalYear: from!, toFiscalYear: toYear, costCenterId: costCenterId ?? null, percentChange: percent ?? 0 }),
    onSuccess: async () => {
      await refreshBooks(queryClient)
      onClose()
    },
    onError: (error) => setProblem(error instanceof ApiError && error.issues.length > 0 ? error.issues.map((i) => t([`budgets.issues.${i.code}`], { defaultValue: i.code })).join(' ') : errorMessage(error, t)),
  })

  return (
    <Modal open title={t('budgets.copyTitle', { year: toYear })} onCancel={onClose} onOk={() => copy.mutate()} okText={t('budgets.copy')} okButtonProps={{ disabled: from === undefined }} cancelText={t('common.cancel')} confirmLoading={copy.isPending} maskClosable={false}>
      <p>{t('budgets.copyIntro')}</p>
      {problem && <Alert type="error" showIcon message={problem} className="form-alert" />}
      <div className="field">
        <label htmlFor="copy-from">{t('budgets.copyFrom')}</label>
        <Select id="copy-from" value={from} onChange={setFrom} options={years.filter((y) => y !== toYear).map((y) => ({ value: y, label: String(y) }))} aria-label={t('budgets.copyFrom')} />
      </div>
      <div className="field">
        <label htmlFor="copy-percent">{t('budgets.percentChange')}</label>
        <InputNumber id="copy-percent" value={percent} onChange={setPercent} min={-100} max={1000} precision={2} controls={false} className="amount-input" addonAfter="%" />
      </div>
    </Modal>
  )
}
