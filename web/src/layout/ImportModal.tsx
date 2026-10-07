import { Alert, App, Button, Modal } from 'antd'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { ImportResult } from '../api/generated/model'
import { refreshBooks } from '../api/hooks'
import { errorMessage } from './errors'
import { downloadBlob } from '../utils/download'
import { uploadFile } from '../utils/upload'

/**
 * Importing from a CSV or Excel file (brief section 10.2): choose the file and it is read at once. If any row is wrong nothing is
 * imported and every wrong row is named, so the file can be fixed and chosen again.
 */
export function ImportModal({
  open,
  onClose,
  title,
  intro,
  columns,
  url,
  templateKey,
}: {
  open: boolean
  onClose: () => void
  title: string
  intro: string
  /** The columns the file may have, one line of text. */
  columns: string
  url: string
  /** Which Excel template to offer for download (a header row and one example row to fill in). */
  templateKey?: string
}) {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const input = useRef<HTMLInputElement>(null)
  const [result, setResult] = useState<ImportResult>()

  const upload = useMutation({
    mutationFn: (file: File) => uploadFile<ImportResult>(url, file),
    onSuccess: async (data) => {
      setResult(data)
      if (data.issues.length === 0) {
        await refreshBooks(queryClient)
        void message.success(t('import.done', { count: data.imported }))
      }
    },
    onError: (error) => void message.error(errorMessage(error, t)),
  })

  const close = () => {
    setResult(undefined)
    upload.reset()
    onClose()
  }

  // Problems found while importing use the codes of the screens they belong to (an account, a party, a product, a rate, a voucher line).
  const issueText = (code: string) =>
    t(
      [
        `import.issues.${code}`,
        `accounts.issues.${code}`,
        `parties.issues.${code}`,
        `trade.issues.${code}`,
        `rates.issues.${code}`,
        `voucher.issues.${code}`,
        `inventory.issues.${code}`,
        `assets.issues.${code}`,
      ],
      { defaultValue: code },
    )

  const downloadTemplate = useMutation({
    mutationFn: async () => {
      const response = await fetch(`/api/import/templates/${templateKey}`, { credentials: 'same-origin' })
      if (!response.ok) throw new Error('template')
      downloadBlob(await response.blob(), `${templateKey}-template.xlsx`)
    },
    onError: (error) => void message.error(errorMessage(error, t)),
  })

  const done = result !== undefined && result.issues.length === 0

  return (
    <Modal
      open={open}
      title={title}
      onCancel={close}
      destroyOnHidden
      width={640}
      footer={
        <Button type="primary" onClick={close}>
          {done ? t('common.close') : t('common.cancel')}
        </Button>
      }
    >
      <p>{intro}</p>
      <p className="muted">
        {t('import.columns')}: <bdi dir="ltr">{columns}</bdi>
      </p>
      <input
        ref={input}
        type="file"
        accept=".csv,.xlsx,text/csv,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        hidden
        data-testid="import-file"
        onChange={(e) => {
          const file = e.target.files?.[0]
          e.target.value = '' // so the same file can be chosen again after fixing it
          if (file) {
            setResult(undefined)
            upload.mutate(file)
          }
        }}
      />
      <div className="form-buttons">
        <Button type="primary" onClick={() => input.current?.click()} loading={upload.isPending}>
          {t('import.choose')}
        </Button>
        {templateKey && (
          <Button onClick={() => downloadTemplate.mutate()} loading={downloadTemplate.isPending}>
            {t('import.template')}
          </Button>
        )}
      </div>

      {done && (
        <Alert
          type="success"
          showIcon
          message={t('import.done', { count: result.imported })}
          description={result.skipped > 0 ? t('import.skipped', { count: result.skipped }) : undefined}
        />
      )}
      {result && result.issues.length > 0 && (
        <Alert
          type="error"
          showIcon
          message={t('import.failed')}
          description={
            <ul className="issue-list">
              {result.issues.slice(0, 30).map((issue, i) => (
                <li key={i}>{issue.row > 0 ? t('import.row', { row: issue.row, problem: issueText(issue.code) }) : issueText(issue.code)}</li>
              ))}
              {result.issues.length > 30 && <li>{t('import.more', { count: result.issues.length - 30 })}</li>}
            </ul>
          }
        />
      )}
    </Modal>
  )
}
