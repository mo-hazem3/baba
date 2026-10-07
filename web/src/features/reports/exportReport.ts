import { exportReport } from '../../api/generated/baba'
import type { ExportFormat, PrintLayout } from '../../api/generated/model'
import { ApiError, asBlob } from '../../api/http'
import { downloadBlob, fileNameFrom, openPdf } from '../../utils/download'

export interface ReportParams {
  From?: string
  To?: string
  AsOf?: string
  AccountId?: string
  Comparison?: 'None' | 'PreviousYear'
  Kind?: 'Payment' | 'Receipt' | 'Journal'
  Status?: 'Draft' | 'Posted'
  PartyId?: string
  CostCenterId?: string
}

/** Exports a report (or list) the way the format asks: PDF opens in the viewer, Excel and CSV are saved as files. */
export async function exportAndShow(key: string, params: ReportParams, format: ExportFormat, layout: PrintLayout): Promise<void> {
  const response = await exportReport(key, { ...params, format, layout })
  if (response.status === 501) throw new ApiError(501, 'NotImplemented', 'This host cannot make PDFs.')
  if (response.status !== 200) throw new ApiError(response.status, 'Unexpected', 'The export failed.')

  const blob = asBlob(response.data)
  if (format === 'Pdf') openPdf(blob)
  else downloadBlob(blob, fileNameFrom(response.headers, `${key}.${format === 'Xlsx' ? 'xlsx' : 'csv'}`))
}
