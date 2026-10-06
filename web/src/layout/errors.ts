import type { TFunction } from 'i18next'
import { ApiError, type ApiIssue } from '../api/http'

/** A plain-language message for any failure, in the user's language. Never shows technical text. */
export const errorMessage = (error: unknown, t: TFunction): string => {
  const problem = error instanceof ApiError ? error.problem : 'Unexpected'
  const key = `errors.${problem}`
  return t(key, { defaultValue: t('errors.Unexpected') })
}

/** The message for one field problem the server found, such as `password.too-short`. */
export const issueMessage = (issue: ApiIssue, t: TFunction, params: Record<string, unknown> = {}): string =>
  t(`validation.${issue.code}`, { ...params, defaultValue: t('errors.Validation') })
