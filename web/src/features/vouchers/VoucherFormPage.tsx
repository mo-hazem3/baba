import { Alert, App, Button, Input, Spin, Tag } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Navigate, useNavigate, useParams } from 'react-router'
import { deleteVoucher, getVoucher, printVoucher, saveAndPostVoucher, saveVoucherDraft } from '../../api/generated/baba'
import type { VoucherDto, VoucherKind } from '../../api/generated/model'
import { ApiError, asBlob } from '../../api/http'
import { refreshBooks, useAccounts, useCostCenters, useCurrentCompany, useCurrencies, useHost, useModules, useParties, usePrintSettings } from '../../api/hooks'
import { AmountText } from '../../layout/AmountText'
import { DateField } from '../../layout/DateField'
import { errorMessage } from '../../layout/errors'
import { FormPage } from '../../layout/FormPage'
import { useShortcuts } from '../../layout/useShortcuts'
import { useUnsavedWork } from '../../layout/useUnsavedWork'
import { useSettings } from '../../settings/SettingsContext'
import { formatAmount, toIsoDate } from '../../utils/format'
import { openPdf } from '../../utils/download'
import { AccountSearchDialog } from '../accounting/AccountSearchDialog'
import { AccountSelect } from '../accounting/AccountSelect'
import { CurrencyFields } from '../accounting/CurrencyFields'
import { VoucherLinesGrid } from '../accounting/VoucherLinesGrid'
import { RecurringModal } from '../recurring/RecurringModal'
import { TransferForm } from './TransferForm'
import {
  hasFreeLines,
  isBlank,
  kindFromRoute,
  listPathOf,
  mapIssues,
  newRow,
  rowsFromVoucher,
  rowsToSend,
  toVoucherInput,
  totals,
  usesCashAccount,
  withTrailingBlank,
  type LineRow,
  type MappedIssues,
} from '../accounting/voucherModel'

/** /vouchers/:kind/new and /vouchers/:kind/:id. Loads the voucher (if there is one) before showing the form. */
export function VoucherFormPage() {
  const { kind: segment, id } = useParams()
  const kind = kindFromRoute(segment)
  const voucherId = id && id !== 'new' ? id : undefined

  const query = useQuery({
    queryKey: ['/api/vouchers', voucherId],
    enabled: voucherId !== undefined,
    queryFn: async () => {
      const response = await getVoucher(voucherId!)
      return response.status === 200 ? response.data : null
    },
  })

  if (!kind) return <Navigate to="/" replace />
  if (voucherId && query.isPending) return <Spin size="large" className="page-spinner" />
  if (voucherId && query.data === null) return <Navigate to={listPathOf(kind)} replace />

  // A new form for each voucher, so nothing typed in one leaks into the next.
  if (kind === 'Transfer') return <TransferForm key={`transfer-${voucherId ?? 'new'}`} initial={query.data ?? undefined} />
  return <VoucherForm key={`${kind}-${voucherId ?? 'new'}`} kind={kind} initial={query.data ?? undefined} />
}

const fingerprint = (date: string, cash: string | undefined, reference: string, memo: string, rows: readonly LineRow[], currency = '', rate: number | null = null) =>
  JSON.stringify([date, cash ?? '', currency, rate ?? 0, reference.trim(), memo.trim(), rowsToSend(rows).map((r) => [r.accountId ?? '', r.partyId ?? '', r.costCenterId ?? '', r.description.trim(), r.debit ?? 0, r.credit ?? 0])])

function VoucherForm({ kind, initial }: { kind: VoucherKind; initial?: VoucherDto }) {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const { message, modal } = App.useApp()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const accountsQuery = useAccounts()
  const accounts = accountsQuery.data ?? []
  const modules = useModules()
  const partiesQuery = useParties()
  const parties = partiesQuery.data ?? []
  const costCentersQuery = useCostCenters()
  const costCenters = costCentersQuery.data ?? []
  const company = useCurrentCompany()
  const currencies = useCurrencies()
  const host = useHost()
  const printSettings = usePrintSettings()

  const baseCurrencyCode = company.data?.baseCurrencyCode ?? ''
  const listPath = listPathOf(kind)
  const readOnly = kind === 'Closing' || kind === 'FxSettlement' // made by Baba (closing a year, a payment), never by hand
  const freeLines = hasFreeLines(kind)

  // Opening balances are dated the day before the books start; everything else starts today.
  const defaultDate =
    kind === 'Opening' && company.data
      ? toIsoDate(new Date(company.data.firstFiscalYear, company.data.fiscalYearStartMonth - 1, 0))
      : toIsoDate(new Date())
  const [date, setDate] = useState(initial?.date ?? defaultDate)
  const [cashAccountId, setCashAccountId] = useState(initial?.cashAccountId ?? undefined)
  const [reference, setReference] = useState(initial?.reference ?? '')
  const [memo, setMemo] = useState(initial?.memo ?? '')
  const [currencyCode, setCurrencyCode] = useState(initial?.currencyCode ?? baseCurrencyCode)
  const [rate, setRate] = useState<number | null>(initial && initial.currencyCode !== baseCurrencyCode ? (initial.exchangeRate ?? null) : null)
  const minorUnits = currencies.data?.find((c) => c.code === currencyCode)?.minorUnits ?? 2
  const [rows, setRows] = useState<LineRow[]>(() => withTrailingBlank(initial ? rowsFromVoucher(initial) : [newRow()]))
  const [issues, setIssues] = useState<MappedIssues>()
  const [find, setFind] = useState<{ open: boolean; target?: string }>({ open: false })
  const [repeating, setRepeating] = useState(false)

  const current = () => fingerprint(date, cashAccountId, reference, memo, rows, currencyCode, rate)
  const [savedFingerprint, setSavedFingerprint] = useState(current)
  const dirty = current() !== savedFingerprint
  useUnsavedWork(dirty)

  const sum = totals(rows)
  const hasAmounts = rowsToSend(rows).length > 0
  const total = kind === 'Receipt' ? sum.credit : sum.debit
  const input = () =>
    toVoucherInput(kind, { date, cashAccountId, reference, memo, currencyCode: currencyCode === baseCurrencyCode ? '' : currencyCode, exchangeRate: rate }, rows)

  const finish = async (text: string) => {
    setSavedFingerprint(current()) // saved: nothing is unsaved any more
    await refreshBooks(queryClient)
    void message.success(text)
    navigate(listPath)
  }

  const save = useMutation({
    mutationFn: (post: boolean) => {
      const request = { id: initial?.id ?? null, input: input() }
      return post ? saveAndPostVoucher(request) : saveVoucherDraft(request)
    },
    onSuccess: (response, post) => finish(post ? t('voucher.postedAs', { number: response.data.number }) : t('voucher.savedDraft')),
    onError: (error) => {
      if (error instanceof ApiError && error.issues.length > 0) setIssues(mapIssues(error.issues, rowsToSend(rows)))
      else void message.error(errorMessage(error, t))
    },
  })

  const print = useMutation({
    mutationFn: async () => {
      const response = await printVoucher(initial!.id, { layout: printSettings.data?.defaultLayout })
      if (response.status !== 200) throw new ApiError(response.status, 'NotImplemented', 'This host cannot make PDFs.')
      openPdf(asBlob(response.data))
    },
    onError: (error) => void message.error(error instanceof ApiError && error.status === 501 ? t('export.pdfUnavailable') : errorMessage(error, t)),
  })

  const remove = useMutation({
    mutationFn: () => deleteVoucher(initial!.id),
    onSuccess: async () => {
      const snapshot = input()
      const wasDraft = initial!.status === 'Draft'
      setSavedFingerprint(current())
      await refreshBooks(queryClient)
      navigate(listPath)
      if (wasDraft) {
        // A draft is gone at once, with a few seconds to take it back (brief section 7.4).
        const key = `undo-${initial!.id}`
        void message.open({
          key,
          duration: 8,
          content: (
            <span>
              {t('voucher.draftDeleted')}{' '}
              <Button
                type="link"
                size="small"
                onClick={async () => {
                  message.destroy(key)
                  await saveVoucherDraft({ id: null, input: snapshot })
                  await refreshBooks(queryClient)
                  void message.success(t('voucher.draftRestored'))
                }}
              >
                {t('common.undo')}
              </Button>
            </span>
          ),
        })
      } else {
        void message.success(t('voucher.deleted'))
      }
    },
    onError: (error) => {
      if (error instanceof ApiError && error.issues.length > 0) setIssues(mapIssues(error.issues, []))
      else void message.error(errorMessage(error, t))
    },
  })

  const confirmDelete = () => {
    if (!initial) return
    if (initial.status === 'Draft') return remove.mutate()
    modal.confirm({
      title: t('voucher.deleteTitle', {
        kind: t(`voucher.title.${kind}`),
        number: initial.number,
        amount: formatAmount(initial.total, minorUnits, settings.digits),
        currency: currencyCode,
      }),
      content: t('voucher.deleteBody'),
      okText: t('voucher.delete'),
      okButtonProps: { danger: true },
      cancelText: t('common.cancel'),
      onOk: () => remove.mutateAsync().catch(() => undefined),
    })
  }

  const cancel = () => {
    if (!dirty) return navigate(listPath)
    modal.confirm({
      title: t('voucher.discardTitle'),
      content: t('voucher.discardBody'),
      okText: t('voucher.discard'),
      okButtonProps: { danger: true },
      cancelText: t('voucher.keepEditing'),
      onOk: () => {
        setSavedFingerprint(current())
        navigate(listPath)
      },
    })
  }

  const openFind = () => {
    const active = document.activeElement as HTMLElement | null
    const rowIndex = active?.closest<HTMLElement>('[data-row]')?.dataset.row
    const target = active?.closest('[data-field="cash"]')
      ? 'cash'
      : rowIndex !== undefined
        ? rows[Number(rowIndex)]?.key
        : (rows.find(isBlank) ?? rows[0])?.key
    setFind({ open: true, target })
  }

  const pick = (accountId: string) => {
    if (find.target === 'cash') {
      setCashAccountId(accountId)
    } else if (find.target) {
      const index = rows.findIndex((r) => r.key === find.target)
      setRows(withTrailingBlank(rows.map((r) => (r.key === find.target ? { ...r, accountId } : r))))
      setTimeout(() => document.querySelector<HTMLElement>(`[data-row="${index}"][data-col="description"] input`)?.focus(), 50)
    }
    setFind({ open: false })
  }

  const busy = save.isPending || remove.isPending
  useShortcuts({
    'ctrl+s': () => !busy && save.mutate(true),
    'ctrl+p': () => initial && !dirty && host.data?.pdfPrinting && print.mutate(),
    f2: openFind,
    'ctrl+n': () => !dirty && kind !== 'Opening' && kind !== 'Closing' && navigate(`${listPath}/new`),
  })

  // The opening balances are the balance sheet as it was: revenue and expense accounts have no opening balance.
  const gridAccounts = kind === 'Opening' ? accounts.filter((a) => a.type !== 'Revenue' && a.type !== 'Expense') : accounts
  const problem = (code: string | undefined) => (code ? t(`voucher.issues.${code}`, { defaultValue: code }) : undefined)
  const cashLabel = kind === 'Payment' ? t('voucher.paidFrom') : t('voucher.receivedInto')
  const difference = sum.difference
  const title = initial?.number ? `${t(`voucher.title.${kind}`)} ${initial.number}` : initial ? `${t(`voucher.title.${kind}`)} (${t('voucher.draft')})` : t(`voucher.new.${kind}`)

  return (
    <FormPage
      title={title}
      help="voucher"
      crumbs={[
        { label: t('breadcrumb.home'), to: '/' },
        { label: t(`voucher.plural.${kind}`), to: listPath },
        { label: initial ? (initial.number ?? t('voucher.draft')) : t('voucher.newShort') },
      ]}
      badge={initial && (initial.status === 'Posted' ? <Tag color="blue">{t('voucher.posted')}</Tag> : <Tag>{t('voucher.draft')}</Tag>)}
      save={readOnly ? undefined : { label: t('voucher.save'), onClick: () => save.mutate(true), loading: save.isPending && save.variables === true, disabled: busy }}
      saveAsDraft={readOnly ? undefined : { label: t('voucher.saveAsDraft'), onClick: () => save.mutate(false), loading: save.isPending && save.variables === false, disabled: busy }}
      print={{
        label: t('voucher.print'),
        onClick: () => print.mutate(),
        loading: print.isPending,
        disabled: !initial || dirty || !host.data?.pdfPrinting,
        title: !initial || dirty ? t('voucher.printSaveFirst') : host.data?.pdfPrinting ? undefined : t('export.pdfUnavailable'),
      }}
      cancel={{ label: t('common.cancel'), onClick: cancel, disabled: busy }}
      remove={initial && !readOnly ? { label: t('voucher.delete'), onClick: confirmDelete, loading: remove.isPending, disabled: busy } : undefined}
    >
      {readOnly && <Alert type="info" showIcon className="form-alert" message={t(kind === 'Closing' ? 'voucher.closingNote' : 'voucher.fxNote')} />}
      {kind === 'Opening' && !initial && <Alert type="info" showIcon className="form-alert" message={t('voucher.openingNote')} />}
      {issues && issues.list.length > 0 && (
        <Alert
          type="error"
          showIcon
          className="form-alert"
          message={t('voucher.cannotSave')}
          description={
            <ul className="issue-list">
              {issues.list.map((issue, i) => (
                <li key={i}>{t(`voucher.issues.${issue.code}`, { line: issue.line, defaultValue: issue.code })}</li>
              ))}
            </ul>
          }
        />
      )}

      {initial && !readOnly && kind !== 'Opening' && (
        <div className="doc-actions">
          <span className="muted">{t('recurring.repeatHelp')}</span>
          <Button onClick={() => setRepeating(true)} disabled={dirty}>
            {t('recurring.repeat')}
          </Button>
        </div>
      )}
      {repeating && initial && (
        <RecurringModal
          open
          source={{ type: 'voucher', id: initial.id, name: initial.memo || `${t(`voucher.title.${kind}`)} ${initial.number ?? ''}`.trim() }}
          onClose={() => setRepeating(false)}
          onSaved={() => void message.success(t('recurring.saved'))}
        />
      )}

      <div className="voucher-header">
        <div className="field">
          <label htmlFor="voucher-date">{t('voucher.date')}</label>
          <DateField id="voucher-date" value={date} onChange={(v) => v && setDate(v)} status={issues?.header.date ? 'error' : undefined} ariaLabel={t('voucher.date')} />
          {issues?.header.date && <div className="cell-error">{problem(issues.header.date)}</div>}
        </div>
        {usesCashAccount(kind) && (
          <div className="field field-wide" data-field="cash">
            <label htmlFor="voucher-cash">{cashLabel}</label>
            <AccountSelect
              id="voucher-cash"
              value={cashAccountId}
              onChange={setCashAccountId}
              accounts={accounts}
              cashOnly
              status={issues?.header.cashAccount ? 'error' : undefined}
              placeholder={t('voucher.cashPlaceholder')}
              ariaLabel={cashLabel}
            />
            {issues?.header.cashAccount && <div className="cell-error">{problem(issues.header.cashAccount)}</div>}
          </div>
        )}
        <CurrencyFields
          date={date}
          currencyCode={currencyCode}
          rate={rate}
          disabled={readOnly}
          rateStatus={issues?.header.exchangeRate ? 'error' : undefined}
          onChange={(change) => {
            setCurrencyCode(change.currencyCode)
            setRate(change.rate)
          }}
        />
        {issues?.header.exchangeRate && <div className="cell-error">{problem(issues.header.exchangeRate)}</div>}
        <div className="field">
          <label htmlFor="voucher-reference">{t('voucher.reference')}</label>
          <Input id="voucher-reference" value={reference} onChange={(e) => setReference(e.target.value)} maxLength={100} dir="ltr" />
        </div>
      </div>

      <VoucherLinesGrid
        kind={kind}
        rows={rows}
        onChange={(next) => {
          setRows(next)
          setIssues(undefined) // an edit takes the old complaints away; saving checks again
        }}
        accounts={gridAccounts}
        disabled={readOnly}
        parties={parties}
        costCenters={costCenters}
        // The columns are offered when the module is on, and also whenever the voucher already has such a tag (switching a module off hides nothing that was recorded).
        showParty={modules.has('customers-suppliers') || rows.some((r) => r.partyId)}
        showCostCenter={modules.has('cost-centers') || rows.some((r) => r.costCenterId)}
        issues={issues?.rows ?? {}}
        minorUnits={minorUnits}
      />
      {issues?.header.lines && <div className="cell-error">{problem(issues.header.lines)}</div>}

      <div className="voucher-totals" aria-live="polite">
        {freeLines ? (
          <>
            <span>
              {t('accounting.debit')}: <AmountText value={sum.debit} minorUnits={minorUnits} />
            </span>
            <span>
              {t('accounting.credit')}: <AmountText value={sum.credit} minorUnits={minorUnits} />
            </span>
            {hasAmounts && (
              <strong className={difference === 0 ? 'check-ok' : 'check-bad'}>
                {difference === 0 ? (
                  t('voucher.balanced')
                ) : (
                  <>
                    {t('voucher.difference')}: <AmountText value={difference} minorUnits={minorUnits} /> — {t('voucher.mustBeZero')}
                  </>
                )}
              </strong>
            )}
          </>
        ) : (
          <strong>
            {t('voucher.total')} ({currencyCode}): <AmountText value={total} minorUnits={minorUnits} />
          </strong>
        )}
      </div>

      <div className="field field-full">
        <label htmlFor="voucher-memo">{t('voucher.notes')}</label>
        <Input.TextArea id="voucher-memo" value={memo} onChange={(e) => setMemo(e.target.value)} rows={2} maxLength={500} />
      </div>

      <AccountSearchDialog open={find.open} accounts={gridAccounts} cashOnly={find.target === 'cash'} onPick={pick} onClose={() => setFind({ open: false })} />
    </FormPage>
  )
}
