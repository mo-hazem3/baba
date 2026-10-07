import type { ApiIssue } from '../../api/http'
import type { DocumentDto, DocumentInput, DocumentKind, DocumentLineInput } from '../../api/generated/model'

export type Side = 'sales' | 'purchases'

/** The kinds of document on each side, in the order a deal goes through them. */
export const kindsOf: Record<Side, DocumentKind[]> = {
  sales: ['Quote', 'SalesOrder', 'DeliveryNote', 'SalesInvoice', 'SalesCreditNote'],
  purchases: ['PurchaseOrder', 'GoodsReceipt', 'PurchaseInvoice', 'PurchaseDebitNote'],
}

export const sideOf = (kind: DocumentKind): Side => (kindsOf.sales.includes(kind) ? 'sales' : 'purchases')

/** What each kind is called in a web address. */
const segments: Record<DocumentKind, string> = {
  Quote: 'quote',
  SalesOrder: 'sales-order',
  DeliveryNote: 'delivery-note',
  SalesInvoice: 'sales-invoice',
  SalesCreditNote: 'credit-note',
  PurchaseOrder: 'purchase-order',
  GoodsReceipt: 'goods-receipt',
  PurchaseInvoice: 'purchase-invoice',
  PurchaseDebitNote: 'debit-note',
}

export const segmentOfKind = (kind: DocumentKind): string => segments[kind]

export const kindFromSegment = (segment: string | undefined): DocumentKind | undefined =>
  (Object.keys(segments) as DocumentKind[]).find((k) => segments[k] === segment)

export const listPathOfKind = (kind: DocumentKind): string => `/${sideOf(kind)}?kind=${segments[kind]}`

export const documentPath = (kind: DocumentKind, id: string): string => `/documents/${segments[kind]}/${id}`

/** Invoices and credit or debit notes post to the books; the others are only paper until they are turned into an invoice. */
export const posts = (kind: DocumentKind): boolean =>
  kind === 'SalesInvoice' || kind === 'SalesCreditNote' || kind === 'PurchaseInvoice' || kind === 'PurchaseDebitNote'

/** The documents one click turns this one into. Must match the backend (`ConvertibleTo`). */
export const convertibleTo = (kind: DocumentKind): DocumentKind[] => {
  switch (kind) {
    case 'Quote':
      return ['SalesOrder', 'SalesInvoice']
    case 'SalesOrder':
      return ['DeliveryNote', 'SalesInvoice']
    case 'DeliveryNote':
      return ['SalesInvoice']
    case 'SalesInvoice':
      return ['SalesCreditNote']
    case 'PurchaseOrder':
      return ['GoodsReceipt', 'PurchaseInvoice']
    case 'GoodsReceipt':
      return ['PurchaseInvoice']
    case 'PurchaseInvoice':
      return ['PurchaseDebitNote']
    default:
      return []
  }
}

/** One row of the lines table as the user sees it. */
export interface DocRow {
  key: string
  id?: string
  productId?: string
  accountId?: string
  costCenterId?: string
  description: string
  quantity: number | null
  unitPrice: number | null
  discountPercent: number | null
}

let counter = 0
export const newDocRow = (): DocRow => ({ key: `doc-row-${++counter}`, description: '', quantity: null, unitPrice: null, discountPercent: null })

export const rowsFromDocument = (document: DocumentDto): DocRow[] =>
  document.lines.map((l) => ({
    key: `doc-row-${++counter}`,
    id: l.id,
    productId: l.productId ?? undefined,
    accountId: l.accountId ?? undefined,
    costCenterId: l.costCenterId ?? undefined,
    description: l.description ?? '',
    quantity: l.quantity,
    unitPrice: l.unitPrice,
    discountPercent: l.discountPercent === 0 ? null : l.discountPercent,
  }))

export const isBlankRow = (row: DocRow): boolean =>
  !row.productId && !row.accountId && !row.description.trim() && !row.quantity && !row.unitPrice

export const rowsToSend = (rows: readonly DocRow[]): DocRow[] => rows.filter((r) => !isBlankRow(r))

/** Always one empty row at the end to type into. */
export const withTrailingBlankRow = (rows: readonly DocRow[]): DocRow[] => {
  const result = [...rows]
  while (result.length > 1 && isBlankRow(result.at(-1)!) && isBlankRow(result.at(-2)!)) result.pop()
  const last = result.at(-1)
  return last && isBlankRow(last) ? result : [...result, newDocRow()]
}

const round = (value: number, minorUnits: number): number => {
  const factor = 10 ** minorUnits
  return Math.round((value + Number.EPSILON * Math.sign(value)) * factor) / factor
}

export interface DocTotals {
  amounts: number[]
  subtotal: number
  discount: number
  total: number
}

/** Mirrors the backend rounding so the form shows exactly what will be posted: each line, then the document discount, then the total. */
export const computeTotals = (rows: readonly DocRow[], documentDiscountPercent: number, minorUnits: number): DocTotals => {
  const amounts = rows.map((r) => round((r.quantity ?? 0) * (r.unitPrice ?? 0) * (1 - (r.discountPercent ?? 0) / 100), minorUnits))
  const subtotal = round(amounts.reduce((sum, a) => sum + a, 0), minorUnits)
  const discount = round((subtotal * documentDiscountPercent) / 100, minorUnits)
  return { amounts, subtotal, discount, total: round(subtotal - discount, minorUnits) }
}

export interface DocHeader {
  date: string
  dueDate: string | null
  partyId?: string
  currencyCode: string
  exchangeRate: number | null
  reference: string
  memo: string
  discountPercent: number
}

export const toDocumentInput = (kind: DocumentKind, header: DocHeader, rows: readonly DocRow[], baseCurrencyCode: string): DocumentInput => ({
  kind,
  date: header.date,
  dueDate: header.dueDate,
  partyId: header.partyId ?? '',
  currencyCode: header.currencyCode === baseCurrencyCode ? null : header.currencyCode,
  exchangeRate: header.currencyCode === baseCurrencyCode ? null : header.exchangeRate,
  reference: header.reference.trim() || null,
  memo: header.memo.trim() || null,
  discountPercent: header.discountPercent,
  lines: rowsToSend(rows).map(
    (r): DocumentLineInput => ({
      id: r.id ?? null,
      productId: r.productId ?? null,
      accountId: r.accountId ?? null,
      description: r.description.trim() || null,
      quantity: r.quantity ?? 0,
      unitPrice: r.unitPrice ?? 0,
      discountPercent: r.discountPercent ?? 0,
      costCenterId: r.costCenterId ?? null,
    }),
  ),
})

export interface MappedDocIssues {
  header: Record<string, string>
  rows: Record<string, Record<string, string>>
  list: ApiIssue[]
}

/** Puts what the API reported on the field or line it is about: "lines[2].account" is the account of the third sent line. */
export const mapDocumentIssues = (issues: readonly ApiIssue[], sent: readonly DocRow[]): MappedDocIssues => {
  const result: MappedDocIssues = { header: {}, rows: {}, list: [...issues] }
  for (const issue of issues) {
    const match = /^lines\[(\d+)\]\.(\w+)$/.exec(issue.field)
    if (match) {
      const row = sent[Number(match[1])]
      if (row) {
        result.rows[row.key] = { ...result.rows[row.key], [match[2]!]: issue.code }
        continue
      }
    }
    result.header[issue.field] = issue.code
  }
  return result
}

/** The fingerprint of what the form holds, to tell whether there is unsaved work. */
export const fingerprintOf = (header: DocHeader, rows: readonly DocRow[]): string =>
  JSON.stringify([
    header.date,
    header.dueDate ?? '',
    header.partyId ?? '',
    header.currencyCode,
    header.exchangeRate ?? 0,
    header.reference.trim(),
    header.memo.trim(),
    header.discountPercent,
    rowsToSend(rows).map((r) => [r.productId ?? '', r.accountId ?? '', r.costCenterId ?? '', r.description.trim(), r.quantity ?? 0, r.unitPrice ?? 0, r.discountPercent ?? 0]),
  ])
