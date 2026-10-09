import type { SessionInfo } from '../../api/generated/model'
import { areaOfPath, hasPermission, needsApproval } from './access'

const session = (over: Partial<SessionInfo>): SessionInfo => ({
  accountsOn: true,
  signedIn: true,
  userName: 'clerk',
  displayName: 'Clerk',
  roleNameEn: 'Clerk',
  roleNameAr: 'موظف',
  mustChangePassword: false,
  permissions: ['accounting.View', 'accounting.Edit'],
  approvalRequired: false,
  ...over,
})

describe('permissions', () => {
  it('allow everything when the company has no user accounts, or the session has not loaded', () => {
    expect(hasPermission(undefined, 'payroll', 'Approve')).toBe(true)
    expect(hasPermission(session({ accountsOn: false, permissions: [] }), 'payroll', 'Approve')).toBe(true)
  })

  it('follow the role once accounts are on', () => {
    const clerk = session({})
    expect(hasPermission(clerk, 'accounting', 'View')).toBe(true)
    expect(hasPermission(clerk, 'accounting', 'Delete')).toBe(false)
    expect(hasPermission(clerk, 'payroll')).toBe(false)
  })

  it('send posting for approval only where approval is on and the role cannot approve', () => {
    expect(needsApproval(session({ approvalRequired: true }), 'accounting')).toBe(true)
    expect(needsApproval(session({ approvalRequired: true, permissions: ['accounting.Approve'] }), 'accounting')).toBe(false)
    expect(needsApproval(session({ approvalRequired: false }), 'accounting')).toBe(false)
    expect(needsApproval(session({ accountsOn: false, approvalRequired: true }), 'accounting')).toBe(false)
  })
})

describe('areaOfPath', () => {
  it('names the area of a screen, also for the pages inside it', () => {
    expect(areaOfPath('/vouchers/payment')).toBe('accounting')
    expect(areaOfPath('/vouchers/open/abc')).toBe('accounting')
    expect(areaOfPath('/payroll/123')).toBe('payroll')
    expect(areaOfPath('/audit-log')).toBe('users')
    expect(areaOfPath('/')).toBeUndefined()
    expect(areaOfPath('/approvals')).toBeUndefined()
    expect(areaOfPath('/salesman')).toBeUndefined() // a prefix only counts at a path boundary
  })
})
