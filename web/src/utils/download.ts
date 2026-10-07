/** The file name the server suggests (Content-Disposition), or a fallback. Handles the plain and the UTF-8 forms. */
export const fileNameFrom = (headers: Headers, fallback: string): string => {
  const disposition = headers.get('content-disposition') ?? ''
  const utf8 = /filename\*=UTF-8''([^;]+)/i.exec(disposition)
  if (utf8?.[1]) return decodeURIComponent(utf8[1])
  const plain = /filename="?([^";]+)"?/i.exec(disposition)
  return plain?.[1] ?? fallback
}

/** Saves a file the app made (Excel, CSV). In the desktop app the host shows its own Save dialog. */
export const downloadBlob = (blob: Blob, fileName: string): void => {
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  document.body.appendChild(link)
  link.click()
  link.remove()
  setTimeout(() => URL.revokeObjectURL(url), 60_000)
}

/** Shows a PDF in the viewer window, where it can be read, printed or saved. */
export const openPdf = (blob: Blob): void => {
  const url = URL.createObjectURL(blob)
  window.open(url, '_blank')
  setTimeout(() => URL.revokeObjectURL(url), 5 * 60_000)
}
