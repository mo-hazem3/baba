import type { AccountDto, VoucherDto } from '../../api/generated/model'
import { accountLabel, accountName, buildTree, pickableAccounts, possibleParents, searchAccounts, suggestCode } from './accountTree'
import { isBlank, kindFromRoute, mapIssues, newRow, rowsFromVoucher, rowsToSend, toVoucherInput, totals, withTrailingBlank, type LineRow } from './voucherModel'

const account = (code: string, nameEn: string, nameAr: string, extra: Partial<AccountDto> = {}): AccountDto => ({
  id: `id-${code}`,
  code,
  nameEn,
  nameAr,
  parentId: null,
  type: 'Asset',
  isPosting: true,
  isActive: true,
  role: 'None',
  hasEntries: false,
  ...extra,
})

const chart: AccountDto[] = [
  account('1', 'Assets', 'الأصول', { isPosting: false }),
  account('11', 'Current assets', 'الأصول المتداولة', { isPosting: false, parentId: 'id-1' }),
  account('111', 'Cash on hand', 'النقدية بالصندوق', { parentId: 'id-11', role: 'CashOrBank' }),
  account('112', 'Bank account', 'الحساب البنكي', { parentId: 'id-11', role: 'CashOrBank' }),
  account('113', 'Accounts receivable', 'العملاء', { parentId: 'id-11', role: 'Receivable' }),
  account('12', 'Fixed assets', 'الأصول الثابتة', { isPosting: false, parentId: 'id-1' }),
  account('2', 'Liabilities', 'الخصوم', { isPosting: false, type: 'Liability' }),
  account('10', 'Closed account', 'حساب مغلق', { parentId: 'id-12', isActive: false }),
]

describe('buildTree', () => {
  it('nests accounts under their parents in natural code order', () => {
    const tree = buildTree(chart)

    expect(tree.map((n) => n.code)).toEqual(['1', '2'])
    expect(tree[0]!.children.map((n) => n.code)).toEqual(['11', '12']) // 11 before 12
    expect(tree[0]!.children[0]!.children.map((n) => n.code)).toEqual(['111', '112', '113'])
    expect(tree[0]!.children[1]!.children.map((n) => n.code)).toEqual(['10'])
  })

  it('sorts numerically so 2 comes before 10', () => {
    const tree = buildTree([account('10', 'Ten', 'عشرة'), account('2', 'Two', 'اثنان'), account('1', 'One', 'واحد')])

    expect(tree.map((n) => n.code)).toEqual(['1', '2', '10'])
  })

  it('shows an account at the top if its parent is missing', () => {
    expect(buildTree([account('5', 'Orphan', 'يتيم', { parentId: 'gone' })]).map((n) => n.code)).toEqual(['5'])
  })
})

describe('account names and labels', () => {
  it('use the language of the user and fall back to the other', () => {
    expect(accountName(chart[2]!, 'en')).toBe('Cash on hand')
    expect(accountName(chart[2]!, 'ar')).toBe('النقدية بالصندوق')
    expect(accountName({ nameEn: 'Only English', nameAr: '' }, 'ar')).toBe('Only English')
    expect(accountLabel(chart[2]!, 'en')).toBe('111 — Cash on hand')
  })
})

describe('pickableAccounts', () => {
  it('offers only usable postable accounts when asked, and bank and cash accounts for the cash field', () => {
    expect(pickableAccounts(chart, { onlyUsable: true }).map((a) => a.code)).toEqual(['111', '112', '113'])
    expect(pickableAccounts(chart, { onlyUsable: true, cashOnly: true }).map((a) => a.code)).toEqual(['111', '112'])
    expect(pickableAccounts(chart).map((a) => a.code)).toContain('10') // everything otherwise
  })
})

describe('searchAccounts', () => {
  it('finds by code, English name or Arabic name', () => {
    expect(searchAccounts(chart, '112').map((a) => a.code)).toEqual(['112'])
    expect(searchAccounts(chart, 'bank').map((a) => a.code)).toEqual(['112'])
    expect(searchAccounts(chart, 'الصندوق').map((a) => a.code)).toEqual(['111'])
  })

  it('ignores Arabic diacritics and letter variants', () => {
    expect(searchAccounts(chart, 'الاصول').map((a) => a.code)).toEqual(['1', '11', '12']) // typed with a plain alef
    expect(searchAccounts(chart, 'النقديه').map((a) => a.code)).toEqual(['111']) // heh for teh marbuta
  })

  it('puts codes that start with what was typed first, in code order', () => {
    // "11" matches 11, 111, 112, 113 by code, and "Fixed assets"... only by code here
    expect(searchAccounts(chart, '11').map((a) => a.code)).toEqual(['11', '111', '112', '113'])
  })

  it('returns everything in code order when nothing is typed', () => {
    expect(searchAccounts(chart, '  ').map((a) => a.code)).toEqual(['1', '2', '10', '11', '12', '111', '112', '113'])
  })
})

describe('possibleParents and suggestCode', () => {
  it('offers groups of the same type, never the account itself or anything below it', () => {
    expect(possibleParents(chart, 'Asset').map((a) => a.code)).toEqual(['1', '11', '12'])
    expect(possibleParents(chart, 'Asset', 'id-11').map((a) => a.code)).toEqual(['1', '12']) // not 11
    expect(possibleParents(chart, 'Asset', 'id-1').map((a) => a.code)).toEqual([]) // everything is below 1
    expect(possibleParents(chart, 'Liability').map((a) => a.code)).toEqual(['2'])
  })

  it('suggests the next code under a parent', () => {
    expect(suggestCode(chart, chart[1])).toBe('114') // after 113
    expect(suggestCode(chart, chart[5])).toBe('13') // 12's only child is "10": 10 + 1 = 11 and then 12 are both taken, so 13
  })

  it('suggests nothing without a parent', () => expect(suggestCode(chart, undefined)).toBe(''))
})

// ---------------------------------------------------------------- voucher rows

const row = (extra: Partial<LineRow> = {}): LineRow => ({ ...newRow(), ...extra })

describe('voucher rows', () => {
  it('know when a row is empty', () => {
    expect(isBlank(row())).toBe(true)
    expect(isBlank(row({ description: '   ' }))).toBe(true)
    expect(isBlank(row({ accountId: 'a' }))).toBe(false)
    expect(isBlank(row({ debit: 5 }))).toBe(false)
    expect(isBlank(row({ description: 'rent' }))).toBe(false)
  })

  it('always keep exactly one empty row at the end to type into', () => {
    const filled = row({ accountId: 'a', debit: 1 })

    expect(withTrailingBlank([]).length).toBe(1)
    expect(withTrailingBlank([filled]).map(isBlank)).toEqual([false, true])
    expect(withTrailingBlank([filled, row(), row()]).map(isBlank)).toEqual([false, true]) // extra empty rows at the end go
    expect(withTrailingBlank([row(), filled]).map(isBlank)).toEqual([true, false, true]) // an empty row in the middle stays
  })

  it('leave out empty rows when sending', () => {
    const rows = [row({ accountId: 'a', debit: 1 }), row(), row({ accountId: 'b', debit: 2 })]

    expect(rowsToSend(rows)).toHaveLength(2)
  })

  it('add up exactly, with no floating point drift', () => {
    const rows = [row({ debit: 0.1 }), row({ debit: 0.2 }), row({ credit: 0.3 })]

    expect(totals(rows)).toEqual({ debit: 0.3, credit: 0.3, difference: 0 }) // 0.1 + 0.2 is exactly 0.3
    expect(totals([row({ debit: 100 }), row({ credit: 99.999 })]).difference).toBe(0.001)
    expect(totals([])).toEqual({ debit: 0, credit: 0, difference: 0 })
  })

  it('turn into an API request by kind', () => {
    const rows = [row({ id: 'line-1', accountId: 'acc-1', description: ' rent ', debit: 750, credit: 5 }), row(), row({ accountId: 'acc-2', debit: 20 })]
    const header = { date: '2026-10-06', cashAccountId: 'cash', reference: ' CHQ-1 ', memo: '  ' }

    const payment = toVoucherInput('Payment', header, rows)
    expect(payment).toMatchObject({ kind: 'Payment', date: '2026-10-06', cashAccountId: 'cash', reference: 'CHQ-1', memo: null })
    expect(payment.lines).toEqual([
      { id: 'line-1', accountId: 'acc-1', partyId: null, costCenterId: null, description: 'rent', debit: 750, credit: 0 }, // a payment line is a debit only
      { id: null, accountId: 'acc-2', partyId: null, costCenterId: null, description: null, debit: 20, credit: 0 },
    ])

    const receipt = toVoucherInput('Receipt', header, rows)
    expect(receipt.lines.map((l) => [l.debit, l.credit])).toEqual([[0, 5], [0, 0]]) // a receipt line is a credit only

    const journal = toVoucherInput('Journal', header, rows)
    expect(journal.cashAccountId).toBeNull();
    expect(journal.lines.map((l) => [l.debit, l.credit])).toEqual([[750, 5], [20, 0]])
  })

  it('carry the customer and the cost center of a line to the request', () => {
    const [line] = toVoucherInput('Receipt', { date: '2026-10-06', cashAccountId: 'cash' }, [row({ accountId: 'a', partyId: 'cust', costCenterId: 'cc', credit: 9 })]).lines
    expect(line).toMatchObject({ partyId: 'cust', costCenterId: 'cc' })
  })

  it('come back from a saved voucher without zeros in the way', () => {
    const voucher = {
      lines: [{ id: 'l1', accountId: 'a', description: null, debit: 50, credit: 0 }, { id: 'l2', accountId: 'b', description: 'x', debit: 0, credit: 50 }],
    } as VoucherDto

    const rows = rowsFromVoucher(voucher)

    expect(rows.map((r) => [r.id, r.accountId, r.description, r.debit, r.credit])).toEqual([
      ['l1', 'a', '', 50, null],
      ['l2', 'b', 'x', null, 50],
    ])
  })
})

describe('mapIssues', () => {
  it('puts row problems on the right row even though empty rows were left out', () => {
    const rows = [row({ accountId: 'a', debit: 1 }), row(), row({ debit: 2 })]
    const sent = rowsToSend(rows) // the first and third row: indexes 0 and 1

    const mapped = mapIssues(
      [
        { field: 'lines[1].account', code: 'line.account-required' },
        { field: 'cashAccount', code: 'cash-account.required' },
        { field: 'balance', code: 'balance.unbalanced' },
      ],
      sent,
    )

    expect(mapped.rows[rows[2]!.key]).toEqual({ account: 'line.account-required' }) // the third row on screen
    expect(mapped.rows[rows[0]!.key]).toBeUndefined()
    expect(mapped.header).toEqual({ cashAccount: 'cash-account.required', balance: 'balance.unbalanced' })
    expect(mapped.list).toEqual([
      { code: 'line.account-required', line: 2 }, // "line 2" counts the rows that were sent
      { code: 'cash-account.required' },
      { code: 'balance.unbalanced' },
    ])
  })

  it('keeps an account problem and an amount problem of the same row together', () => {
    const rows = [row({ description: 'x' })]

    const mapped = mapIssues([{ field: 'lines[0].account', code: 'a' }, { field: 'lines[0].amount', code: 'b' }], rows)

    expect(mapped.rows[rows[0]!.key]).toEqual({ account: 'a', amount: 'b' })
  })

  it('treats a row index that does not exist as a problem with the form', () => {
    expect(mapIssues([{ field: 'lines[7].amount', code: 'x' }], []).header).toEqual({ 'lines[7].amount': 'x' })
  })
})

describe('kindFromRoute', () => {
  it('reads the kind from the address and ignores anything else', () => {
    expect(kindFromRoute('payment')).toBe('Payment')
    expect(kindFromRoute('receipt')).toBe('Receipt')
    expect(kindFromRoute('journal')).toBe('Journal')
    expect(kindFromRoute('invoice')).toBeUndefined()
    expect(kindFromRoute(undefined)).toBeUndefined()
  })
})
