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
  const query = new URLSearchParams({ accountId: link.id })
  if (link.from) query.set('from', link.from)
  if (link.to) query.set('to', link.to)
  return `/reports/statement-of-account?${query.toString()}`
}

export const voucherPath = (kind: VoucherKind, id: string): string => `/vouchers/${kind.toLowerCase()}/${id}`

export const reportKeys = ['trial-balance', 'profit-and-loss', 'balance-sheet', 'statement-of-account', 'general-ledger', 'journal'] as const
export type ReportKey = (typeof reportKeys)[number]

/** What a report asks for: a date range, an "as of" date, an account, a comparison with last year. */
export const reportInputs: Record<ReportKey, { range: boolean; asOf: boolean; account: boolean; comparison: boolean }> = {
  'trial-balance': { range: true, asOf: false, account: false, comparison: false },
  'profit-and-loss': { range: true, asOf: false, account: false, comparison: true },
  'balance-sheet': { range: false, asOf: true, account: false, comparison: true },
  'statement-of-account': { range: true, asOf: false, account: true, comparison: false },
  'general-ledger': { range: true, asOf: false, account: false, comparison: false },
  journal: { range: true, asOf: false, account: false, comparison: false },
}

export const isReportKey = (key: string | undefined): key is ReportKey => reportKeys.includes(key as ReportKey)

export { parseIsoDate }
