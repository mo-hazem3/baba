import { App } from 'antd'
import { useMutation } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { backupCompany, pickCompanyFileToSave } from '../../api/generated/baba'
import { useCurrentCompany } from '../../api/hooks'
import { errorMessage } from '../../layout/errors'
import { useSettings } from '../../settings/SettingsContext'
import { formatDate } from '../../utils/format'

/** "Back up now": asks where to save with the normal Windows dialog, then writes a complete copy of the company (brief section 9). */
export function useBackup() {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const { message } = App.useApp()
  const company = useCurrentCompany()
  const companyName = company.data ? (settings.language === 'ar' ? company.data.nameAr : company.data.nameEn) : ''

  return useMutation({
    mutationFn: async () => {
      const suggestion = t('summary.backupName', {
        name: companyName,
        date: formatDate(new Date(), 'western').replace(/\//g, '-'),
      })
      const picked = await pickCompanyFileToSave({ suggestedFileName: `${suggestion}.baba` })
      const path = picked.status === 200 ? picked.data.path : null
      if (!path) return null // the user cancelled the dialog
      await backupCompany({ destinationPath: path })
      return path
    },
    onSuccess: (path) => {
      if (path) void message.success(t('summary.backupSaved', { path }))
    },
    onError: (error) => void message.error(errorMessage(error, t)),
  })
}
