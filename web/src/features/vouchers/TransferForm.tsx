import { Alert, App, Input, InputNumber } from 'antd'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router'
import { deleteVoucher, printVoucher, saveAndPostVoucher, saveVoucherDraft } from '../../api/generated/baba'
import type { VoucherDto, VoucherInput } from '../../api/generated/model'
import { ApiError, asBlob, type ApiIssue } from '../../api/http'
import { refreshBooks, useAccounts, useCurrentCompany, useCurrencies, useHost, usePrintSettings } from '../../api/hooks'
import { DateField } from '../../layout/DateField'
import { errorMessage } from '../../layout/errors'
import { FormPage } from '../../layout/FormPage'
import { useShortcuts } from '../../layout/useShortcuts'
import { useUnsavedWork } from '../../layout/useUnsavedWork'
import { openPdf } from '../../utils/download'
import { toIsoDate } from '../../utils/format'
import { AccountSearchDialog } from '../accounting/AccountSearchDialog'
import { AccountSelect } from '../accounting/AccountSelect'

const listPath = '/vouchers/transfer'

/**
 * A transfer between two bank or cash accounts (brief section 10.2): from, to, amount and date. It is a voucher like the others, with
 * its own number (TV-...), but its form has only what a transfer needs.
 */
export function TransferForm({ initial }: { initial?: VoucherDto }) {
  const { t } = useTranslation()
  const { message, modal } = App.useApp()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const accounts = useAccounts().data ?? []
  const company = useCurrentCompany()
  const currencies = useCurrencies()
  const host = useHost()
  const printSettings = usePrintSettings()
  const minorUnits = currencies.data?.find((c) => c.code === company.data?.baseCurrencyCode)?.minorUnits ?? 2

  const line = initial?.lines[0]
  const [date, setDate] = useState(initial?.date ?? toIsoDate(new Date()))
  const [fromId, setFromId] = useState(initial?.cashAccountId ?? undefined)
  const [toId, setToId] = useState<string | undefined>(line?.accountId)
  const [amount, setAmount] = useState<number | null>(line?.debit ?? null)
  const [reference, setReference] = useState(initial?.reference ?? '')
  const [memo, setMemo] = useState(initial?.memo ?? '')
  const [issues, setIssues] = useState<ApiIssue[]>()
  const [find, setFind] = useState<'from' | 'to'>()

  const snapshot = () => JSON.stringify([date, fromId ?? '', toId ?? '', amount ?? 0, reference.trim(), memo.trim()])
  const [saved, setSaved] = useState(snapshot)
  const dirty = snapshot() !== saved
  useUnsavedWork(dirty)

  const input = (): VoucherInput => ({
    kind: 'Transfer',
    date,
    cashAccountId: fromId ?? null,
    reference: reference.trim() || null,
    memo: memo.trim() || null,
    lines: [{ id: line?.id ?? null, accountId: toId ?? '00000000-0000-0000-0000-000000000000', description: null, debit: amount ?? 0, credit: 0 }],
  })

  const save = useMutation({
    mutationFn: (post: boolean) => {
      const request = { id: initial?.id ?? null, input: input() }
      return post ? saveAndPostVoucher(request) : saveVoucherDraft(request)
    },
    onSuccess: async (response, post) => {
      setSaved(snapshot())
      await refreshBooks(queryClient)
      void message.success(post ? t('voucher.postedAs', { number: response.data.number }) : t('voucher.savedDraft'))
      navigate(listPath)
    },
    onError: (error) => {
      if (error instanceof ApiError && error.issues.length > 0) setIssues(error.issues)
      else void message.error(errorMessage(error, t))
    },
  })

  const remove = useMutation({
    mutationFn: () => deleteVoucher(initial!.id),
    onSuccess: async () => {
      setSaved(snapshot())
      await refreshBooks(queryClient)
      void message.success(t('voucher.deleted'))
      navigate(listPath)
    },
    onError: (error) => {
      if (error instanceof ApiError && error.issues.length > 0) setIssues(error.issues)
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

  const confirmDelete = () =>
    modal.confirm({
      title: t('transfer.deleteTitle', { number: initial?.number ?? t('voucher.draft') }),
      content: initial?.status === 'Posted' ? t('voucher.deleteBody') : t('transfer.deleteDraftBody'),
      okText: t('voucher.delete'),
      okButtonProps: { danger: true },
      cancelText: t('common.cancel'),
      onOk: () => remove.mutateAsync().catch(() => undefined),
    })

  const cancel = () => {
    if (!dirty) return navigate(listPath)
    modal.confirm({
      title: t('voucher.discardTitle'),
      content: t('voucher.discardBody'),
      okText: t('voucher.discard'),
      okButtonProps: { danger: true },
      cancelText: t('voucher.keepEditing'),
      onOk: () => {
        setSaved(snapshot())
        navigate(listPath)
      },
    })
  }

  const busy = save.isPending || remove.isPending
  useShortcuts({
    'ctrl+s': () => !busy && save.mutate(true),
    'ctrl+p': () => initial && !dirty && host.data?.pdfPrinting && print.mutate(),
    f2: () => setFind(document.activeElement?.closest('[data-field="to"]') ? 'to' : 'from'),
  })

  const problem = (field: string) => {
    const issue = issues?.find((i) => i.field === field)
    return issue ? t(`voucher.issues.${issue.code}`, { line: 1, defaultValue: issue.code }) : undefined
  }
  const title = initial?.number ? `${t('voucher.title.Transfer')} ${initial.number}` : initial ? `${t('voucher.title.Transfer')} (${t('voucher.draft')})` : t('voucher.new.Transfer')

  return (
    <FormPage
      title={title}
      help="transfer"
      crumbs={[
        { label: t('breadcrumb.home'), to: '/' },
        { label: t('voucher.plural.Transfer'), to: listPath },
        { label: initial ? (initial.number ?? t('voucher.draft')) : t('voucher.newShort') },
      ]}
      save={{ label: t('voucher.save'), onClick: () => save.mutate(true), loading: save.isPending && save.variables === true, disabled: busy }}
      saveAsDraft={{ label: t('voucher.saveAsDraft'), onClick: () => save.mutate(false), loading: save.isPending && save.variables === false, disabled: busy }}
      print={{
        label: t('voucher.print'),
        onClick: () => print.mutate(),
        loading: print.isPending,
        disabled: !initial || dirty || !host.data?.pdfPrinting,
        title: !initial || dirty ? t('voucher.printSaveFirst') : host.data?.pdfPrinting ? undefined : t('export.pdfUnavailable'),
      }}
      cancel={{ label: t('common.cancel'), onClick: cancel, disabled: busy }}
      remove={initial ? { label: t('voucher.delete'), onClick: confirmDelete, loading: remove.isPending, disabled: busy } : undefined}
    >
      {issues && issues.length > 0 && (
        <Alert
          type="error"
          showIcon
          className="form-alert"
          message={t('voucher.cannotSave')}
          description={
            <ul className="issue-list">
              {issues.map((issue, i) => (
                <li key={i}>{t(`voucher.issues.${issue.code}`, { line: 1, defaultValue: issue.code })}</li>
              ))}
            </ul>
          }
        />
      )}

      <div className="voucher-header">
        <div className="field">
          <label htmlFor="transfer-date">{t('voucher.date')}</label>
          <DateField id="transfer-date" value={date} onChange={(v) => v && setDate(v)} status={problem('date') ? 'error' : undefined} ariaLabel={t('voucher.date')} />
          {problem('date') && <div className="cell-error">{problem('date')}</div>}
        </div>
        <div className="field field-wide" data-field="from">
          <label htmlFor="transfer-from">{t('transfer.from')}</label>
          <AccountSelect
            id="transfer-from"
            value={fromId}
            onChange={(id) => {
              setFromId(id)
              setIssues(undefined)
            }}
            accounts={accounts}
            cashOnly
            status={problem('cashAccount') ? 'error' : undefined}
            placeholder={t('voucher.cashPlaceholder')}
            ariaLabel={t('transfer.from')}
          />
          {problem('cashAccount') && <div className="cell-error">{problem('cashAccount')}</div>}
        </div>
        <div className="field field-wide" data-field="to">
          <label htmlFor="transfer-to">{t('transfer.to')}</label>
          <AccountSelect
            id="transfer-to"
            value={toId}
            onChange={(id) => {
              setToId(id)
              setIssues(undefined)
            }}
            accounts={accounts}
            cashOnly
            status={problem('lines[0].account') ? 'error' : undefined}
            placeholder={t('voucher.cashPlaceholder')}
            ariaLabel={t('transfer.to')}
          />
          {problem('lines[0].account') && <div className="cell-error">{problem('lines[0].account')}</div>}
        </div>
      </div>

      <div className="voucher-header">
        <div className="field">
          <label htmlFor="transfer-amount">{t('accounting.amount')}</label>
          <InputNumber
            id="transfer-amount"
            value={amount}
            onChange={(v) => {
              setAmount(typeof v === 'number' ? v : null)
              setIssues(undefined)
            }}
            min={0}
            precision={minorUnits}
            controls={false}
            className="amount-input"
            status={problem('lines[0].amount') ? 'error' : undefined}
            aria-label={t('accounting.amount')}
          />
          {problem('lines[0].amount') && <div className="cell-error">{problem('lines[0].amount')}</div>}
        </div>
        <div className="field">
          <label htmlFor="transfer-reference">{t('voucher.reference')}</label>
          <Input id="transfer-reference" value={reference} onChange={(e) => setReference(e.target.value)} maxLength={100} dir="ltr" />
        </div>
      </div>

      <div className="field field-full">
        <label htmlFor="transfer-memo">{t('voucher.notes')}</label>
        <Input.TextArea id="transfer-memo" value={memo} onChange={(e) => setMemo(e.target.value)} rows={2} maxLength={500} />
      </div>

      <AccountSearchDialog
        open={find !== undefined}
        accounts={accounts}
        cashOnly
        onPick={(id) => {
          if (find === 'to') setToId(id)
          else setFromId(id)
          setFind(undefined)
        }}
        onClose={() => setFind(undefined)}
      />
    </FormPage>
  )
}
