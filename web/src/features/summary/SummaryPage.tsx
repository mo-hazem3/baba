import { Alert, App, Button, Card, Descriptions } from 'antd'
import { useMutation } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { backupCompany, pickCompanyFileToSave } from '../../api/generated/baba'
import type { CompanyInfo } from '../../api/generated/model'
import { useCountries, useHost } from '../../api/hooks'
import { errorMessage } from '../../layout/errors'
import { PageHeader } from '../../layout/PageHeader'
import { useSettings } from '../../settings/SettingsContext'
import { formatDate, monthName } from '../../utils/format'

/** The home page of an open company. Key balances will appear here once entries can be recorded (Phase 1). */
export function SummaryPage({ company }: { company: CompanyInfo }) {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const { message } = App.useApp()
  const countries = useCountries()
  const host = useHost()
  const ar = settings.language === 'ar'

  const country = countries.data?.find((c) => c.code === company.countryCode)
  const companyName = ar ? company.nameAr : company.nameEn

  const backup = useMutation({
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

  return (
    <div>
      <PageHeader
        title={t('summary.title')}
        help="summary"
        action={
          host.data?.fileDialogs ? (
            <Button onClick={() => backup.mutate()} loading={backup.isPending}>
              {t('summary.backup')}
            </Button>
          ) : undefined
        }
      />

      <Alert type="success" showIcon message={t('summary.welcomeTitle')} description={t('summary.welcomeBody')} className="form-alert" />

      <Card title={t('summary.company')}>
        <Descriptions column={1} bordered size="middle" className="company-details">
          <Descriptions.Item label={t('summary.company')}>{companyName}</Descriptions.Item>
          <Descriptions.Item label={t('summary.country')}>
            {country ? (ar ? country.nameAr : country.nameEn) : company.countryCode}
          </Descriptions.Item>
          <Descriptions.Item label={t('summary.baseCurrency')}>{company.baseCurrencyCode}</Descriptions.Item>
          <Descriptions.Item label={t('summary.fiscalYear')}>
            {monthName(company.fiscalYearStartMonth, settings.language)}
          </Descriptions.Item>
          <Descriptions.Item label={t('summary.modules')}>
            {company.enabledModules.length > 0
              ? company.enabledModules.map((key) => t(`modules.${key}.name`, { defaultValue: key })).join(ar ? '، ' : ', ')
              : t('summary.noModules')}
          </Descriptions.Item>
          <Descriptions.Item label={t('summary.file')}>
            <bdi dir="ltr" className="path">
              {company.filePath}
            </bdi>
          </Descriptions.Item>
        </Descriptions>
      </Card>
    </div>
  )
}
