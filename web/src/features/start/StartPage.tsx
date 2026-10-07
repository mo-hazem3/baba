import { App, Button, Card, Typography } from 'antd'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router'
import { pickCompanyFileToOpen, pickCompanyFileToSave, removeRecentFile, restoreBackup } from '../../api/generated/baba'
import type { RecentFileDto } from '../../api/generated/model'
import { fetchStartupFile, refreshRecentFiles, startupFileOnce, useHost, useRecentFiles } from '../../api/hooks'
import { ApiError } from '../../api/http'
import { EmptyState } from '../../layout/EmptyState'
import { errorMessage } from '../../layout/errors'
import { PageHeader } from '../../layout/PageHeader'
import { useSettings } from '../../settings/SettingsContext'
import { formatDate } from '../../utils/format'
import { OpenCompanyModal, type OpenTarget } from './OpenCompanyModal'

const fileName = (path: string) => path.split(/[\\/]/).pop()?.replace(/\.baba$/i, '') ?? path

/** The first screen when no company is open: recent files, New company, Open file (brief section 9). */
export function StartPage() {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const { message } = App.useApp()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const host = useHost()
  const recent = useRecentFiles()
  const [target, setTarget] = useState<OpenTarget | null>(null)

  // A file passed on start (double-click on a .baba file) goes straight to the password step.
  useEffect(() => {
    let cancelled = false
    void startupFileOnce().then((path) => {
      if (path && !cancelled) setTarget({ path, name: fileName(path) })
    })
    const onStartupFile = () =>
      void fetchStartupFile().then((path) => path && setTarget({ path, name: fileName(path) }))
    window.addEventListener('baba:startup-file', onStartupFile)
    return () => {
      cancelled = true
      window.removeEventListener('baba:startup-file', onStartupFile)
    }
  }, [])

  const pick = useMutation({
    mutationFn: () => pickCompanyFileToOpen(),
    onSuccess: (response) => {
      const path = response.status === 200 ? response.data.path : null
      if (path) setTarget({ path, name: fileName(path) })
    },
    onError: (error) => void message.error(errorMessage(error, t)),
  })

  // Restoring: choose the backup, choose where the restored company goes, and it is ready to open with its password.
  const restore = useMutation({
    mutationFn: async () => {
      const picked = await pickCompanyFileToOpen()
      const backupPath = picked.status === 200 ? picked.data.path : null
      if (!backupPath) return null // cancelled
      const where = await pickCompanyFileToSave({ suggestedFileName: `${fileName(backupPath)} ${t('start.restoredSuffix')}.baba` })
      const destinationPath = where.status === 200 ? where.data.path : null
      if (!destinationPath) return null
      const response = await restoreBackup({ backupPath, destinationPath })
      return response.status === 200 ? response.data.path : null
    },
    onSuccess: (path) => {
      if (path) {
        void message.success(t('start.restored'))
        setTarget({ path, name: fileName(path) })
      }
    },
    onError: (error) => {
      const code = error instanceof ApiError ? error.issues[0]?.code : undefined
      void message.error(code ? t(`start.issues.${code}`, { defaultValue: errorMessage(error, t) }) : errorMessage(error, t))
    },
  })

  const remove = useMutation({
    mutationFn: (path: string) => removeRecentFile({ path }),
    onSuccess: () => refreshRecentFiles(queryClient),
  })

  const openFile = () => {
    if (host.data?.fileDialogs) pick.mutate()
    else setTarget({ path: null, name: '' }) // no file dialog here: ask for the location by typing it
  }

  const files: RecentFileDto[] = recent.data ?? []

  return (
    <div className="start-page">
      <PageHeader title={t('start.title')} help="start" />
      <Typography.Paragraph className="start-subtitle">{t('start.subtitle')}</Typography.Paragraph>

      <div className="form-buttons">
        <Button type="primary" size="large" onClick={() => navigate('/new-company')}>
          {t('start.newCompany')}
        </Button>
        <Button size="large" onClick={openFile} loading={pick.isPending}>
          {t('start.openFile')}
        </Button>
        {host.data?.fileDialogs && (
          <Button size="large" onClick={() => restore.mutate()} loading={restore.isPending}>
            {t('start.restore')}
          </Button>
        )}
      </div>

      <Card title={t('start.recent')} className="recent-card">
        {files.length === 0 ? (
          <EmptyState title={t('start.emptyTitle')} body={t('start.emptyBody')} />
        ) : (
          <ul className="recent-list">
            {files.map((file) => (
              <li key={file.path} className="recent-item">
                <div className="recent-info">
                  <Typography.Text strong>{file.name}</Typography.Text>
                  <Typography.Text type="secondary" className="path">
                    <bdi dir="ltr">{file.path}</bdi>
                  </Typography.Text>
                  {file.exists ? (
                    <Typography.Text type="secondary">
                      {t('start.lastOpened', { date: formatDate(file.lastOpenedAt, settings.digits) })}
                    </Typography.Text>
                  ) : (
                    <Typography.Text type="danger">{t('start.fileMissing')}</Typography.Text>
                  )}
                </div>
                <div className="recent-actions">
                  {file.exists && (
                    <Button
                      type="primary"
                      aria-label={t('start.openRecent', { name: file.name })}
                      onClick={() => setTarget({ path: file.path, name: file.name })}
                    >
                      {t('common.open')}
                    </Button>
                  )}
                  <Button
                    aria-label={t('start.removeRecent', { name: file.name })}
                    onClick={() => remove.mutate(file.path)}
                  >
                    {t('common.remove')}
                  </Button>
                </div>
              </li>
            ))}
          </ul>
        )}
      </Card>

      <OpenCompanyModal target={target} onClose={() => setTarget(null)} />
    </div>
  )
}
