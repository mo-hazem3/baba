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

  // Without a company there is nothing to go to, so the menu only lists the modules of an open one.
  const modules = new Set(company?.enabledModules ?? [])
  const items = [
    { key: '/', label: t('nav.summary') },
    { key: '/accounts', label: t('nav.accounts') },
    { key: '/vouchers/payment', label: t('nav.payments') },
    { key: '/vouchers/receipt', label: t('nav.receipts') },
    { key: '/vouchers/journal', label: t('nav.journal') },
    ...(modules.has('bank-cash')
      ? [
          { key: '/bank', label: t('nav.bank') },
          { key: '/vouchers/transfer', label: t('nav.transfers') },
        ]
      : []),
    ...(modules.has('customers-suppliers')
      ? [
          { key: '/customers', label: t('nav.customers') },
          { key: '/suppliers', label: t('nav.suppliers') },
        ]
      : []),
    ...(modules.has('sales') ? [{ key: '/sales', label: t('nav.sales') }] : []),
    ...(modules.has('purchases') ? [{ key: '/purchases', label: t('nav.purchases') }] : []),
    ...(modules.has('sales') || modules.has('purchases') ? [{ key: '/products', label: t('nav.products') }] : []),
    ...(modules.has('cost-centers') ? [{ key: '/cost-centers', label: t('nav.costCenters') }] : []),
    { key: '/recurring', label: t('nav.recurring') },
    { key: '/reports', label: t('nav.reports') },
    { key: '/vouchers/opening', label: t('nav.opening') },
    { key: '/year-end', label: t('nav.yearEnd') },
    { key: '/exchange-rates', label: t('nav.exchangeRates') },
    { key: '/settings', label: t('nav.settings') },
  ]
  // A page deeper in a module (a voucher, a report) keeps its module highlighted.
  const selected = items.map((i) => i.key).filter((key) => key === '/' ? pathname === '/' : pathname === key || pathname.startsWith(key + '/'))

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
                selectedKeys={selected}
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
