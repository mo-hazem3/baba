import { Alert, App, Button, Card, InputNumber, Space, Table, Tag } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, Navigate, useParams } from 'react-router'
import { clearBankStatement, completeReconciliation, getReconciliation, listReconciliations, undoLastReconciliation } from '../../api/generated/baba'
import type { BankEntry, ReconciliationDto, StatementLineDto } from '../../api/generated/model'
import { ApiError } from '../../api/http'
import { refreshBooks, useAccounts, useCurrencies, useCurrentCompany } from '../../api/hooks'
import { AmountText } from '../../layout/AmountText'
import { DateField } from '../../layout/DateField'
import { errorMessage } from '../../layout/errors'
import { ImportModal } from '../../layout/ImportModal'
import { PageHeader } from '../../layout/PageHeader'
import { useSettings } from '../../settings/SettingsContext'
import { formatDate, parseIsoDate, toIsoDate } from '../../utils/format'
import { itemName } from '../accounting/LookupSelects'

/** Money is compared in whole 1/10,000 units so that adding up never leaves a 0.0000001 difference on screen. */
const scaled = (value: number) => Math.round(value * 10_000)

const statementTemplate = 'Date,Description,Reference,Amount\n2026-01-31,Customer transfer,REF-1,1500.000\n2026-02-01,Rent,CHQ-77,-400.000\n'

/**
 * Bank reconciliation (brief section 10.2). Enter the date and closing balance printed on the bank's statement, tick the entries that
 * appear on it, and the difference must come to zero before the reconciliation can be finished. An imported statement helps by
 * suggesting which entries match which lines.
 */
export function ReconcilePage() {
  const { accountId } = useParams()
  if (!accountId) return <Navigate to="/bank" replace />
  return <Reconcile accountId={accountId} />
}

function Reconcile({ accountId }: { accountId: string }) {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const { message, modal } = App.useApp()
  const queryClient = useQueryClient()
  const company = useCurrentCompany()
  const currencies = useCurrencies()
  const accounts = useAccounts()
  const minorUnits = currencies.data?.find((c) => c.code === company.data?.baseCurrencyCode)?.minorUnits ?? 2
  const account = accounts.data?.find((a) => a.id === accountId)

  const [statementDate, setStatementDate] = useState(toIsoDate(new Date()))
  const [statementBalance, setStatementBalance] = useState<number | null>(null)
  const [ticked, setTicked] = useState<ReadonlySet<string>>(new Set())
  const [importing, setImporting] = useState(false)

  const view = useQuery({
    queryKey: ['/api/bank', accountId, 'reconciliation', statementDate],
    queryFn: async () => {
      const response = await getReconciliation(accountId, { statementDate })
      if (response.status !== 200) throw new ApiError(response.status, 'NotFound', 'No such account.')
      return response.data
    },
  })
  const history = useQuery({
    queryKey: ['/api/bank', accountId, 'history'],
    queryFn: async () => (await listReconciliations(accountId)).data,
  })

  const entries = useMemo(() => view.data?.entries ?? [], [view.data])
  const lines = useMemo(() => view.data?.statementLines ?? [], [view.data])
  const suggestions = useMemo(() => view.data?.suggestions ?? [], [view.data])
  const entryById = useMemo(() => new Map(entries.map((e) => [e.entryId, e])), [entries])
  const matchOfLine = useMemo(() => new Map(suggestions.map((s) => [s.statementLineId, s.entryId])), [suggestions])

  // Only entries that are in the list for this date count, even if one was ticked before the date was changed.
  const tickedHere = entries.filter((e) => ticked.has(e.entryId))
  const opening = view.data?.openingBalance ?? 0
  const tickedTotal = tickedHere.reduce((sum, e) => sum + scaled(e.amount), 0) / 10_000
  const calculated = (scaled(opening) + scaled(tickedTotal)) / 10_000
  const difference = statementBalance === null ? null : (scaled(statementBalance) - scaled(calculated)) / 10_000
  const balanced = difference === 0

  const autoMatch = () => setTicked(new Set([...ticked, ...suggestions.map((s) => s.entryId)]))

  const finish = useMutation({
    mutationFn: () =>
      completeReconciliation(accountId, {
        statementDate,
        statementBalance: statementBalance ?? 0,
        entryIds: tickedHere.map((e) => e.entryId),
        statementLineIds: lines.filter((l) => !l.reconciled && matchOfLine.has(l.id) && ticked.has(matchOfLine.get(l.id)!)).map((l) => l.id),
      }),
    onSuccess: async () => {
      setTicked(new Set())
      setStatementBalance(null)
      await refreshBooks(queryClient)
      void message.success(t('bank.reconciled'))
    },
    onError: (error) => {
      const issue = error instanceof ApiError ? error.issues[0]?.code : undefined
      void message.error(issue ? t(`bank.issues.${issue}`, { defaultValue: errorMessage(error, t) }) : errorMessage(error, t))
    },
  })

  const undo = useMutation({
    mutationFn: () => undoLastReconciliation(accountId),
    onSuccess: async () => {
      setTicked(new Set())
      await refreshBooks(queryClient)
      void message.success(t('bank.undone'))
    },
    onError: (error) => void message.error(errorMessage(error, t)),
  })

  const clear = useMutation({
    mutationFn: () => clearBankStatement(accountId),
    onSuccess: async (response) => {
      await refreshBooks(queryClient)
      void message.success(t('bank.cleared', { count: response.data.removed }))
    },
    onError: (error) => void message.error(errorMessage(error, t)),
  })

  const confirm = (title: string, body: string, run: () => void) =>
    modal.confirm({ title, content: body, okText: t('common.ok'), cancelText: t('common.cancel'), okButtonProps: { danger: true }, onOk: run })

  const entryColumns: ColumnsType<BankEntry> = [
    { title: t('voucher.date'), dataIndex: 'date', width: 120, render: (d: string) => formatDate(parseIsoDate(d), settings.digits, settings.hijri) },
    {
      title: t('voucher.number'),
      key: 'number',
      width: 150,
      render: (_: unknown, e) => (
        <Link to={`/vouchers/open/${e.voucherId}`} dir="ltr">
          {e.voucherNumber ?? t('voucher.draft')}
        </Link>
      ),
    },
    { title: t('accounting.description'), dataIndex: 'description' },
    { title: t('bank.moneyIn'), key: 'in', width: 130, align: 'end', render: (_: unknown, e) => (e.amount > 0 ? <AmountText value={e.amount} minorUnits={minorUnits} /> : null) },
    { title: t('bank.moneyOut'), key: 'out', width: 130, align: 'end', render: (_: unknown, e) => (e.amount < 0 ? <AmountText value={-e.amount} minorUnits={minorUnits} /> : null) },
  ]

  const lineColumns: ColumnsType<StatementLineDto> = [
    { title: t('voucher.date'), dataIndex: 'date', width: 120, render: (d: string) => formatDate(parseIsoDate(d), settings.digits, settings.hijri) },
    { title: t('accounting.description'), key: 'description', render: (_: unknown, l) => [l.description, l.reference].filter(Boolean).join(' · ') },
    { title: t('accounting.amount'), key: 'amount', width: 130, align: 'end', render: (_: unknown, l) => <AmountText value={l.amount} minorUnits={minorUnits} /> },
    {
      title: t('bank.match'),
      key: 'match',
      width: 190,
      render: (_: unknown, l) => {
        if (l.reconciled) return <Tag color="green">{t('bank.lineReconciled')}</Tag>
        const entry = matchOfLine.has(l.id) ? entryById.get(matchOfLine.get(l.id)!) : undefined
        return entry ? <Tag color="blue">{t('bank.matches', { number: entry.voucherNumber ?? t('voucher.draft') })}</Tag> : <Tag>{t('bank.noMatch')}</Tag>
      },
    },
  ]

  const historyColumns: ColumnsType<ReconciliationDto> = [
    { title: t('bank.statementDate'), dataIndex: 'statementDate', render: (d: string) => formatDate(parseIsoDate(d), settings.digits, settings.hijri) },
    { title: t('bank.statementBalance'), key: 'balance', align: 'end', render: (_: unknown, r) => <AmountText value={r.statementBalance} minorUnits={minorUnits} /> },
    { title: t('bank.finishedOn'), dataIndex: 'completedAt', render: (d: string) => formatDate(new Date(d), settings.digits, settings.hijri) },
  ]

  const title = account ? `${t('bank.reconcileTitle')}: ${account.code} ${itemName(account, settings.language)}` : t('bank.reconcileTitle')

  return (
    <div>
      <PageHeader
        title={title}
        help="reconcile"
        crumbs={[{ label: t('breadcrumb.home'), to: '/' }, { label: t('bank.title'), to: '/bank' }, { label: t('bank.reconcileTitle') }]}
      />

      <div className="voucher-header">
        <div className="field">
          <label htmlFor="statement-date">{t('bank.statementDate')}</label>
          <DateField id="statement-date" value={statementDate} onChange={(v) => v && setStatementDate(v)} ariaLabel={t('bank.statementDate')} />
        </div>
        <div className="field">
          <label htmlFor="statement-balance">{t('bank.statementBalance')}</label>
          <InputNumber
            id="statement-balance"
            value={statementBalance}
            onChange={(v) => setStatementBalance(typeof v === 'number' ? v : null)}
            precision={minorUnits}
            controls={false}
            className="amount-input"
            aria-label={t('bank.statementBalance')}
          />
          <span className="muted">{t('bank.statementBalanceHelp')}</span>
        </div>
      </div>

      <Card className="reconcile-summary" size="small">
        <dl className="reconcile-figures">
          <div>
            <dt>{t('bank.openingBalance')}</dt>
            <dd>
              <AmountText value={opening} minorUnits={minorUnits} />
            </dd>
          </div>
          <div>
            <dt>{t('bank.tickedTotal', { count: tickedHere.length })}</dt>
            <dd>
              <AmountText value={tickedTotal} minorUnits={minorUnits} />
            </dd>
          </div>
          <div>
            <dt>{t('bank.calculatedBalance')}</dt>
            <dd>
              <AmountText value={calculated} minorUnits={minorUnits} />
            </dd>
          </div>
          <div aria-live="polite">
            <dt>{t('bank.difference')}</dt>
            <dd className={difference === null ? 'muted' : balanced ? 'check-ok' : 'check-bad'}>
              {difference === null ? t('bank.enterBalance') : balanced ? t('bank.noDifference') : <AmountText value={difference} minorUnits={minorUnits} />}
            </dd>
          </div>
        </dl>
      </Card>

      <div className="form-buttons">
        <Button type="primary" disabled={!balanced} loading={finish.isPending} onClick={() => finish.mutate()}>
          {t('bank.finish')}
        </Button>
        <Button onClick={autoMatch} disabled={suggestions.length === 0}>
          {t('bank.autoMatch')}
        </Button>
        <Button onClick={() => setImporting(true)}>{t('bank.importStatement')}</Button>
        <Button onClick={() => setTicked(new Set(entries.map((e) => e.entryId)))} disabled={entries.length === 0}>
          {t('bank.tickAll')}
        </Button>
        <Button onClick={() => setTicked(new Set())} disabled={ticked.size === 0}>
          {t('bank.untickAll')}
        </Button>
      </div>
      {statementBalance !== null && !balanced && tickedHere.length > 0 && <Alert type="warning" showIcon className="form-alert" message={t('bank.differenceHelp')} />}

      <Table<BankEntry>
        columns={entryColumns}
        dataSource={[...entries]}
        rowKey="entryId"
        loading={view.isPending}
        pagination={false}
        size="middle"
        bordered
        rowSelection={{
          selectedRowKeys: tickedHere.map((e) => e.entryId),
          onChange: (keys) => setTicked(new Set(keys as string[])),
          columnTitle: <span className="visually-hidden">{t('bank.tick')}</span>,
          getCheckboxProps: (e) => ({ 'aria-label': `${t('bank.tick')} ${e.voucherNumber ?? ''} ${e.description ?? ''}` }),
        }}
        locale={{ emptyText: t('bank.nothingToCheck') }}
        summary={() => (
          <Table.Summary.Row className="report-total">
            <Table.Summary.Cell index={0} />
            <Table.Summary.Cell index={1} colSpan={3}>
              {t('bank.entriesUpTo', { count: entries.length })}
            </Table.Summary.Cell>
            <Table.Summary.Cell index={2} colSpan={2} />
          </Table.Summary.Row>
        )}
      />

      {lines.length > 0 && (
        <Card
          title={t('bank.statementLines')}
          className="settings-card"
          extra={
            lines.some((l) => !l.reconciled) && (
              <Button danger onClick={() => confirm(t('bank.clearTitle'), t('bank.clearBody'), () => clear.mutate())} loading={clear.isPending}>
                {t('bank.clearLines')}
              </Button>
            )
          }
        >
          <Table<StatementLineDto> columns={lineColumns} dataSource={[...lines]} rowKey="id" pagination={false} size="small" bordered />
        </Card>
      )}

      {(history.data?.length ?? 0) > 0 && (
        <Card
          title={t('bank.history')}
          className="settings-card"
          extra={
            <Button danger onClick={() => confirm(t('bank.undoTitle'), t('bank.undoBody'), () => undo.mutate())} loading={undo.isPending}>
              {t('bank.undoLast')}
            </Button>
          }
        >
          <Table<ReconciliationDto> columns={historyColumns} dataSource={[...(history.data ?? [])]} rowKey="id" pagination={false} size="small" bordered />
        </Card>
      )}

      <Space />
      <ImportModal
        open={importing}
        onClose={() => setImporting(false)}
        title={t('bank.importStatement')}
        intro={t('bank.importIntro')}
        columns="Date, Description, Reference, Amount  (or Debit, Credit)"
        url={`/api/bank/accounts/${accountId}/statement`}
        template={statementTemplate}
        templateName="bank-statement-template.csv"
      />
    </div>
  )
}
