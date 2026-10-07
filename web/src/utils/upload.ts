import { ApiError } from '../api/http'

/**
 * Sends a file the user chose (a spreadsheet or CSV) as the request body, and returns the answer. The generated client cannot send
 * files, so the few calls that need it use this. The file name travels in a header; it is percent-encoded because a header can only
 * hold plain characters and file names may be Arabic. The server only looks at the ending (.csv or .xlsx).
 */
export async function uploadFile<T>(url: string, file: File): Promise<T> {
  let response: Response
  try {
    response = await fetch(url, {
      method: 'PUT',
      credentials: 'same-origin',
      headers: { 'X-File-Name': encodeURIComponent(file.name) },
      body: file,
    })
  } catch {
    throw new ApiError(0, 'Network', 'Cannot reach Baba.')
  }

  if (!response.ok) {
    const body = (await response.json().catch(() => ({}))) as { problem?: string; message?: string; issues?: { field: string; code: string }[] }
    throw new ApiError(response.status, body.problem ?? 'Unexpected', body.message ?? response.statusText, body.issues ?? [])
  }

  return (await response.json()) as T
}

/** A small text file the user can save as a starting point (a template to fill in). A byte order mark makes Excel read Arabic correctly. */
export const downloadText = (text: string, fileName: string): void => {
  const url = URL.createObjectURL(new Blob(['﻿', text], { type: 'text/csv;charset=utf-8' }))
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  document.body.appendChild(link)
  link.click()
  link.remove()
  setTimeout(() => URL.revokeObjectURL(url), 60_000)
}
