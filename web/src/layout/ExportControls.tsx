import { DownOutlined } from '@ant-design/icons'
import { App, Button, Dropdown, Select, Space } from 'antd'
import { useMutation } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { ExportFormat, PrintLayout } from '../api/generated/model'
import { ApiError } from '../api/http'
import { useHost, usePrintSettings } from '../api/hooks'
import { errorMessage } from './errors'

/**
 * Export (brief section 11): the language of the printout, then PDF, Excel or CSV. The PDF is shown in a viewer window,
 * Excel and CSV are saved as files. PDF is only offered where the host can make one (the desktop app).
 */
export function ExportControls({ run }: { run: (format: ExportFormat, layout: PrintLayout) => Promise<void> }) {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const host = useHost()
  const settings = usePrintSettings()
  const [chosen, setChosen] = useState<PrintLayout>()
  const layout = chosen ?? settings.data?.defaultLayout ?? 'Both'

  const exporting = useMutation({
    mutationFn: (format: ExportFormat) => run(format, layout),
    onError: (error) => void message.error(error instanceof ApiError && error.status === 501 ? t('export.pdfUnavailable') : errorMessage(error, t)),
  })

  const pdfAvailable = host.data?.pdfPrinting ?? false

  return (
    <Space wrap>
      <Select
        value={layout}
        onChange={setChosen}
        aria-label={t('export.language')}
        options={[
          { value: 'Both', label: `${t('export.language')}: ${t('export.both')}` },
          { value: 'Arabic', label: `${t('export.language')}: ${t('export.arabic')}` },
          { value: 'English', label: `${t('export.language')}: ${t('export.english')}` },
        ]}
        className="export-language"
      />
      <Button onClick={() => exporting.mutate('Pdf')} disabled={!pdfAvailable} title={pdfAvailable ? undefined : t('export.pdfUnavailable')} loading={exporting.isPending && exporting.variables === 'Pdf'}>
        {t('export.print')}
      </Button>
      <Dropdown
        menu={{
          items: [
            { key: 'Pdf', label: t('export.pdf'), disabled: !pdfAvailable, onClick: () => exporting.mutate('Pdf') },
            { key: 'Xlsx', label: t('export.excel'), onClick: () => exporting.mutate('Xlsx') },
            { key: 'Csv', label: t('export.csv'), onClick: () => exporting.mutate('Csv') },
          ],
        }}
        trigger={['click']}
      >
        <Button>
          {t('export.export')} <DownOutlined />
        </Button>
      </Dropdown>
    </Space>
  )
}
