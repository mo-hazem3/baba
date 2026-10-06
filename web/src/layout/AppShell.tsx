import { Layout, Menu } from 'antd'
import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { useLocation, useNavigate } from 'react-router'
import type { CompanyInfo } from '../api/generated/model'
import { AppHeader } from './AppHeader'

/**
 * The one layout every screen uses (brief section 7.3): top bar, a sidebar of modules with text labels
 * (it mirrors to the right in Arabic), and the page. Without an open company there is no sidebar.
 */
export function AppShell({ company, children }: { company?: CompanyInfo | null; children: ReactNode }) {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const { pathname } = useLocation()

  const items = [
    { key: '/', label: t('nav.summary') },
    { key: '/settings', label: t('nav.settings') },
  ]

  return (
    <Layout className="app-shell">
      <a className="skip-link" href="#main">
        {t('app.skipToContent')}
      </a>
      <Layout.Header className="app-header-bar">
        <AppHeader company={company} />
      </Layout.Header>
      <Layout>
        {company && (
          <Layout.Sider width={230} className="app-sider" theme="light">
            <nav aria-label={t('nav.label')}>
              <Menu
                mode="inline"
                selectedKeys={[pathname]}
                items={items}
                onClick={({ key }) => navigate(key)}
              />
            </nav>
          </Layout.Sider>
        )}
        <Layout.Content id="main" className="app-content" tabIndex={-1}>
          {children}
        </Layout.Content>
      </Layout>
    </Layout>
  )
}
