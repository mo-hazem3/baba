import type { TFunction } from 'i18next'
import { ApiError } from '../../api/http'
import { errorMessage } from '../../layout/errors'

/** The shortest password the program accepts (the same number the server uses). */
export const minPasswordLength = 8

/** Every problem the server named, as plain sentences in the user's language (or the one general sentence for the failure). */
export function securityMessages(error: unknown, t: TFunction): string[] {
  if (error instanceof ApiError) {
    const sentences = error.issues
      .map((issue) => t(`security.issues.${issue.code}`, { min: minPasswordLength, defaultValue: '' }))
      .filter((text) => text !== '')
    if (sentences.length > 0) return [...new Set(sentences)]
  }

  return [errorMessage(error, t)]
}

/** The first of <see cref="securityMessages" />, for places with room for one sentence. */
export const securityMessage = (error: unknown, t: TFunction): string => securityMessages(error, t)[0]!
