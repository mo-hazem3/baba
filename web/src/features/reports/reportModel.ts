import type { ReportCell, ReportLink, ReportResult, VoucherKind } from '../../api/generated/model'
import type { Language } from '../../settings/settings'
import { parseIsoDate, toIsoDate } from '../../utils/format'

export type RangePreset = 'thisMonth' | 'lastMonth' | 'thisYear' | 'lastYear' | 'all'

export interface DateRange {
  from?: string
  to?: string
}

const addDays = (date: Date, days: number) => new Date(date.getFullYear(), date.getMonth(), date.getDate() + days)

/** The usual choices for a report's dates. "Year" is the company's fiscal year, which may start in any month. */
export const presetRange = (preset: RangePreset, today: Date, fiscalYearStartMonth: number): DateRange => {
  const month = today.getMonth() + 1
  switch (preset) {
    case 'thisMonth': {
      return { from: toIsoDate(new Date(today.getFullYear(), today.getMonth(), 1)), to: toIsoDate(new Date(today.getFullYear(), today.getMonth() + 1, 0)) }
    }
    case 'lastMonth': {
      return { from: toIsoDate(new Date(today.getFullYear(), today.getMonth() - 1, 1)), to: toIsoDate(new Date(today.getFullYear(), today.getMonth(), 0)) }
    }
    case 'thisYear':
    case 'lastYear': {
      const startYear = (month >= fiscalYearStartMonth ? today.getFullYear() : today.getFullYear() - 1) - (preset === 'lastYear' ? 1 : 0)
      const start = new Date(startYear, fiscalYearStartMonth - 1, 1)
      return { from: toIsoDate(start), to: toIsoDate(addDays(new Date(startYear + 1, fiscalYearStartMonth - 1, 1), -1)) }
    }
    default:
      return {}
  }
}

/** Which preset (if any) a range equals, so the screen can show it as chosen. */
export const matchPreset = (range: DateRange, today: Date, fiscalYearStartMonth: number): RangePreset | undefined =>
  (['thisMonth', 'lastMonth', 'thisYear', 'lastYear', 'all'] as const).find((preset) => {
    const candidate = presetRange(preset, today, fiscalYearStartMonth)
    return candidate.from === range.from && candidate.to === range.to
  })

/** The text of a cell in the user's language: the Arabic variant in Arabic, the English text otherwise, whichever exists. */
export const cellText = (cell: ReportCell | undefined, language: Language): string =>
  (language === 'ar' ? cell?.textAr || cell?.text : cell?.text || cell?.textAr) ?? ''

/** The report's title and subtitle in the user's language. */
export const reportTitle = (report: Pick<ReportResult, 'titleEn' | 'titleAr'>, language: Language): string =>
  language === 'ar' ? report.titleAr : report.titleEn

export const reportSubtitle = (report: Pick<ReportResult, 'subtitleEn' | 'subtitleAr'>, language: Language): string =>
  language === 'ar' ? report.subtitleAr : report.subtitleEn

/**
 * Where clicking a report row goes (drill-down, brief section 11): an account's statement for the same dates, or the voucher.
 * Vouchers go through /vouchers/open/:id, which finds out the kind and sends you to the right form.
 */
export const drillPath = (link: ReportLink): string => {
  if (link.kind === 'voucher') return `/vouchers/open/${link.id}`
  if (link.kind === 'party' || link.kind === 'costCenter') {
    const dates = new URLSearchParams()
    if (link.from) dates.set('from', link.from)
    if (link.to) dates.set('to', link.to)
    const rest = dates.size > 0 ? `&${dates.toString()}` : ''
    return link.kind === 'party' ? `/reports/party-statement?partyId=${link.id}${rest}` : `/reports/profit-and-loss?costCenterId=${link.id}${rest}`
  }
  const query = new URLSearchParams({ accountId: link.id })
  if (link.from) query.set('from', link.from)
  if (link.to) query.set('to', link.to)
  return `/reports/statement-of-account?${query.toString()}`
}

const documentVoucherKinds: readonly VoucherKind[] = ['SalesInvoice', 'SalesCreditNote', 'PurchaseInvoice', 'PurchaseDebitNote']

/** Where a voucher opens. An invoice or note is shown as the document it was made from. */
export const voucherPath = (kind: VoucherKind, id: string): string =>
  documentVoucherKinds.includes(kind) ? `/vouchers/open/${id}` : `/vouchers/${kind.toLowerCase()}/${id}`

export const reportKeys = [
  'trial-balance',
  'profit-and-loss',
  'balance-sheet',
  'statement-of-account',
  'general-ledger',
  'journal',
  'party-statement',
  'aging-receivable',
  'aging-payable',
  'cost-centers',
  'tax-return',
  'stock-valuation',
  'stock-movements',
  'stock-reorder',
  'asset-register',
] as const
export type ReportKey = (typeof reportKeys)[number]

/** What a report asks for: a date range, an "as of" date, an account, a comparison with last year. */
export const reportInputs: Record<
  ReportKey,
  { range: boolean; asOf: boolean; account: boolean; comparison: boolean; party: boolean; costCenter: boolean; warehouse?: boolean; product?: boolean }
> = {
  'trial-balance': { range: true, asOf: false, account: false, comparison: false, party: false, costCenter: false },
  'profit-and-loss': { range: true, asOf: false, account: false, comparison: true, party: false, costCenter: true },
  'balance-sheet': { range: false, asOf: true, account: false, comparison: true, party: false, costCenter: false },
  'statement-of-account': { range: true, asOf: false, account: true, comparison: false, party: false, costCenter: false },
  'general-ledger': { range: true, asOf: false, account: false, comparison: false, party: false, costCenter: false },
  journal: { range: true, asOf: false, account: false, comparison: false, party: false, costCenter: false },
  'party-statement': { range: true, asOf: false, account: false, comparison: false, party: true, costCenter: false },
  'aging-receivable': { range: false, asOf: true, account: false, comparison: false, party: false, costCenter: false },
  'aging-payable': { range: false, asOf: true, account: false, comparison: false, party: false, costCenter: false },
  'cost-centers': { range: true, asOf: false, account: false, comparison: false, party: false, costCenter: false },
  'tax-return': { range: true, asOf: false, account: false, comparison: false, party: false, costCenter: false },
  'stock-valuation': { range: false, asOf: true, account: false, comparison: false, party: false, costCenter: false, warehouse: true },
  'stock-movements': { range: true, asOf: false, account: false, comparison: false, party: false, costCenter: false, warehouse: true, product: true },
  'stock-reorder': { range: false, asOf: false, account: false, comparison: false, party: false, costCenter: false },
  'asset-register': { range: false, asOf: true, account: false, comparison: false, party: false, costCenter: false },
}

/** The optional module a report belongs to. Reports of a module that is switched off are not offered on the Reports page. */
export const reportModule: Partial<Record<ReportKey, string>> = {
  'party-statement': 'customers-suppliers',
  'aging-receivable': 'customers-suppliers',
  'aging-payable': 'customers-suppliers',
  'cost-centers': 'cost-centers',
  'stock-valuation': 'inventory',
  'stock-movements': 'inventory',
  'stock-reorder': 'inventory',
  'asset-register': 'fixed-assets',
}

/** Reports that exist only where the country's pack supports them (the capability, never the country). */
export const reportNeedsTaxReturn = (key: ReportKey): boolean => key === 'tax-return'

export const isReportKey = (key: string | undefined): key is ReportKey => reportKeys.includes(key as ReportKey)

export { parseIsoDate }
