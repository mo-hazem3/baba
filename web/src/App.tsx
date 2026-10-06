import { Spin } from 'antd'
import { useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { Navigate, Route, Routes } from 'react-router'
import { useCurrentCompany } from './api/hooks'
import { NewCompanyWizard } from './features/company/NewCompanyWizard'
import { SettingsPage } from './features/settings/SettingsPage'
import { StartPage } from './features/start/StartPage'
import { SummaryPage } from './features/summary/SummaryPage'
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
        <Route path="/settings" element={<SettingsPage />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </AppShell>
  )
}
