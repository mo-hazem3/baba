import { Button, Input, InputNumber } from 'antd'
import { useRef, type KeyboardEvent } from 'react'
import { useTranslation } from 'react-i18next'
import type { AccountDto, VoucherKind } from '../../api/generated/model'
import { AccountSelect } from './AccountSelect'
import { withTrailingBlank, type LineRow, type MappedIssues } from './voucherModel'

type Column = 'account' | 'description' | 'amount' | 'debit' | 'credit'

/**
 * The lines of a voucher, entered like in Excel (brief section 7.4):
 * Enter or Tab goes to the next cell, a new line appears by itself at the bottom, and the arrow keys move between rows.
 * A payment line is one amount (the money spent), a receipt line one amount (where it came from), a journal line a debit or a credit.
 */
export function VoucherLinesGrid({
  kind,
  rows,
  onChange,
  accounts,
  issues,
  minorUnits,
  disabled,
}: {
  kind: VoucherKind
  rows: readonly LineRow[]
  onChange: (rows: LineRow[]) => void
  accounts: readonly AccountDto[]
  issues: MappedIssues['rows']
  minorUnits: number
  disabled?: boolean
}) {
  const { t } = useTranslation()
  const container = useRef<HTMLDivElement>(null)
  const journal = kind === 'Journal'
  const columns: Column[] = journal ? ['account', 'description', 'debit', 'credit'] : ['account', 'description', 'amount']

  const focusCell = (row: number, column: Column) =>
    setTimeout(() => {
      const cell = container.current?.querySelector<HTMLElement>(`[data-row="${row}"][data-col="${column}"]`)
      cell?.querySelector<HTMLElement>('input')?.focus()
    }, 0)

  const update = (key: string, patch: Partial<LineRow>) =>
    onChange(withTrailingBlank(rows.map((r) => (r.key === key ? { ...r, ...patch } : r))))

  const remove = (key: string) => onChange(withTrailingBlank(rows.filter((r) => r.key !== key)))

  /** A journal line is a debit or a credit, never both: typing one clears the other. */
  const setAmount = (row: LineRow, column: 'debit' | 'credit' | 'amount', value: number | null) => {
    if (column === 'amount') update(row.key, kind === 'Receipt' ? { credit: value } : { debit: value })
    else update(row.key, column === 'debit' ? { debit: value, ...(value ? { credit: null } : {}) } : { credit: value, ...(value ? { debit: null } : {}) })
  }

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    const cell = (event.target as HTMLElement).closest<HTMLElement>('[data-row][data-col]')
    if (!cell || event.altKey || event.ctrlKey || event.metaKey) return

    const row = Number(cell.dataset.row)
    const column = cell.dataset.col as Column
    if (column === 'account') return // the account box handles its own keys (Enter picks the highlighted account)

    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault()
      const index = columns.indexOf(column)
      if (index < columns.length - 1) focusCell(row, columns[index + 1]!)
      else focusCell(row + 1, 'account') // the last cell of a row: on to the next line
    } else if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault()
      focusCell(row + (event.key === 'ArrowDown' ? 1 : -1), column)
    }
  }

  const message = (code: string | undefined, line: number) => (code ? t(`voucher.issues.${code}`, { line, defaultValue: code }) : undefined)

  return (
    <div ref={container} onKeyDown={onKeyDown} className="lines-grid-wrap">
      <table className="lines-grid">
        <thead>
          <tr>
            <th className="col-number">#</th>
            <th>{t('accounting.account')}</th>
            <th>{t('accounting.description')}</th>
            {journal ? (
              <>
                <th className="num">{t('accounting.debit')}</th>
                <th className="num">{t('accounting.credit')}</th>
              </>
            ) : (
              <th className="num">{t('accounting.amount')}</th>
            )}
            <th className="col-actions">
              <span className="visually-hidden">{t('voucher.rowActions')}</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row, index) => {
            const problem = issues[row.key]
            const accountProblem = message(problem?.account, index + 1)
            const amountProblem = message(problem?.amount, index + 1)
            const amountStatus = problem?.amount ? 'error' : undefined
            return (
              <tr key={row.key}>
                <td className="col-number">{index + 1}</td>
                <td data-row={index} data-col="account">
                  <AccountSelect
                    value={row.accountId}
                    accounts={accounts}
                    onChange={(accountId) => {
                      update(row.key, { accountId })
                      if (accountId) focusCell(index, 'description')
                    }}
                    status={problem?.account ? 'error' : undefined}
                    ariaLabel={`${t('accounting.account')} ${index + 1}`}
                    disabled={disabled}
                  />
                  {accountProblem && <div className="cell-error">{accountProblem}</div>}
                </td>
                <td data-row={index} data-col="description">
                  <Input
                    value={row.description}
                    onChange={(e) => update(row.key, { description: e.target.value })}
                    maxLength={200}
                    aria-label={`${t('accounting.description')} ${index + 1}`}
                    disabled={disabled}
                  />
                </td>
                {(journal ? (['debit', 'credit'] as const) : (['amount'] as const)).map((column) => {
                  const value = column === 'credit' || (column === 'amount' && kind === 'Receipt') ? row.credit : row.debit
                  const label = column === 'amount' ? t('accounting.amount') : t(`accounting.${column}`)
                  return (
                    <td key={column} data-row={index} data-col={column} className="num">
                      <InputNumber
                        value={value}
                        onChange={(v) => setAmount(row, column, typeof v === 'number' ? v : null)}
                        min={0}
                        precision={minorUnits}
                        controls={false}
                        keyboard={false}
                        status={amountStatus}
                        aria-label={`${label} ${index + 1}`}
                        className="amount-input"
                        disabled={disabled}
                      />
                      {(column === 'amount' || column === 'credit') && amountProblem && <div className="cell-error">{amountProblem}</div>}
                    </td>
                  )
                })}
                <td className="col-actions">
                  {rows.length > 1 && !disabled && (index < rows.length - 1 || row.accountId || row.description || row.debit || row.credit) ? (
                    <Button type="link" danger size="small" onClick={() => remove(row.key)}>
                      {t('voucher.removeRow')}
                    </Button>
                  ) : null}
                </td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}
