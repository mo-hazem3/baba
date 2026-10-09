import { useSession } from '../../api/hooks'
import { hasPermission, needsApproval, type PermissionAction } from './access'

/** What the signed-in person may do. Screens ask this instead of looking at roles: a role is just a set of permissions. */
export function useAccess() {
  const query = useSession()
  const session = query.data
  return {
    session,
    accountsOn: session?.accountsOn === true,
    can: (area: string, action: PermissionAction = 'View') => hasPermission(session, area, action),
    /** Posting or issuing in this area has to be sent to an approver. */
    needsApproval: (area: string) => needsApproval(session, area),
  }
}
