import { Spin } from 'antd'
import { useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { Navigate, Route, Routes } from 'react-router'
import { useCurrentCompany } from './api/hooks'
import { ChartOfAccountsPage } from './features/accounts/ChartOfAccountsPage'
import { EmployeesPage } from './features/payroll/EmployeesPage'
import { PayrollPage } from './features/payroll/PayrollPage'
import { PayrollRunPage } from './features/payroll/PayrollRunPage'
import { AssetsPage } from './features/assets/AssetsPage'
import { StockPage } from './features/inventory/StockPage'
import { WarehousesPage } from './features/inventory/WarehousesPage'
import { CostCentersPage } from './features/costcenters/CostCentersPage'
import { BankPage } from './features/bank/BankPage'
import { ExchangeRatesPage } from './features/rates/ExchangeRatesPage'
import { ReconcilePage } from './features/bank/ReconcilePage'
import { DocumentFormPage, DocumentOpenPage } from './features/trade/DocumentFormPage'
import { DocumentListPage } from './features/trade/DocumentListPage'
import { ProductsPage } from './features/trade/ProductsPage'
import { TaxCodesPage } from './features/trade/TaxCodesPage'
import { SettlementPage } from './features/trade/SettlementPage'
import { RecurringPage } from './features/recurring/RecurringPage'
import { PartiesPage } from './features/parties/PartiesPage'
import { OpeningPage } from './features/vouchers/OpeningPage'
import { YearEndPage } from './features/yearend/YearEndPage'
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
        <Route path="/customers" element={<PartiesPage kind="Customer" />} />
        <Route path="/suppliers" element={<PartiesPage kind="Supplier" />} />
        <Route path="/sales" element={<DocumentListPage side="sales" />} />
        <Route path="/purchases" element={<DocumentListPage side="purchases" />} />
        <Route path="/products" element={<ProductsPage />} />
        <Route path="/tax-codes" element={<TaxCodesPage />} />
        <Route path="/assets" element={<AssetsPage />} />
        <Route path="/employees" element={<EmployeesPage />} />
        <Route path="/payroll" element={<PayrollPage />} />
        <Route path="/payroll/:id" element={<PayrollRunPage />} />
        <Route path="/stock" element={<StockPage />} />
        <Route path="/warehouses" element={<WarehousesPage />} />
        <Route path="/recurring" element={<RecurringPage />} />
        <Route path="/settlements/receive" element={<SettlementPage side="customer" />} />
        <Route path="/settlements/pay" element={<SettlementPage side="supplier" />} />
        <Route path="/documents/open/:id" element={<DocumentOpenPage />} />
        <Route path="/documents/:segment/:id" element={<DocumentFormPage />} />
        <Route path="/cost-centers" element={<CostCentersPage />} />
        <Route path="/vouchers/payment" element={<VoucherListPage kind="Payment" />} />
        <Route path="/vouchers/receipt" element={<VoucherListPage kind="Receipt" />} />
        <Route path="/vouchers/journal" element={<VoucherListPage kind="Journal" />} />
        <Route path="/vouchers/transfer" element={<VoucherListPage kind="Transfer" />} />
        <Route path="/vouchers/opening" element={<OpeningPage />} />
        <Route path="/bank" element={<BankPage />} />
        <Route path="/bank/:accountId/reconcile" element={<ReconcilePage />} />
        <Route path="/year-end" element={<YearEndPage />} />
        <Route path="/exchange-rates" element={<ExchangeRatesPage />} />
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
