import { Alert, Button, Card, Descriptions, Statistic } from 'antd'
import { useTranslation } from 'react-i18next'
import type { CompanyInfo } from '../../api/generated/model'
import { useCountries, useDashboard, useHost } from '../../api/hooks'
import { Link } from 'react-router'
import { AmountText } from '../../layout/AmountText'
import { errorMessage } from '../../layout/errors'
import { useBackup } from './useBackup'
import { PageHeader } from '../../layout/PageHeader'
import { useSettings } from '../../settings/SettingsContext'
import { formatDate, monthName } from '../../utils/format'

/** The home page of an open company: the few numbers an owner checks every morning, shortcuts to record something, and the company's details. */
export function SummaryPage({ company }: { company: CompanyInfo }) {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const countries = useCountries()
  const host = useHost()
  const dashboard = useDashboard()
  const ar = settings.language === 'ar'

  const country = countries.data?.find((c) => c.code === company.countryCode)
  const companyName = ar ? company.nameAr : company.nameEn

  const backup = useBackup()

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

      {dashboard.isError && <Alert type="error" showIcon message={errorMessage(dashboard.error, t)} className="form-alert" />}
      {dashboard.data && (
        <>
          <div className="dashboard-cards">
            {[
              { key: 'cash', label: t('summary.cash'), value: dashboard.data.cash },
              { key: 'receivables', label: t('summary.receivables'), value: dashboard.data.receivables },
              { key: 'payables', label: t('summary.payables'), value: dashboard.data.payables },
              { key: 'profit', label: t('summary.profit'), value: dashboard.data.profitThisMonth },
            ].map((card) => (
              <Card key={card.key} className="dashboard-card">
                <Statistic
                  title={card.label}
                  valueRender={() => (
                    <span className="dashboard-amount">
                      <AmountText value={card.value} minorUnits={dashboard.data.minorUnits} /> <small>{dashboard.data.currencyCode}</small>
                    </span>
                  )}
                />
              </Card>
            ))}
          </div>
          <p className="muted">
            {t('summary.month', {
              from: formatDate(dashboard.data.monthStart, settings.digits, settings.hijri),
              to: formatDate(dashboard.data.monthEnd, settings.digits, settings.hijri),
            })}
          </p>
          {dashboard.data.draftVouchers > 0 && (
            <Alert
              type="info"
              showIcon
              className="form-alert"
              message={`${t('summary.drafts')}: ${dashboard.data.draftVouchers}`}
              description={t('summary.draftsBody')}
            />
          )}
        </>
      )}

      <Card title={t('summary.quickActions')} className="settings-card">
        <div className="form-buttons">
          <Link to="/vouchers/receipt/new">
            <Button type="primary">{t('voucher.new.Receipt')}</Button>
          </Link>
          <Link to="/vouchers/payment/new">
            <Button type="primary">{t('voucher.new.Payment')}</Button>
          </Link>
          <Link to="/vouchers/journal/new">
            <Button>{t('voucher.new.Journal')}</Button>
          </Link>
          <Link to="/reports">
            <Button>{t('reports.title')}</Button>
          </Link>
        </div>
      </Card>

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
