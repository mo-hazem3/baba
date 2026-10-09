import { useQueryClient } from '@tanstack/react-query'
import { getGetCurrentCompanyQueryKey, getGetSessionQueryKey } from '../../api/generated/baba'

/**
 * Whenever who is signed in changes (signing in, signing out, a new password, accounts turned on): forget everything read under the
 * old person, so nobody ever sees a screenful of figures they were not allowed to see, and take the new session. Give it the server's
 * answer, or nothing to have the session read again.
 */
export function useSessionUpdate() {
  const queryClient = useQueryClient()
  return async (response?: unknown) => {
    const keep = new Set<unknown>([getGetSessionQueryKey()[0], getGetCurrentCompanyQueryKey()[0]])
    queryClient.removeQueries({ predicate: (query) => !keep.has(query.queryKey[0]) })
    if (response === undefined) await queryClient.invalidateQueries({ queryKey: getGetSessionQueryKey() })
    else queryClient.setQueryData(getGetSessionQueryKey(), response)
  }
}
