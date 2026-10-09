import type { SessionInfo } from '../../api/generated/model'

export type PermissionAction = 'View' | 'Edit' | 'Delete' | 'Approve'

/** Whether the person may do this. A company without user accounts allows everything, and so does a session that has not loaded yet. */
export const hasPermission = (session: SessionInfo | undefined, area: string, action: PermissionAction = 'View'): boolean =>
  !session || !session.accountsOn || session.permissions.includes(`${area}.${action}`)

/** Posting or issuing needs someone who may approve this area when the company asks for approval. */
export const needsApproval = (session: SessionInfo | undefined, area: string): boolean =>
  session?.accountsOn === true && session.approvalRequired === true && !hasPermission(session, area, 'Approve')

// The permission area each part of the app belongs to (the same areas the API checks), so the menu and the "+ New" buttons follow the role.
const areaOfPrefix: readonly (readonly [string, string])[] = [
  ['/accounts', 'accounting'],
  ['/vouchers', 'accounting'],
  ['/year-end', 'accounting'],
  ['/exchange-rates', 'accounting'],
  ['/recurring', 'accounting'],
  ['/cost-centers', 'accounting'],
  ['/bank', 'banking'],
  ['/customers', 'parties'],
  ['/suppliers', 'parties'],
  ['/sales', 'trade'],
  ['/purchases', 'trade'],
  ['/documents', 'trade'],
  ['/settlements', 'trade'],
  ['/products', 'products'],
  ['/tax-codes', 'tax'],
  ['/stock', 'inventory'],
  ['/warehouses', 'inventory'],
  ['/assets', 'assets'],
  ['/employees', 'payroll'],
  ['/payroll', 'payroll'],
  ['/claims', 'claims'],
  ['/budgets', 'budgets'],
  ['/reports', 'reports'],
  ['/settings', 'settings'],
  ['/users', 'users'],
  ['/audit-log', 'users'],
]

/** The permission area of a screen, or undefined for the ones everybody who is signed in may open (the Summary, the approvals). */
export const areaOfPath = (pathname: string): string | undefined =>
  areaOfPrefix.find(([prefix]) => pathname === prefix || pathname.startsWith(prefix + '/'))?.[1]
