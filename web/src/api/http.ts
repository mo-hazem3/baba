// The one place that talks to the network. The generated client (src/api/generated) calls this.
// The API's secret token travels in a cookie the desktop host sets, so nothing here handles it.

export interface ApiIssue {
  field: string
  code: string
}

/** What the API sends when something goes wrong. `problem` is a stable code the UI translates. */
export class ApiError extends Error {
  readonly status: number
  readonly problem: string
  readonly issues: ApiIssue[]

  constructor(status: number, problem: string, message: string, issues: ApiIssue[] = []) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.problem = problem
    this.issues = issues
  }
}

interface ProblemBody {
  problem?: string
  message?: string
  issues?: ApiIssue[]
}

export const http = async <T>(url: string, options: RequestInit): Promise<T> => {
  let response: Response
  try {
    response = await fetch(url, { credentials: 'same-origin', ...options })
  } catch {
    throw new ApiError(0, 'Network', 'Cannot reach Baba.')
  }

  const text = await response.text()
  const data: unknown = text ? JSON.parse(text) : undefined

  if (!response.ok && response.status !== 501) {
    const body = (data ?? {}) as ProblemBody
    throw new ApiError(response.status, body.problem ?? 'Unexpected', body.message ?? response.statusText, body.issues ?? [])
  }

  return { data, status: response.status, headers: response.headers } as T
}
