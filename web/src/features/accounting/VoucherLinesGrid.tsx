import { Button, Input, InputNumber } from 'antd'
import { useMemo, useRef, type KeyboardEvent } from 'react'
import { useTranslation } from 'react-i18next'
import type { AccountDto, CostCenterDto, PartyDto, VoucherKind } from '../../api/generated/model'
import { AccountSelect } from './AccountSelect'
import { CostCenterSelect, PartySelect } from './LookupSelects'
import { hasFreeLines, withTrailingBlank, type LineRow, type MappedIssues } from './voucherModel'

type Column = 'account' | 'party' | 'description' | 'costCenter' | 'amount' | 'debit' | 'credit'

/** A line on a receivable or payable account says which customer or supplier it is for (the sub-ledger). */
const partyKindFor = (account: AccountDto | undefined): PartyDto['kind'] | undefined =>
  account?.role === 'Receivable' ? 'Customer' : account?.role === 'Payable' ? 'Supplier' : undefined

/**
 * The lines of a voucher, entered like in Excel (brief section 7.4):
 * Enter or Tab goes to the next cell, a new line appears by itself at the bottom, and the arrow keys move between rows.
 * A payment line is one amount (the money spent), a receipt line one amount (where it came from), a journal line a debit or a credit.
 * With customers and suppliers on, a line on a receivable or payable account also names the party; with cost centers on, any
 * line can carry one.
 */
export function VoucherLinesGrid({
  kind,
  rows,
  onChange,
  accounts,
  parties = [],
  costCenters = [],
  showParty = false,
  showCostCenter = false,
  issues,
  minorUnits,
  disabled,
}: {
  kind: VoucherKind
  rows: readonly LineRow[]
  onChange: (rows: LineRow[]) => void
  accounts: readonly AccountDto[]
  parties?: readonly PartyDto[]
  costCenters?: readonly CostCenterDto[]
  showParty?: boolean
  showCostCenter?: boolean
  issues: MappedIssues['rows']
  minorUnits: number
  disabled?: boolean
}) {
  const { t } = useTranslation()
  const container = useRef<HTMLDivElement>(null)
  const journal = hasFreeLines(kind)
  const accountsById = useMemo(() => new Map(accounts.map((a) => [a.id, a])), [accounts])

  const columns: Column[] = [
    'account',
    ...(showParty ? (['party'] as const) : []),
    'description',
    ...(showCostCenter ? (['costCenter'] as const) : []),
    ...(journal ? (['debit', 'credit'] as const) : (['amount'] as const)),
  ]

  const focusCell = (row: number, column: Column) =>
    setTimeout(() => {
      const cell = container.current?.querySelector<HTMLElement>(`[data-row="${row}"][data-col="${column}"]`)
      cell?.querySelector<HTMLElement>('input')?.focus()
    }, 0)

  /** Focuses the first cell that can be typed in, starting at a column; after the last one, the next line's account. */
  const focusFrom = (row: number, startColumn: number) =>
    setTimeout(() => {
      for (let i = startColumn; i < columns.length; i++) {
        const input = container.current?.querySelector<HTMLElement>(`[data-row="${row}"][data-col="${columns[i]}"] input:not([disabled])`)
        if (input) return input.focus()
      }
      container.current?.querySelector<HTMLElement>(`[data-row="${row + 1}"][data-col="account"] input`)?.focus()
    }, 0)

  const update = (key: string, patch: Partial<LineRow>) =>
    onChange(withTrailingBlank(rows.map((r) => (r.key === key ? { ...r, ...patch } : r))))

  const remove = (key: string) => onChange(withTrailingBlank(rows.filter((r) => r.key !== key)))

  /** A journal line is a debit or a credit, never both: typing one clears the other. */
  const setAmount = (row: LineRow, column: 'debit' | 'credit' | 'amount', value: number | null) => {
    if (column === 'amount') update(row.key, kind === 'Receipt' ? { credit: value } : { debit: value })
    else update(row.key, column === 'debit' ? { debit: value, ...(value ? { credit: null } : {}) } : { credit: value, ...(value ? { debit: null } : {}) })
  }

  /** Choosing another account drops a customer or supplier that no longer fits it (a party only belongs on receivables and payables). */
  const setAccount = (row: LineRow, index: number, accountId: string | undefined) => {
    const needs = partyKindFor(accountId ? accountsById.get(accountId) : undefined)
    const keep = needs && row.partyId && parties.find((p) => p.id === row.partyId)?.kind === needs ? row.partyId : undefined
    update(row.key, { accountId, partyId: keep })
    if (accountId) focusFrom(index, 1)
  }

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    const cell = (event.target as HTMLElement).closest<HTMLElement>('[data-row][data-col]')
    if (!cell || event.altKey || event.ctrlKey || event.metaKey) return

    const row = Number(cell.dataset.row)
    const column = cell.dataset.col as Column
    if (column === 'account' || column === 'party' || column === 'costCenter') return // these boxes handle their own keys

    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault()
      focusFrom(row, columns.indexOf(column) + 1)
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
            {showParty && <th>{t('accounting.party')}</th>}
            <th>{t('accounting.description')}</th>
            {showCostCenter && <th>{t('accounting.costCenter')}</th>}
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
            const partyProblem = message(problem?.party, index + 1)
            const costCenterProblem = message(problem?.costCenter, index + 1)
            const amountProblem = message(problem?.amount, index + 1)
            const amountStatus = problem?.amount ? 'error' : undefined
            const needsParty = partyKindFor(row.accountId ? accountsById.get(row.accountId) : undefined)
            return (
              <tr key={row.key}>
                <td className="col-number">{index + 1}</td>
                <td data-row={index} data-col="account">
                  <AccountSelect
                    value={row.accountId}
                    accounts={accounts}
                    onChange={(accountId) => setAccount(row, index, accountId)}
                    status={problem?.account ? 'error' : undefined}
                    ariaLabel={`${t('accounting.account')} ${index + 1}`}
                    disabled={disabled}
                  />
                  {accountProblem && <div className="cell-error">{accountProblem}</div>}
                </td>
                {showParty && (
                  <td data-row={index} data-col="party">
                    <PartySelect
                      value={row.partyId}
                      parties={parties}
                      kind={needsParty}
                      onChange={(partyId) => {
                        update(row.key, { partyId })
                        if (partyId) focusFrom(index, columns.indexOf('party') + 1)
                      }}
                      placeholder={needsParty ? t(needsParty === 'Customer' ? 'accounting.chooseCustomer' : 'accounting.chooseSupplier') : undefined}
                      status={problem?.party ? 'error' : undefined}
                      ariaLabel={`${t('accounting.party')} ${index + 1}`}
                      disabled={disabled || !needsParty}
                    />
                    {partyProblem && <div className="cell-error">{partyProblem}</div>}
                  </td>
                )}
                <td data-row={index} data-col="description">
                  <Input
                    value={row.description}
                    onChange={(e) => update(row.key, { description: e.target.value })}
                    maxLength={200}
                    aria-label={`${t('accounting.description')} ${index + 1}`}
                    disabled={disabled}
                  />
                </td>
                {showCostCenter && (
                  <td data-row={index} data-col="costCenter">
                    <CostCenterSelect
                      value={row.costCenterId}
                      costCenters={costCenters}
                      onChange={(costCenterId) => {
                        update(row.key, { costCenterId })
                        if (costCenterId) focusFrom(index, columns.indexOf('costCenter') + 1)
                      }}
                      status={problem?.costCenter ? 'error' : undefined}
                      ariaLabel={`${t('accounting.costCenter')} ${index + 1}`}
                      disabled={disabled}
                    />
                    {costCenterProblem && <div className="cell-error">{costCenterProblem}</div>}
                  </td>
                )}
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
