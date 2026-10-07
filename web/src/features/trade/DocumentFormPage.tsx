import { Alert, App, Button, Input, InputNumber, Select, Space, Spin, Tag } from 'antd'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { RecurringModal } from '../recurring/RecurringModal'
import { useTranslation } from 'react-i18next'
import { Navigate, useNavigate, useParams } from 'react-router'
import { convertDocument, deleteDocument, getDocument, getProductPrice, issueDocument, listOutstandingInvoices, printDocument, saveDocumentDraft } from '../../api/generated/baba'
import type { DocumentDto, DocumentKind, ProductDto, TaxCodeDto } from '../../api/generated/model'
import { ApiError, asBlob } from '../../api/http'
import { refreshBooks, useAccounts, useCostCenters, useCurrencies, useCurrentCompany, useHost, useModules, useParties, usePrintSettings, useProducts, useTaxCodes } from '../../api/hooks'
import { AmountText } from '../../layout/AmountText'
import { DateField } from '../../layout/DateField'
import { errorMessage } from '../../layout/errors'
import { FormPage } from '../../layout/FormPage'
import { useShortcuts } from '../../layout/useShortcuts'
import { useUnsavedWork } from '../../layout/useUnsavedWork'
import { useSettings } from '../../settings/SettingsContext'
import { openPdf } from '../../utils/download'
import { addDays, parseIsoDate, toIsoDate } from '../../utils/format'
import { AccountSelect } from '../accounting/AccountSelect'
import { CurrencyFields } from '../accounting/CurrencyFields'
import { CostCenterSelect, itemName, PartySelect } from '../accounting/LookupSelects'
import {
  computeTotals,
  convertibleTo,
  documentPath,
  fingerprintOf,
  isBlankRow,
  kindFromSegment,
  listPathOfKind,
  mapDocumentIssues,
  newDocRow,
  posts,
  rowsFromDocument,
  rowsToSend,
  sideOf,
  toDocumentInput,
  withTrailingBlankRow,
  type DocHeader,
  type DocRow,
  type MappedDocIssues,
} from './documentModel'

/** /documents/:segment/new and /documents/:segment/:id. Loads the document (if there is one) before showing the form. */
export function DocumentFormPage() {
  const { segment, id } = useParams()
  const kind = kindFromSegment(segment)
  const documentId = id && id !== 'new' ? id : undefined

  const query = useQuery({
    queryKey: ['/api/documents', documentId],
    enabled: documentId !== undefined,
    queryFn: async () => {
      const response = await getDocument(documentId!)
      return response.status === 200 ? response.data : null
    },
  })

  if (!kind) return <Navigate to="/" replace />
  if (documentId && query.isPending) return <Spin size="large" className="page-spinner" />
  if (documentId && query.data === null) return <Navigate to={listPathOfKind(kind)} replace />

  // A new form for each document, so nothing typed in one leaks into the next.
  return <DocumentForm key={`${kind}-${documentId ?? 'new'}-${query.data?.status ?? ''}`} kind={kind} initial={query.data ?? undefined} />
}

/** /documents/open/:id. A report row or a voucher only knows a document's id, so this finds its kind and goes to the right form. */
export function DocumentOpenPage() {
  const { id } = useParams()
  const query = useQuery({
    queryKey: ['/api/documents', 'open', id],
    enabled: id !== undefined,
    queryFn: async () => {
      const response = await getDocument(id!)
      return response.status === 200 ? response.data : null
    },
  })
  if (query.isPending) return <Spin size="large" className="page-spinner" />
  return query.data ? <Navigate to={documentPath(query.data.kind, query.data.id)} replace /> : <Navigate to="/" replace />
}

function DocumentForm({ kind, initial }: { kind: DocumentKind; initial?: DocumentDto }) {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const { message, modal } = App.useApp()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const company = useCurrentCompany()
  const currencies = useCurrencies()
  const modules = useModules()
  const accounts = useAccounts().data ?? []
  const parties = useParties().data ?? []
  const products = useProducts().data ?? []
  const costCenters = useCostCenters().data ?? []
  const taxCodes = useTaxCodes().data ?? []
  const host = useHost()
  const printSettings = usePrintSettings()

  const sales = sideOf(kind) === 'sales'
  const baseCurrencyCode = company.data?.baseCurrencyCode ?? ''
  const listPath = listPathOfKind(kind)
  const issued = initial !== undefined && initial.status !== 'Draft'
  const converted = initial?.status === 'Converted'
  const isInvoice = kind === 'SalesInvoice' || kind === 'PurchaseInvoice'

  // An invoice that has been paid or credited, even in part, is fixed: its payment or credit note has to go first.
  const standing = useQuery({
    queryKey: ['/api/settlements/outstanding', initial?.partyId],
    enabled: isInvoice && initial?.status === 'Issued',
    queryFn: async () => (await listOutstandingInvoices({ partyId: initial!.partyId })).data,
  })
  const outstanding = standing.data?.find((o) => o.documentId === initial?.id)?.outstanding
  const settled = outstanding !== undefined && initial !== undefined && outstanding < initial.total
  const readOnly = converted || settled

  const [partyId, setPartyId] = useState<string | undefined>(initial?.partyId)
  const [date, setDate] = useState(initial?.date ?? toIsoDate(new Date()))
  const [dueDate, setDueDate] = useState<string | null>(initial?.dueDate ?? null)
  const [reference, setReference] = useState(initial?.reference ?? '')
  const [memo, setMemo] = useState(initial?.memo ?? '')
  const [discountPercent, setDiscountPercent] = useState(initial?.discountPercent ?? 0)
  const [currencyCode, setCurrencyCode] = useState(initial?.currencyCode ?? baseCurrencyCode)
  const [rate, setRate] = useState<number | null>(initial && initial.currencyCode !== baseCurrencyCode ? initial.exchangeRate : null)
  const [rows, setRows] = useState<DocRow[]>(() => withTrailingBlankRow(initial ? rowsFromDocument(initial) : [newDocRow()]))
  const [issues, setIssues] = useState<MappedDocIssues>()
  const [repeating, setRepeating] = useState(false)

  const minorUnits = currencies.data?.find((c) => c.code === currencyCode)?.minorUnits ?? 2
  const header = (): DocHeader => ({ date, dueDate, partyId, currencyCode, exchangeRate: rate, reference, memo, discountPercent })
  const current = () => fingerprintOf(header(), rows)
  const [savedFingerprint, setSavedFingerprint] = useState(current)
  const dirty = current() !== savedFingerprint
  useUnsavedWork(dirty)

  const sent = useMemo(() => rowsToSend(rows), [rows])
  const totals = computeTotals(sent, discountPercent, minorUnits)
  const input = () => toDocumentInput(kind, header(), rows, baseCurrencyCode)
  const party = parties.find((p) => p.id === partyId)

  const choosePartyId = (id: string | undefined) => {
    setPartyId(id)
    // The due date follows the customer's or supplier's terms until the person types one.
    const terms = parties.find((p) => p.id === id)?.paymentTermsDays
    if (posts(kind) && terms !== undefined && !issued) setDueDate(toIsoDate(addDays(parseIsoDate(date), terms)))
    setIssues(undefined)
  }

  /** A tax code that can be put on a line dated on the document's date, or the one it already has. */
  const usableCodes = (current?: string): TaxCodeDto[] =>
    taxCodes.filter(
      (c) => c.id === current || (c.isActive && (c.effectiveFrom === null || c.effectiveFrom <= date) && (c.effectiveTo === null || date <= c.effectiveTo)),
    )
  const defaultCode = usableCodes().find((c) => c.isDefault)

  const update = (key: string, patch: Partial<DocRow>) => {
    // A line that is first touched starts with the company's default tax code.
    setRows((all) =>
      withTrailingBlankRow(
        all.map((r) =>
          r.key === key
            ? { ...r, ...(isBlankRow(r) && !r.taxCodeId && defaultCode && !('taxCodeId' in patch) ? { taxCodeId: defaultCode.id, taxRate: defaultCode.rate } : {}), ...patch }
            : r,
        ),
      ),
    )
    setIssues(undefined)
  }

  /** Choosing a product fills in its price (the customer's price list when there is one), name and account. */
  /** The product's tax code if it has a usable one, otherwise the company's default. */
  const taxFor = (productCodeId: string | null | undefined) => {
    const code = usableCodes().find((c) => c.id === productCodeId) ?? defaultCode
    return code ? { taxCodeId: code.id, taxRate: code.rate } : {}
  }

  const chooseProduct = async (row: DocRow, product: ProductDto | undefined) => {
    if (!product) return update(row.key, { productId: undefined })
    update(row.key, { productId: product.id })
    const response = await getProductPrice({
      productId: product.id,
      partyId,
      currency: currencyCode === baseCurrencyCode ? undefined : currencyCode,
      rate: rate ?? undefined,
      sale: sales,
    })
    if (response.status !== 200) return
    const price = response.data
    setRows((all) =>
      withTrailingBlankRow(
        all.map((r) =>
          r.key === row.key
            ? {
                ...r,
                productId: product.id,
                unitPrice: price.price,
                quantity: r.quantity ?? 1,
                accountId: r.accountId ?? price.accountId ?? undefined,
                ...taxFor(price.taxCodeId),
                description: r.description || itemName(price, settings.language),
              }
            : r,
        ),
      ),
    )
  }

  const finish = async (text: string) => {
    setSavedFingerprint(current())
    await refreshBooks(queryClient)
    void message.success(text)
    navigate(listPath)
  }

  const onError = (error: unknown) => {
    if (error instanceof ApiError && error.issues.length > 0) setIssues(mapDocumentIssues(error.issues, sent))
    else void message.error(errorMessage(error, t))
  }

  const save = useMutation({
    mutationFn: (issue: boolean) => {
      const request = { id: initial?.id ?? null, input: input() }
      return issue ? issueDocument(request) : saveDocumentDraft(request)
    },
    onSuccess: (response, issue) =>
      finish(issue ? (issued ? t('trade.saved') : t('trade.issuedAs', { number: response.data.number })) : t('trade.savedDraft')),
    onError,
  })

  const convert = useMutation({
    mutationFn: (target: DocumentKind) => convertDocument(initial!.id, { target }),
    onSuccess: async (response) => {
      await refreshBooks(queryClient)
      void message.success(t('trade.converted', { kind: t(`trade.title.${response.data.kind}`) }))
      navigate(documentPath(response.data.kind, response.data.id))
    },
    onError,
  })

  const print = useMutation({
    mutationFn: async () => {
      const response = await printDocument(initial!.id, { layout: printSettings.data?.defaultLayout })
      if (response.status !== 200) throw new ApiError(response.status, 'NotImplemented', 'This host cannot make PDFs.')
      openPdf(asBlob(response.data))
    },
    onError: (error) => void message.error(error instanceof ApiError && error.status === 501 ? t('export.pdfUnavailable') : errorMessage(error, t)),
  })

  const remove = useMutation({
    mutationFn: () => deleteDocument(initial!.id),
    onSuccess: async () => {
      setSavedFingerprint(current())
      await refreshBooks(queryClient)
      navigate(listPath)
      void message.success(t('trade.deleted'))
    },
    onError,
  })

  const confirmDelete = () => {
    if (!initial) return
    if (initial.status === 'Draft') return remove.mutate()
    modal.confirm({
      title: t('trade.deleteTitle', { kind: t(`trade.title.${kind}`), number: initial.number }),
      content: posts(kind) ? t('trade.deletePostedBody') : t('trade.deleteBody'),
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

  const busy = save.isPending || remove.isPending || convert.isPending
  useShortcuts({
    'ctrl+s': () => !busy && !readOnly && save.mutate(true),
    'ctrl+p': () => initial && !dirty && host.data?.pdfPrinting && print.mutate(),
  })

  const issueText = (code: string, line?: number) => t([`trade.issues.${code}`, `voucher.issues.${code}`], { line, defaultValue: code })
  const rowProblem = (key: string, field: string) => {
    const code = issues?.rows[key]?.[field]
    return code ? issueText(code) : undefined
  }
  const title = initial?.number ? `${t(`trade.title.${kind}`)} ${initial.number}` : initial ? `${t(`trade.title.${kind}`)} (${t('trade.status.Draft')})` : t(`trade.new.${kind}`)
  const headerProblem = (field: string) => (issues?.header[field] ? issueText(issues.header[field]) : undefined)
  const accountChoices = accounts.filter((a) => (sales ? a.type === 'Revenue' : a.type === 'Expense' || a.type === 'Asset'))
  const showCostCenter = modules.has('cost-centers') || rows.some((r) => r.costCenterId)
  // The tax column is offered when the country has tax codes, and always when a line already carries one.
  const showTax = taxCodes.length > 0 || rows.some((r) => r.taxCodeId)

  const statusTag =
    initial &&
    (initial.status === 'Draft' ? (
      <Tag>{t('trade.status.Draft')}</Tag>
    ) : initial.status === 'Converted' ? (
      <Tag color="gold">{t('trade.status.Converted')}</Tag>
    ) : (
      <Tag color="blue">{t('trade.status.Issued')}</Tag>
    ))

  return (
    <FormPage
      title={title}
      help="documents"
      crumbs={[
        { label: t('breadcrumb.home'), to: '/' },
        { label: t(`trade.side.${sideOf(kind)}`), to: listPath },
        { label: initial ? (initial.number ?? t('trade.status.Draft')) : t('voucher.newShort') },
      ]}
      badge={statusTag}
      save={
        readOnly
          ? undefined
          : {
              label: issued ? t('trade.save') : t(posts(kind) ? 'trade.issueInvoice' : 'trade.issue'),
              onClick: () => save.mutate(true),
              loading: save.isPending && save.variables === true,
              disabled: busy,
            }
      }
      saveAsDraft={
        readOnly || issued ? undefined : { label: t('voucher.saveAsDraft'), onClick: () => save.mutate(false), loading: save.isPending && save.variables === false, disabled: busy }
      }
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
      {converted && <Alert type="info" showIcon className="form-alert" message={t('trade.convertedNote')} />}
      {settled && <Alert type="info" showIcon className="form-alert" message={t('trade.settledNote')} />}
      {issued && !converted && posts(kind) && <Alert type="info" showIcon className="form-alert" message={t('trade.postedNote')} />}
      {issues && issues.list.length > 0 && (
        <Alert
          type="error"
          showIcon
          className="form-alert"
          message={t('trade.cannotSave')}
          description={
            <ul className="issue-list">
              {issues.list.map((issue, i) => (
                <li key={i}>{issueText(issue.code)}</li>
              ))}
            </ul>
          }
        />
      )}

      {initial && !converted && (
        <div className="doc-actions">
          <span className="muted">{t('recurring.repeatHelp')}</span>
          <Button onClick={() => setRepeating(true)} disabled={dirty} title={dirty ? t('trade.saveFirst') : undefined}>
            {t('recurring.repeat')}
          </Button>
        </div>
      )}
      {repeating && initial && (
        <RecurringModal
          open
          source={{ type: 'document', id: initial.id, name: `${t(`trade.title.${kind}`)} — ${parties.find((p) => p.id === initial.partyId)?.nameEn ?? ''}` }}
          onClose={() => setRepeating(false)}
          onSaved={() => void message.success(t('recurring.saved'))}
        />
      )}
      {isInvoice && initial?.status === 'Issued' && outstanding !== undefined && (
        <div className="doc-actions">
          <span>
            {t('trade.outstanding')}: <AmountText value={outstanding} minorUnits={minorUnits} /> {currencyCode !== baseCurrencyCode && currencyCode}
          </span>
          {outstanding > 0 ? (
            <Button type="primary" onClick={() => navigate(`/settlements/${sales ? 'receive' : 'pay'}?partyId=${initial.partyId}&documentId=${initial.id}`)} disabled={dirty} title={dirty ? t('trade.saveFirst') : undefined}>
              {t(sales ? 'trade.receivePayment' : 'trade.payInvoices')}
            </Button>
          ) : (
            <Tag color="green">{t('trade.paid')}</Tag>
          )}
        </div>
      )}

      {initial?.status === 'Issued' && convertibleTo(kind).length > 0 && (
        <div className="doc-actions">
          <span className="muted">{t('trade.convertTo')}</span>
          <Space wrap>
            {convertibleTo(kind).map((target) => (
              <Button key={target} onClick={() => convert.mutate(target)} loading={convert.isPending && convert.variables === target} disabled={busy || dirty} title={dirty ? t('trade.saveFirst') : undefined}>
                {t(`trade.title.${target}`)}
              </Button>
            ))}
          </Space>
        </div>
      )}

      <div className="voucher-header">
        <div className="field field-wide">
          <label htmlFor="doc-party">{sales ? t('trade.customer') : t('trade.supplier')}</label>
          <PartySelect
            id="doc-party"
            value={partyId}
            parties={parties}
            kind={sales ? 'Customer' : 'Supplier'}
            onChange={choosePartyId}
            status={issues?.header.party ? 'error' : undefined}
            placeholder={sales ? t('accounting.chooseCustomer') : t('accounting.chooseSupplier')}
            ariaLabel={sales ? t('trade.customer') : t('trade.supplier')}
            allowClear={false}
            disabled={readOnly}
          />
          {headerProblem('party') && <div className="cell-error">{headerProblem('party')}</div>}
        </div>
        <div className="field">
          <label htmlFor="doc-date">{t('voucher.date')}</label>
          <DateField id="doc-date" value={date} onChange={(v) => v && setDate(v)} status={issues?.header.date ? 'error' : undefined} ariaLabel={t('voucher.date')} />
          {headerProblem('date') && <div className="cell-error">{headerProblem('date')}</div>}
        </div>
        {posts(kind) && (
          <div className="field">
            <label htmlFor="doc-due">{t('trade.dueDate')}</label>
            <DateField id="doc-due" value={dueDate ?? date} onChange={(v) => v && setDueDate(v)} ariaLabel={t('trade.dueDate')} />
          </div>
        )}
        <CurrencyFields
          date={date}
          currencyCode={currencyCode}
          rate={rate}
          disabled={readOnly || issued}
          rateStatus={issues?.header.exchangeRate ? 'error' : undefined}
          onChange={(change) => {
            setCurrencyCode(change.currencyCode)
            setRate(change.rate)
          }}
        />
        {headerProblem('exchangeRate') && <div className="cell-error">{headerProblem('exchangeRate')}</div>}
        <div className="field">
          <label htmlFor="doc-reference">{t('voucher.reference')}</label>
          <Input id="doc-reference" value={reference} onChange={(e) => setReference(e.target.value)} maxLength={100} dir="ltr" disabled={readOnly} />
        </div>
      </div>

      <div className="lines-grid-wrap">
        <table className="lines-grid doc-lines">
          <thead>
            <tr>
              <th className="col-number">#</th>
              <th>{t('trade.product')}</th>
              <th className="col-description">{t('accounting.description')}</th>
              <th>{sales ? t('trade.revenueAccount') : t('trade.expenseAccount')}</th>
              {showCostCenter && <th>{t('accounting.costCenter')}</th>}
              <th className="num">{t('trade.quantity')}</th>
              <th className="num">{t('trade.unitPrice')}</th>
              <th className="num">{t('trade.discountPercent')}</th>
              {showTax && <th>{t('trade.taxCode')}</th>}
              <th className="num">{t('accounting.amount')}</th>
              <th className="col-actions">
                <span className="visually-hidden">{t('voucher.rowActions')}</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row, index) => {
              const sentIndex = sent.findIndex((r) => r.key === row.key)
              return (
                <tr key={row.key}>
                  <td className="col-number">{index + 1}</td>
                  <td>
                    <Select
                      value={row.productId}
                      allowClear
                      showSearch
                      optionFilterProp="label"
                      popupMatchSelectWidth={false}
                      className="account-select"
                      disabled={readOnly}
                      aria-label={`${t('trade.product')} ${index + 1}`}
                      status={rowProblem(row.key, 'product') ? 'error' : undefined}
                      options={products.filter((p) => p.isActive || p.id === row.productId).map((p) => ({ value: p.id, label: `${p.code} — ${itemName(p, settings.language)}` }))}
                      onChange={(id: string | undefined) => void chooseProduct(row, products.find((p) => p.id === id))}
                    />
                    {rowProblem(row.key, 'product') && <div className="cell-error">{rowProblem(row.key, 'product')}</div>}
                  </td>
                  <td className="col-description">
                    <Input value={row.description} onChange={(e) => update(row.key, { description: e.target.value })} maxLength={200} aria-label={`${t('accounting.description')} ${index + 1}`} disabled={readOnly} />
                  </td>
                  <td>
                    <AccountSelect
                      value={row.accountId}
                      accounts={accountChoices}
                      onChange={(accountId) => update(row.key, { accountId })}
                      status={rowProblem(row.key, 'account') ? 'error' : undefined}
                      ariaLabel={`${t('trade.account')} ${index + 1}`}
                      disabled={readOnly}
                    />
                    {rowProblem(row.key, 'account') && <div className="cell-error">{rowProblem(row.key, 'account')}</div>}
                  </td>
                  {showCostCenter && (
                    <td>
                      <CostCenterSelect
                        value={row.costCenterId}
                        costCenters={costCenters}
                        onChange={(costCenterId) => update(row.key, { costCenterId })}
                        ariaLabel={`${t('accounting.costCenter')} ${index + 1}`}
                        disabled={readOnly}
                      />
                    </td>
                  )}
                  <td className="num">
                    <InputNumber value={row.quantity} min={0} precision={4} controls={false} className="amount-input" disabled={readOnly} status={rowProblem(row.key, 'quantity') ? 'error' : undefined} aria-label={`${t('trade.quantity')} ${index + 1}`} onChange={(v) => update(row.key, { quantity: typeof v === 'number' ? v : null })} />
                    {rowProblem(row.key, 'quantity') && <div className="cell-error">{rowProblem(row.key, 'quantity')}</div>}
                  </td>
                  <td className="num">
                    <InputNumber value={row.unitPrice} min={0} precision={Math.max(minorUnits, 2)} controls={false} className="amount-input" disabled={readOnly} status={rowProblem(row.key, 'price') ? 'error' : undefined} aria-label={`${t('trade.unitPrice')} ${index + 1}`} onChange={(v) => update(row.key, { unitPrice: typeof v === 'number' ? v : null })} />
                    {rowProblem(row.key, 'price') && <div className="cell-error">{rowProblem(row.key, 'price')}</div>}
                  </td>
                  <td className="num">
                    <InputNumber value={row.discountPercent} min={0} max={100} precision={2} controls={false} className="amount-input" disabled={readOnly} status={rowProblem(row.key, 'discount') ? 'error' : undefined} aria-label={`${t('trade.discountPercent')} ${index + 1}`} onChange={(v) => update(row.key, { discountPercent: typeof v === 'number' ? v : null })} />
                  </td>
                  {showTax && (
                    <td>
                      <Select
                        value={row.taxCodeId}
                        allowClear
                        popupMatchSelectWidth={false}
                        className="tax-select"
                        disabled={readOnly}
                        aria-label={`${t('trade.taxCode')} ${index + 1}`}
                        status={rowProblem(row.key, 'taxCode') ? 'error' : undefined}
                        options={usableCodes(row.taxCodeId).map((c) => ({ value: c.id, label: `${c.code} (${c.rate}%)` }))}
                        onChange={(id: string | undefined) => update(row.key, { taxCodeId: id, taxRate: taxCodes.find((c) => c.id === id)?.rate ?? 0 })}
                      />
                      {rowProblem(row.key, 'taxCode') && <div className="cell-error">{rowProblem(row.key, 'taxCode')}</div>}
                      {sentIndex >= 0 && row.taxCodeId && totals.taxes[sentIndex] !== undefined && (
                        <div className="muted">
                          <AmountText value={totals.taxes[sentIndex] ?? 0} minorUnits={minorUnits} />
                        </div>
                      )}
                    </td>
                  )}
                  <td className="num">{sentIndex >= 0 && <AmountText value={totals.amounts[sentIndex] ?? 0} minorUnits={minorUnits} />}</td>
                  <td className="col-actions">
                    {!readOnly && sent.some((r) => r.key === row.key) && (
                      <Button type="link" danger size="small" onClick={() => setRows((all) => withTrailingBlankRow(all.filter((r) => r.key !== row.key)))}>
                        {t('voucher.removeRow')}
                      </Button>
                    )}
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>
      {headerProblem('lines') && <div className="cell-error">{headerProblem('lines')}</div>}

      <div className="doc-totals" aria-live="polite">
        <div>
          <span>{t('trade.subtotal')}</span>
          <AmountText value={totals.subtotal} minorUnits={minorUnits} />
        </div>
        <div className="doc-discount">
          <label htmlFor="doc-discount">{t('trade.documentDiscount')}</label>
          <InputNumber id="doc-discount" value={discountPercent} min={0} max={100} precision={2} controls={false} className="amount-input" disabled={readOnly} addonAfter="%" onChange={(v) => setDiscountPercent(typeof v === 'number' ? v : 0)} />
          <AmountText value={-totals.discount} minorUnits={minorUnits} />
        </div>
        {showTax && (
          <>
            <div>
              <span>{t('trade.netTotal')}</span>
              <AmountText value={totals.net} minorUnits={minorUnits} />
            </div>
            <div>
              <span>{t('trade.taxTotal')}</span>
              <AmountText value={totals.taxTotal} minorUnits={minorUnits} />
            </div>
          </>
        )}
        <strong>
          {t('voucher.total')} ({currencyCode}): <AmountText value={totals.total} minorUnits={minorUnits} />
        </strong>
        {currencyCode !== baseCurrencyCode && rate !== null && (
          <span className="muted">
            ≈ <AmountText value={Math.round(totals.total * rate * 10 ** 4) / 10 ** 4} minorUnits={currencies.data?.find((c) => c.code === baseCurrencyCode)?.minorUnits ?? 2} /> {baseCurrencyCode}
          </span>
        )}
        {party && posts(kind) && party.creditLimit > 0 && sales && <span className="muted">{t('trade.creditLimitNote', { limit: party.creditLimit })}</span>}
      </div>

      <div className="field field-full">
        <label htmlFor="doc-memo">{t('voucher.notes')}</label>
        <Input.TextArea id="doc-memo" value={memo} onChange={(e) => setMemo(e.target.value)} rows={2} maxLength={500} disabled={readOnly} />
      </div>
    </FormPage>
  )
}
