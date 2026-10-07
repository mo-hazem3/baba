import type { ApiIssue } from '../../api/http'
import type { VoucherDto, VoucherInput, VoucherKind } from '../../api/generated/model'

/** One row of the voucher grid as the user sees it. Amounts are empty (null) until typed. */
export interface LineRow {
  key: string
  /** The id of the line when this row came from a saved voucher, so editing keeps it. */
  id?: string
  accountId?: string
  /** The customer or supplier, for a line on a receivable or payable account. */
  partyId?: string
  /** The optional cost center or project tag. */
  costCenterId?: string
  description: string
  debit: number | null
  credit: number | null
}

let counter = 0
export const newRow = (): LineRow => ({ key: `row-${++counter}`, description: '', debit: null, credit: null })

export const rowsFromVoucher = (voucher: VoucherDto): LineRow[] =>
  voucher.lines.map((line) => ({
    key: `row-${++counter}`,
    id: line.id,
    accountId: line.accountId,
    partyId: line.partyId ?? undefined,
    costCenterId: line.costCenterId ?? undefined,
    description: line.description ?? '',
    debit: line.debit === 0 ? null : line.debit,
    credit: line.credit === 0 ? null : line.credit,
  }))

/** A row the user has not touched. The grid always keeps one empty row at the end to type into. */
export const isBlank = (row: LineRow): boolean => !row.accountId && !row.description.trim() && !row.debit && !row.credit

/** The rows to send: empty rows are left out. */
export const rowsToSend = (rows: readonly LineRow[]): LineRow[] => rows.filter((r) => !isBlank(r))

/** Makes sure there is exactly one empty row at the end to type into (Excel-like: a new line appears by itself). */
export const withTrailingBlank = (rows: readonly LineRow[]): LineRow[] => {
  const result = [...rows]
  // Two empty rows at the end are one too many; blank rows in the middle are left alone (the user may be about to fill them).
  while (result.length > 1 && isBlank(result.at(-1)!) && isBlank(result.at(-2)!)) result.pop()
  const last = result.at(-1)
  return last && isBlank(last) ? result : [...result, newRow()]
}

// Money is added as whole 1/10,000 units so that 0.1 + 0.2 is exactly 0.3 on screen too.
const scaled = (value: number | null): number => Math.round((value ?? 0) * 10_000)

export interface Totals {
  debit: number
  credit: number
  /** Debits minus credits. A journal can only be posted when this is 0. */
  difference: number
}

export const totals = (rows: readonly LineRow[]): Totals => {
  const debit = rows.reduce((sum, r) => sum + scaled(r.debit), 0)
  const credit = rows.reduce((sum, r) => sum + scaled(r.credit), 0)
  return { debit: debit / 10_000, credit: credit / 10_000, difference: (debit - credit) / 10_000 }
}

export interface VoucherHeader {
  date: string
  cashAccountId?: string | null
  reference?: string | null
  memo?: string | null
}

/** The request for the API. Payment rows send a debit, receipt rows a credit, journal rows whichever was typed. */
export const toVoucherInput = (kind: VoucherKind, header: VoucherHeader, rows: readonly LineRow[]): VoucherInput => ({
  kind,
  date: header.date,
  cashAccountId: kind === 'Journal' ? null : (header.cashAccountId ?? null),
  reference: header.reference?.trim() ? header.reference.trim() : null,
  memo: header.memo?.trim() ? header.memo.trim() : null,
  lines: rowsToSend(rows).map((r) => ({
    id: r.id ?? null,
    accountId: r.accountId ?? '00000000-0000-0000-0000-000000000000',
    description: r.description.trim() ? r.description.trim() : null,
    partyId: r.partyId ?? null,
    costCenterId: r.costCenterId ?? null,
    debit: kind === 'Receipt' ? 0 : (r.debit ?? 0),
    credit: kind === 'Payment' ? 0 : (r.credit ?? 0),
  })),
})

export interface MappedIssues {
  /** Problems with the form as a whole (date, bank account, balance...), by field. */
  header: Record<string, string>
  /** Problems on rows, by the row's key: `account` and/or `amount` with the problem code. */
  rows: Record<string, { account?: string; amount?: string; party?: string; costCenter?: string }>
  /** The same problems in reading order for the list at the top of the form, with the row's number (1, 2, 3...) when it has one. */
  list: { code: string; line?: number }[]
}

/**
 * Puts the API's problems (such as `lines[2].account`) back where the user can see them: next to the field, on the row.
 * `sent` must be the rows that were sent (empty rows were left out), so the index maps to the right one.
 */
export const mapIssues = (issues: readonly ApiIssue[], sent: readonly LineRow[]): MappedIssues => {
  const result: MappedIssues = { header: {}, rows: {}, list: [] }
  for (const issue of issues) {
    const match = /^lines\[(\d+)\]\.(account|amount|party|costCenter)$/.exec(issue.field)
    if (match) {
      const index = Number(match[1])
      const row = sent[index]
      if (row) {
        result.rows[row.key] = { ...result.rows[row.key], [match[2]!]: issue.code }
        result.list.push({ code: issue.code, line: index + 1 })
        continue
      }
    }
    result.header[issue.field] = issue.code
    result.list.push({ code: issue.code })
  }
  return result
}

export const kindFromRoute = (segment: string | undefined): VoucherKind | undefined =>
  segment === 'payment' ? 'Payment' : segment === 'receipt' ? 'Receipt' : segment === 'journal' ? 'Journal' : undefined

export const routeOfKind = (kind: VoucherKind): string => kind.toLowerCase()
