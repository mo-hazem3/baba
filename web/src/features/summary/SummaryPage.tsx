import { Alert, App, Button, Card, Descriptions, Statistic } from 'antd'
import { useQueryClient } from '@tanstack/react-query'
import { useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { runDueRecurring, useGetOverdue, useGetReport } from '../../api/generated/baba'
import type { CompanyInfo } from '../../api/generated/model'
import { refreshBooks, useCountries, useDashboard, useHost, useModules } from '../../api/hooks'
import { Link } from 'react-router'
import { AmountText } from '../../layout/AmountText'
import { errorMessage } from '../../layout/errors'
import { useBackup } from './useBackup'
import { PageHeader } from '../../layout/PageHeader'
import { useSettings } from '../../settings/SettingsContext'
import { formatAmount, formatDate, monthName } from '../../utils/format'

// Recurring invoices and entries that came due are made once each time a company is shown for the first time.
let recurringCheckedFor: string | undefined

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
  const modules = useModules()
  const trades = modules.has('sales') || modules.has('purchases')
  const overdue = useGetOverdue({ query: { select: (r) => r.data, enabled: trades } })
  // Stock at or below its reorder level (brief section 10.4): the same list as the reorder report.
  const lowStock = useGetReport('stock-reorder', undefined, { query: { select: (r) => (r.status === 200 ? r.data.rows.length : 0), enabled: modules.has('inventory') } })
  const { message } = App.useApp()
  const queryClient = useQueryClient()

  useEffect(() => {
    if (recurringCheckedFor === company.id) return
    recurringCheckedFor = company.id
    void runDueRecurring().then(async (response) => {
      const made = response.data.items.filter((i) => !i.problemCode).length
      if (made > 0) {
        await refreshBooks(queryClient)
        void message.info(t('recurring.ranOnOpen', { count: made }), 6)
      }
    })
    // eslint-disable-next-line react-hooks/exhaustive-deps -- once per company, however often the page is shown
  }, [company.id])

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
          {overdue.data && overdue.data.invoiceCount > 0 && (
            <Alert
              type="warning"
              showIcon
              className="form-alert"
              message={t('summary.overdueInvoices', {
                count: overdue.data.invoiceCount,
                amount: formatAmount(overdue.data.invoiceAmount, dashboard.data.minorUnits, settings.digits),
                currency: dashboard.data.currencyCode,
              })}
              description={<Link to="/sales">{t('summary.overdueOpenInvoices')}</Link>}
            />
          )}
          {overdue.data && overdue.data.billCount > 0 && (
            <Alert
              type="warning"
              showIcon
              className="form-alert"
              message={t('summary.overdueBills', {
                count: overdue.data.billCount,
                amount: formatAmount(overdue.data.billAmount, dashboard.data.minorUnits, settings.digits),
                currency: dashboard.data.currencyCode,
              })}
              description={<Link to="/purchases">{t('summary.overdueOpenBills')}</Link>}
            />
          )}
          {(lowStock.data ?? 0) > 0 && (
            <Alert
              type="warning"
              showIcon
              className="form-alert"
              message={t('summary.lowStock', { count: lowStock.data })}
              description={<Link to="/reports/stock-reorder">{t('summary.lowStockOpen')}</Link>}
            />
          )}
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
          {modules.has('sales') && (
            <Link to="/documents/sales-invoice/new">
              <Button type="primary">{t('trade.new.SalesInvoice')}</Button>
            </Link>
          )}
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
