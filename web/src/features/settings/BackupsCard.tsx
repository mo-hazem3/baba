import { Button, Card } from 'antd'
import { useTranslation } from 'react-i18next'
import { useHost } from '../../api/hooks'
import { useBackup } from '../summary/useBackup'

/** Backups (brief section 9): save a copy of the company now, and where the automatic ones come from. */
export function BackupsCard() {
  const { t } = useTranslation()
  const host = useHost()
  const backup = useBackup()

  return (
    <Card title={t('backups.title')} className="settings-card">
      <p>{t('backups.intro')}</p>
      <ul>
        <li>{t('backups.automaticUpdate')}</li>
        <li>{t('backups.automaticYearEnd')}</li>
        <li>{t('backups.restore')}</li>
      </ul>
      {host.data?.fileDialogs && (
        <div className="form-buttons">
          <Button type="primary" onClick={() => backup.mutate()} loading={backup.isPending}>
            {t('summary.backup')}
          </Button>
        </div>
      )}
    </Card>
  )
}
