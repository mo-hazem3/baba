import ar from './locales/ar.json'
import en from './locales/en.json'

type Tree = { [key: string]: string | Tree }

const has = (tree: Tree, key: string): boolean => {
  let node: string | Tree | undefined = tree
  for (const part of key.split('.')) {
    if (typeof node !== 'object') return false
    node = node[part]
  }
  return node !== undefined
}

// Every screen's source, read as text, so a text that is used but never written down is caught here and not by a user.
const sources = import.meta.glob(['../**/*.{ts,tsx}', '!../**/*.test.*', '!../api/generated/**'], {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>

describe('translation keys used in the code', () => {
  it('exist in both languages', () => {
    const missing: string[] = []
    for (const [file, text] of Object.entries(sources)) {
      for (const match of text.matchAll(/\bt\(\s*(['`])([^'`$]+)\1/g)) {
        const key = match[2]!
        for (const [name, tree] of [['en', en], ['ar', ar]] as const) {
          if (!has(tree as Tree, key)) missing.push(`${name}: ${key} (${file})`)
        }
      }
    }
    expect(missing).toEqual([])
  })

  it('explain every problem the accounting API can report', () => {
    const accountCodes = [
      'account.code-duplicate', 'account.code-required', 'account.group-has-entries', 'account.has-children', 'account.has-entries',
      'account.name-required', 'account.parent-cycle', 'account.parent-must-be-group', 'account.parent-unknown',
      'account.posting-has-children', 'account.role-needs-posting', 'account.role-wrong-type', 'account.type-differs-from-parent',
      'account.type-has-children', 'account.type-has-entries', 'move.same-account', 'move.target-not-cash', 'move.target-not-usable',
      'move.target-unknown', 'move.type-differs',
    ]
    const voucherCodes = [
      'balance.unbalanced', 'balance.unbalanced-base', 'cash-account.invalid', 'cash-account.required', 'currency.required',
      'currency.unknown', 'date.locked-period', 'date.required', 'exchange-rate.base-must-be-one', 'exchange-rate.invalid',
      'line.account-inactive', 'line.account-not-posting', 'line.account-required', 'line.account-unknown', 'line.amount-both-sides',
      'line.amount-decimals', 'line.amount-negative', 'line.amount-required', 'line.amount-wrong-side', 'lines.required',
      'voucher.kind-cannot-change', 'line.party-required', 'line.party-unknown', 'line.party-inactive', 'line.party-not-allowed',
      'line.cost-center-unknown', 'line.cost-center-inactive',
    ]
    const partyCodes = ['party.code-required', 'party.code-duplicate', 'party.name-required', 'party.credit-limit-negative', 'party.terms-invalid', 'party.kind-in-use', 'party.in-use']
    const costCenterCodes = ['cost-center.code-required', 'cost-center.code-duplicate', 'cost-center.name-required', 'cost-center.in-use']
    const missing = [
      ...accountCodes.map((code) => `accounts.issues.${code}`),
      ...voucherCodes.map((code) => `voucher.issues.${code}`),
      ...partyCodes.map((code) => `parties.issues.${code}`),
      ...costCenterCodes.map((code) => `costCenters.issues.${code}`),
      ...['image.required', 'image.too-large', 'image.unsupported-type'].map((code) => `branding.issues.${code}`),
      ...['print.layout-unknown', 'print.text-too-long'].map((code) => `printing.issues.${code}`),
    ].filter((key) => !has(en as Tree, key) || !has(ar as Tree, key))
    expect(missing).toEqual([])
  })

  it('name every report, voucher kind, account type and special use', () => {
    const missing = [
      ...['trial-balance', 'profit-and-loss', 'balance-sheet', 'statement-of-account', 'general-ledger', 'journal', 'party-statement', 'aging-receivable', 'aging-payable', 'cost-centers'].flatMap((r) => [`reports.names.${r}`, `reports.descriptions.${r}`]),
      ...['Payment', 'Receipt', 'Journal'].flatMap((k) => [`voucher.title.${k}`, `voucher.plural.${k}`, `voucher.new.${k}`, `voucher.emptyTitle.${k}`, `voucher.emptyBody.${k}`]),
      ...['Asset', 'Liability', 'Equity', 'Revenue', 'Expense'].map((t) => `accounts.types.${t}`),
      ...['None', 'CashOrBank', 'Receivable', 'Payable', 'RetainedEarnings'].map((r) => `accounts.roles.${r}`),
      ...['summary', 'settings', 'accounts', 'vouchers', 'voucher', 'reports', 'report', 'parties', 'costCenters'].map((h) => `help.${h}`),
    ].filter((key) => !has(en as Tree, key) || !has(ar as Tree, key))
    expect(missing).toEqual([])
  })
})
