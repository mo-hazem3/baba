import { Alert, App, Button, Input, InputNumber, Table } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { listOutstandingInvoices, settleInvoices } from '../../api/generated/baba'
import type { OutstandingInvoice } from '../../api/generated/model'
import { ApiError } from '../../api/http'
import { refreshBooks, useAccounts, useCurrencies, useCurrentCompany, useParties } from '../../api/hooks'
import { AmountText } from '../../layout/AmountText'
import { DateField } from '../../layout/DateField'
import { EmptyState } from '../../layout/EmptyState'
import { errorMessage } from '../../layout/errors'
import { FormPage } from '../../layout/FormPage'
import { useUnsavedWork } from '../../layout/useUnsavedWork'
import { useSettings } from '../../settings/SettingsContext'
import { formatDate, parseIsoDate, toIsoDate } from '../../utils/format'
import { AccountSelect } from '../accounting/AccountSelect'
import { CurrencyFields } from '../accounting/CurrencyFields'
import { PartySelect } from '../accounting/LookupSelects'
import { documentPath } from './documentModel'

/**
 * Receive a payment from a customer, or pay a supplier, against specific invoices (brief section 10.3). It makes the receipt or payment
 * voucher, marks the invoices as paid by the amounts typed here, and books the exchange gain or loss when the rate moved since the
 * invoices were issued.
 */
export function SettlementPage({ side }: { side: 'customer' | 'supplier' }) {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const { message } = App.useApp()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [params] = useSearchParams()
  const company = useCurrentCompany()
  const currencies = useCurrencies()
  const accounts = useAccounts().data ?? []
  const parties = useParties().data ?? []

  const customer = side === 'customer'
  const base = company.data?.baseCurrencyCode ?? ''
  const listPath = customer ? '/sales' : '/purchases'
  const prefillDocument = params.get('documentId') ?? undefined

  const [partyId, setPartyId] = useState<string | undefined>(params.get('partyId') ?? undefined)
  const [date, setDate] = useState(toIsoDate(new Date()))
  const [cashAccountId, setCashAccountId] = useState<string>()
  const [reference, setReference] = useState('')
  const [memo, setMemo] = useState('')
  const [currencyChoice, setCurrencyChoice] = useState<string>()
  const [rate, setRate] = useState<number | null>(null)
  const [amounts, setAmounts] = useState<Record<string, number | null>>({})
  const [onAccount, setOnAccount] = useState<number | null>(null)
  const [problems, setProblems] = useState<string[]>([])
  const [touched, setTouched] = useState(false)
  useUnsavedWork(touched)

  const outstanding = useQuery({
    queryKey: ['/api/settlements/outstanding', partyId],
    enabled: partyId !== undefined,
    queryFn: async () => (await listOutstandingInvoices({ partyId })).data,
  })
  const open = (outstanding.data ?? []).filter((o) => o.outstanding > 0)

  // The payment is in the currency of the invoices: the one asked for, else that of the invoice that was opened, else the first owed.
  const currencyCode = currencyChoice ?? open.find((o) => o.documentId === prefillDocument)?.currencyCode ?? open[0]?.currencyCode ?? base
  const inCurrency = open.filter((o) => (o.currencyCode || base) === currencyCode)
  const minorUnits = currencies.data?.find((c) => c.code === currencyCode)?.minorUnits ?? 2

  const amountOf = (o: OutstandingInvoice): number | null =>
    o.documentId in amounts ? (amounts[o.documentId] ?? null) : o.documentId === prefillDocument ? o.outstanding : null
  const allocated = inCurrency.reduce((sum, o) => sum + (amountOf(o) ?? 0), 0)
  const total = Math.round((allocated + (onAccount ?? 0)) * 10 ** 4) / 10 ** 4

  const choosePartyId = (id: string | undefined) => {
    setPartyId(id)
    setAmounts({})
    setCurrencyChoice(undefined)
    setRate(null)
    setProblems([])
    setTouched(true)
  }

  const save = useMutation({
    mutationFn: () =>
      settleInvoices({
        partyId: partyId ?? '',
        date,
        cashAccountId: cashAccountId ?? '',
        currencyCode: currencyCode === base ? null : currencyCode,
        exchangeRate: currencyCode === base ? null : rate,
        reference: reference.trim() || null,
        memo: memo.trim() || null,
        allocations: inCurrency.flatMap((o) => ((amountOf(o) ?? 0) > 0 ? [{ documentId: o.documentId, amount: amountOf(o)! }] : [])),
        onAccount: onAccount ?? 0,
      }),
    onSuccess: async (response) => {
      setTouched(false)
      await refreshBooks(queryClient)
      const gain = response.data.exchangeGain
      void message.success(
        [
          t(customer ? 'trade.receiptRecorded' : 'trade.paymentRecorded', { number: response.data.payment.number }),
          gain !== 0 ? t(gain > 0 ? 'trade.exchangeGainNote' : 'trade.exchangeLossNote', { amount: Math.abs(gain).toFixed(3), currency: base }) : '',
        ].join(' '),
        6,
      )
      navigate(listPath)
    },
    onError: (error) => {
      if (error instanceof ApiError && error.issues.length > 0)
        setProblems(error.issues.map((i) => t([`trade.issues.${i.code}`, `voucher.issues.${i.code}`], { defaultValue: i.code })))
      else void message.error(errorMessage(error, t))
    },
  })

  const columns: ColumnsType<OutstandingInvoice> = [
    {
      title: t('trade.number'),
      key: 'number',
      width: 150,
      render: (_: unknown, o) => (
        <Link to={documentPath(o.kind, o.documentId)} dir="ltr">
          {o.number}
        </Link>
      ),
    },
    { title: t('voucher.date'), key: 'date', width: 170, render: (_: unknown, o) => formatDate(parseIsoDate(o.date), settings.digits, settings.hijri) },
    { title: t('trade.dueDate'), key: 'due', width: 170, render: (_: unknown, o) => (o.dueDate ? formatDate(parseIsoDate(o.dueDate), settings.digits, settings.hijri) : '') },
    { title: t('voucher.total'), key: 'total', align: 'end', width: 130, render: (_: unknown, o) => <AmountText value={o.total} minorUnits={minorUnits} /> },
    { title: t('trade.outstanding'), key: 'outstanding', align: 'end', width: 130, render: (_: unknown, o) => <AmountText value={o.outstanding} minorUnits={minorUnits} /> },
    {
      title: t('trade.payNow'),
      key: 'pay',
      width: 230,
      render: (_: unknown, o) => (
        <div className="pay-cell">
          <InputNumber
            value={amountOf(o)}
            min={0}
            max={o.outstanding}
            precision={minorUnits}
            controls={false}
            className="amount-input"
            aria-label={`${t('trade.payNow')} ${o.number}`}
            onChange={(v) => {
              setAmounts((all) => ({ ...all, [o.documentId]: typeof v === 'number' ? v : null }))
              setProblems([])
              setTouched(true)
            }}
          />
          <Button
            type="link"
            size="small"
            onClick={() => {
              setAmounts((all) => ({ ...all, [o.documentId]: o.outstanding }))
              setTouched(true)
            }}
          >
            {t('trade.payInFull')}
          </Button>
        </div>
      ),
    },
  ]

  const cashLabel = customer ? t('voucher.receivedInto') : t('voucher.paidFrom')
  const title = t(customer ? 'trade.receivePayment' : 'trade.payInvoices')

  return (
    <FormPage
      title={title}
      help="documents"
      crumbs={[
        { label: t('breadcrumb.home'), to: '/' },
        { label: t(customer ? 'trade.side.sales' : 'trade.side.purchases'), to: listPath },
        { label: title },
      ]}
      save={{
        label: t(customer ? 'trade.recordReceipt' : 'trade.recordPayment'),
        onClick: () => save.mutate(),
        loading: save.isPending,
        disabled: save.isPending || partyId === undefined || total <= 0,
      }}
      cancel={{ label: t('common.cancel'), onClick: () => navigate(listPath), disabled: save.isPending }}
    >
      {problems.length > 0 && (
        <Alert
          type="error"
          showIcon
          className="form-alert"
          message={t('trade.cannotSave')}
          description={
            <ul className="issue-list">
              {problems.map((p, i) => (
                <li key={i}>{p}</li>
              ))}
            </ul>
          }
        />
      )}
      <Alert type="info" showIcon className="form-alert" message={t('trade.settlementNote')} />

      <div className="voucher-header">
        <div className="field field-wide">
          <label htmlFor="settle-party">{customer ? t('trade.customer') : t('trade.supplier')}</label>
          <PartySelect
            id="settle-party"
            value={partyId}
            parties={parties}
            kind={customer ? 'Customer' : 'Supplier'}
            onChange={choosePartyId}
            placeholder={customer ? t('accounting.chooseCustomer') : t('accounting.chooseSupplier')}
            ariaLabel={customer ? t('trade.customer') : t('trade.supplier')}
            allowClear={false}
          />
        </div>
        <div className="field">
          <label htmlFor="settle-date">{t('voucher.date')}</label>
          <DateField id="settle-date" value={date} onChange={(v) => v && setDate(v)} ariaLabel={t('voucher.date')} />
        </div>
        <div className="field field-wide">
          <label htmlFor="settle-cash">{cashLabel}</label>
          <AccountSelect id="settle-cash" value={cashAccountId} onChange={setCashAccountId} accounts={accounts} cashOnly placeholder={t('voucher.cashPlaceholder')} ariaLabel={cashLabel} />
        </div>
        <CurrencyFields
          date={date}
          currencyCode={currencyCode}
          rate={rate}
          onChange={(change) => {
            setCurrencyChoice(change.currencyCode)
            setRate(change.rate)
            setAmounts({})
          }}
        />
        <div className="field">
          <label htmlFor="settle-reference">{t('voucher.reference')}</label>
          <Input id="settle-reference" value={reference} onChange={(e) => setReference(e.target.value)} maxLength={100} dir="ltr" />
        </div>
      </div>

      {partyId !== undefined && outstanding.isSuccess && inCurrency.length === 0 ? (
        <EmptyState title={t('trade.nothingOutstandingTitle')} body={t('trade.nothingOutstandingBody', { currency: currencyCode })} />
      ) : (
        partyId !== undefined && <Table<OutstandingInvoice> columns={columns} dataSource={inCurrency} rowKey="documentId" loading={outstanding.isPending} pagination={false} size="middle" bordered />
      )}

      {partyId !== undefined && (
        <div className="doc-totals" aria-live="polite">
          <div className="doc-discount">
            <label htmlFor="settle-on-account">{t('trade.onAccount')}</label>
            <InputNumber
              id="settle-on-account"
              value={onAccount}
              min={0}
              precision={minorUnits}
              controls={false}
              className="amount-input"
              onChange={(v) => {
                setOnAccount(typeof v === 'number' ? v : null)
                setTouched(true)
              }}
            />
          </div>
          <span className="muted">{t('trade.onAccountHelp')}</span>
          <strong>
            {t('voucher.total')} ({currencyCode}): <AmountText value={total} minorUnits={minorUnits} />
          </strong>
        </div>
      )}

      <div className="field field-full">
        <label htmlFor="settle-memo">{t('voucher.notes')}</label>
        <Input.TextArea id="settle-memo" value={memo} onChange={(e) => setMemo(e.target.value)} rows={2} maxLength={500} />
      </div>
    </FormPage>
  )
}
