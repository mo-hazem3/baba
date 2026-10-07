import type { AccountDto } from '../../api/generated/model'
import type { Language } from '../../settings/settings'
import { matchesSearch, normalizeArabic } from '../../utils/arabic'

export interface AccountNode extends AccountDto {
  children: AccountNode[]
}

const byCode = (a: { code: string }, b: { code: string }) => a.code.localeCompare(b.code, 'en', { numeric: true, sensitivity: 'base' })

/** The chart of accounts as a tree in code order. Accounts whose parent is missing show at the top. */
export const buildTree = (accounts: readonly AccountDto[]): AccountNode[] => {
  const nodes = new Map<string, AccountNode>(accounts.map((a) => [a.id, { ...a, children: [] }]))
  const roots: AccountNode[] = []
  for (const node of nodes.values()) {
    const parent = node.parentId ? nodes.get(node.parentId) : undefined
    if (parent) parent.children.push(node)
    else roots.push(node)
  }

  const sort = (list: AccountNode[]) => {
    list.sort(byCode)
    list.forEach((n) => sort(n.children))
  }
  sort(roots)
  return roots
}

/** An account's name in the user's language, falling back to the other language if one is missing. */
export const accountName = (account: Pick<AccountDto, 'nameAr' | 'nameEn'>, language: Language): string =>
  (language === 'ar' ? account.nameAr || account.nameEn : account.nameEn || account.nameAr) ?? ''

/** "1110 — Cash on hand", the way an account is shown in lists and pick-lists. */
export const accountLabel = (account: AccountDto, language: Language): string => `${account.code} — ${accountName(account, language)}`

export interface PickOptions {
  /** Only accounts a voucher can post to are offered (postable and not switched off). */
  onlyUsable?: boolean
  /** Only bank and cash accounts (for the "paid from" and "received into" field). */
  cashOnly?: boolean
}

export const pickableAccounts = (accounts: readonly AccountDto[], options: PickOptions = {}): AccountDto[] =>
  accounts
    .filter((a) => !options.onlyUsable || (a.isPosting && a.isActive))
    .filter((a) => !options.cashOnly || a.role === 'CashOrBank')
    .sort(byCode)

/**
 * Finds accounts by code or by Arabic or English name (brief section 7.4). Arabic letter variants and diacritics are ignored, and
 * accounts whose code starts with what was typed come first, so typing "11" brings up 111, 112 ... before anything else.
 */
export const searchAccounts = (accounts: readonly AccountDto[], query: string): AccountDto[] => {
  const typed = normalizeArabic(query).trim()
  const found = accounts.filter((a) => matchesSearch(query, a.code, a.nameAr, a.nameEn))
  if (!typed) return [...found].sort(byCode)

  const rank = (a: AccountDto) => (normalizeArabic(a.code).startsWith(typed) ? 0 : 1)
  return found.sort((a, b) => rank(a) - rank(b) || byCode(a, b))
}

/** The accounts of one type that a group could contain: groups of the same type, for the "parent" field. */
export const possibleParents = (accounts: readonly AccountDto[], type: AccountDto['type'], excludeId?: string): AccountDto[] => {
  const blocked = new Set<string>()
  if (excludeId) {
    // An account cannot sit under itself or anything below it.
    const stack = [excludeId]
    while (stack.length > 0) {
      const id = stack.pop()!
      blocked.add(id)
      accounts.filter((a) => a.parentId === id).forEach((a) => stack.push(a.id))
    }
  }
  return accounts.filter((a) => !a.isPosting && a.type === type && !blocked.has(a.id)).sort(byCode)
}

/** The next free code under a parent, by adding one to its last child's code (or ending 1 under a new parent). */
export const suggestCode = (accounts: readonly AccountDto[], parent: AccountDto | undefined): string => {
  if (!parent) return ''
  const used = new Set(accounts.map((a) => a.code))
  const children = accounts.filter((a) => a.parentId === parent.id).sort(byCode)
  const last = children.at(-1)?.code
  let candidate = last && /^\d+$/.test(last) ? String(BigInt(last) + 1n) : `${parent.code}1`
  while (used.has(candidate)) candidate = /^\d+$/.test(candidate) ? String(BigInt(candidate) + 1n) : `${candidate}1`
  return candidate
}
