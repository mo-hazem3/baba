import { Spin } from 'antd'
import { useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { Navigate, Route, Routes } from 'react-router'
import { useCurrentCompany } from './api/hooks'
import { ChartOfAccountsPage } from './features/accounts/ChartOfAccountsPage'
import { NewCompanyWizard } from './features/company/NewCompanyWizard'
import { ReportPage } from './features/reports/ReportPage'
import { ReportsPage } from './features/reports/ReportsPage'
import { SettingsPage } from './features/settings/SettingsPage'
import { StartPage } from './features/start/StartPage'
import { SummaryPage } from './features/summary/SummaryPage'
import { VoucherFormPage } from './features/vouchers/VoucherFormPage'
import { VoucherListPage } from './features/vouchers/VoucherListPage'
import { VoucherOpenPage } from './features/vouchers/VoucherOpenPage'
import { AppShell } from './layout/AppShell'
import { useSettings } from './settings/SettingsContext'

/** Without an open company: the start screen and the new-company wizard. With one: the app itself. */
export function App() {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const company = useCurrentCompany()

  const name = company.data ? (settings.language === 'ar' ? company.data.nameAr : company.data.nameEn) : null

  // The desktop window shows the page title, so it names the open company.
  useEffect(() => {
    document.title = name ? `${name} · ${t('app.name')}` : t('app.name')
  }, [name, t])

  if (company.isPending) return <Spin size="large" className="page-spinner" />

  if (!company.data) {
    return (
      <AppShell>
        <Routes>
          <Route path="/" element={<StartPage />} />
          <Route path="/new-company" element={<NewCompanyWizard />} />
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </AppShell>
    )
  }

  return (
    <AppShell company={company.data}>
      <Routes>
        <Route path="/" element={<SummaryPage company={company.data} />} />
        <Route path="/accounts" element={<ChartOfAccountsPage />} />
        <Route path="/vouchers/payment" element={<VoucherListPage kind="Payment" />} />
        <Route path="/vouchers/receipt" element={<VoucherListPage kind="Receipt" />} />
        <Route path="/vouchers/journal" element={<VoucherListPage kind="Journal" />} />
        <Route path="/vouchers/open/:id" element={<VoucherOpenPage />} />
        <Route path="/vouchers/:kind/:id" element={<VoucherFormPage />} />
        <Route path="/reports" element={<ReportsPage />} />
        <Route path="/reports/:key" element={<ReportPage />} />
        <Route path="/settings" element={<SettingsPage />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </AppShell>
  )
}
