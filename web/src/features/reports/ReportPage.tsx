import { Alert, Checkbox, Segmented, Spin } from 'antd'
import { useQuery } from '@tanstack/react-query'
import { useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { Navigate, useParams, useSearchParams } from 'react-router'
import { getReport } from '../../api/generated/baba'
import { ApiError } from '../../api/http'
import { useAccounts, useCostCenters, useCurrentCompany, useParties, useProducts, useWarehouses } from '../../api/hooks'
import { DateField } from '../../layout/DateField'
import { errorMessage } from '../../layout/errors'
import { ExportControls } from '../../layout/ExportControls'
import { PageHeader } from '../../layout/PageHeader'
import { useSettings } from '../../settings/SettingsContext'
import { toIsoDate } from '../../utils/format'
import { AccountSelect } from '../accounting/AccountSelect'
import { CostCenterSelect, PartySelect, ProductSelect, WarehouseSelect } from '../accounting/LookupSelects'
import { exportAndShow, type ReportParams } from './exportReport'
import { ReportTable } from './ReportTable'
import { isReportKey, matchPreset, presetRange, reportInputs, reportSubtitle, reportTitle, type RangePreset } from './reportModel'

const presets: RangePreset[] = ['thisMonth', 'lastMonth', 'thisYear', 'lastYear', 'all']

/**
 * One report with its filters (brief section 11): a date range or an "as of" date, an account for a statement, a comparison with
 * last year, and export. The choices live in the address, so drill-down, the Back button and a bookmark all work.
 */
export function ReportPage() {
  const { key } = useParams()
  if (!isReportKey(key)) return <Navigate to="/reports" replace />
  return <Report reportKey={key} />
}

function Report({ reportKey }: { reportKey: keyof typeof reportInputs }) {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const company = useCurrentCompany()
  const accounts = useAccounts()
  const parties = useParties()
  const costCenters = useCostCenters()
  const warehouses = useWarehouses()
  const products = useProducts()
  const [params, setParams] = useSearchParams()
  const inputs = reportInputs[reportKey]
  const fiscalStart = company.data?.fiscalYearStartMonth ?? 1

  // Without choices in the address, a report starts on the current fiscal year (or today, for a balance sheet).
  const defaults = useMemo(
    () => ({ ...presetRange('thisYear', new Date(), fiscalStart), asOf: toIsoDate(new Date()) }),
    [fiscalStart],
  )
  const all = params.get('all') === '1' // the user chose "all dates": no start and no end
  const from = params.get('from') ?? (all || params.has('to') ? undefined : defaults.from)
  const to = params.get('to') ?? (all || params.has('from') ? undefined : defaults.to)
  const asOf = params.get('asOf') ?? defaults.asOf
  const accountId = params.get('accountId') ?? undefined
  const partyId = params.get('partyId') ?? undefined
  const costCenterId = params.get('costCenterId') ?? undefined
  const warehouseId = params.get('warehouseId') ?? undefined
  const productId = params.get('productId') ?? undefined
  const compare = params.get('comparison') === 'PreviousYear'

  const set = (changes: Record<string, string | undefined>) =>
    setParams((current) => {
      const next = new URLSearchParams(current)
      // Once the dates are chosen explicitly they stay in the address, even when one end is left open.
      if (inputs.range && !next.has('from') && !next.has('to') && !all) {
        if (from) next.set('from', from)
        if (to) next.set('to', to)
      }
      if ('from' in changes || 'to' in changes) next.delete('all')
      for (const [name, value] of Object.entries(changes)) {
        if (value === undefined || value === '') next.delete(name)
        else next.set(name, value)
      }
      return next
    }, { replace: true })

  const requestParams: ReportParams = {
    ...(inputs.range ? { From: from, To: to } : {}),
    ...(inputs.asOf ? { AsOf: asOf } : {}),
    ...(inputs.account ? { AccountId: accountId } : {}),
    ...(inputs.party ? { PartyId: partyId } : {}),
    ...(inputs.costCenter ? { CostCenterId: costCenterId } : {}),
    ...(inputs.warehouse ? { WarehouseId: warehouseId } : {}),
    ...(inputs.product ? { ProductId: productId } : {}),
    ...(inputs.comparison && compare ? { Comparison: 'PreviousYear' } : {}),
  }

  const ready = (!inputs.account || accountId !== undefined) && (!inputs.party || partyId !== undefined)
  const query = useQuery({
    queryKey: ['/api/reports', reportKey, requestParams],
    enabled: ready,
    queryFn: async () => {
      const response = await getReport(reportKey, requestParams)
      if (response.status !== 200) throw new ApiError(response.status, 'NotFound', 'No such report.')
      return response.data
    },
  })

  const chosenPreset = matchPreset({ from, to }, new Date(), fiscalStart)

  return (
    <div>
      <PageHeader
        title={query.data ? reportTitle(query.data, settings.language) : t(`reports.names.${reportKey}`)}
        help="report"
        crumbs={[{ label: t('breadcrumb.home'), to: '/' }, { label: t('reports.title'), to: '/reports' }, { label: t(`reports.names.${reportKey}`) }]}
        action={query.data && <ExportControls run={(format, layout) => exportAndShow(reportKey, requestParams, format, layout)} />}
      />

      <div className="filter-bar">
        {inputs.account && (
          <div className="field field-wide">
            <label htmlFor="report-account">{t('accounting.account')}</label>
            <AccountSelect
              id="report-account"
              value={accountId}
              onChange={(id) => set({ accountId: id })}
              accounts={accounts.data ?? []}
              usableOnly={false}
              placeholder={t('reports.chooseAccount')}
            />
          </div>
        )}
        {inputs.party && (
          <div className="field field-wide">
            <label htmlFor="report-party">{t('reports.party')}</label>
            <PartySelect
              id="report-party"
              value={partyId}
              onChange={(id) => set({ partyId: id })}
              parties={parties.data ?? []}
              placeholder={t('reports.chooseParty')}
              ariaLabel={t('reports.party')}
              allowClear={false}
            />
          </div>
        )}
        {inputs.costCenter && ((costCenters.data?.length ?? 0) > 0 || costCenterId) && (
          <div className="field field-wide">
            <label htmlFor="report-cost-center">{t('reports.costCenter')}</label>
            <CostCenterSelect
              id="report-cost-center"
              value={costCenterId}
              onChange={(id) => set({ costCenterId: id })}
              costCenters={costCenters.data ?? []}
              placeholder={t('reports.allCostCenters')}
              ariaLabel={t('reports.costCenter')}
            />
          </div>
        )}
        {inputs.warehouse && (warehouses.data?.length ?? 0) > 1 && (
          <div className="field field-wide">
            <label htmlFor="report-warehouse">{t('inventory.warehouse')}</label>
            <WarehouseSelect
              id="report-warehouse"
              value={warehouseId}
              onChange={(id) => set({ warehouseId: id })}
              warehouses={warehouses.data ?? []}
              placeholder={t('inventory.allWarehouses')}
              ariaLabel={t('inventory.warehouse')}
            />
          </div>
        )}
        {inputs.product && (
          <div className="field field-wide">
            <label htmlFor="report-product">{t('trade.product')}</label>
            <ProductSelect
              id="report-product"
              value={productId}
              onChange={(id) => set({ productId: id })}
              products={(products.data ?? []).filter((p) => p.isStockItem)}
              placeholder={t('inventory.allProducts')}
              ariaLabel={t('trade.product')}
            />
          </div>
        )}
        {inputs.range && (
          <>
            <Segmented
              value={chosenPreset ?? 'custom'}
              onChange={(value) => {
                const range = presetRange(value as RangePreset, new Date(), fiscalStart)
                setParams((current) => {
                  const next = new URLSearchParams(current)
                  next.delete('from')
                  next.delete('to')
                  if (range.from) next.set('from', range.from)
                  if (range.to) next.set('to', range.to)
                  if (!range.from && !range.to) next.set('all', '1') // "all dates" is its own choice, not "no choice"
                  return next
                }, { replace: true })
              }}
              options={[...presets.map((p) => ({ value: p, label: t(`reports.presets.${p}`) })), ...(chosenPreset ? [] : [{ value: 'custom', label: t('reports.presets.custom'), disabled: true }])]}
              aria-label={t('reports.dates')}
            />
            <div className="field">
              <label htmlFor="report-from">{t('reports.from')}</label>
              <DateField id="report-from" value={from} onChange={(v) => set({ from: v })} allowEmpty ariaLabel={t('reports.from')} />
            </div>
            <div className="field">
              <label htmlFor="report-to">{t('reports.to')}</label>
              <DateField id="report-to" value={to} onChange={(v) => set({ to: v })} allowEmpty ariaLabel={t('reports.to')} />
            </div>
          </>
        )}
        {inputs.asOf && (
          <div className="field">
            <label htmlFor="report-asof">{t('reports.asOf')}</label>
            <DateField id="report-asof" value={asOf} onChange={(v) => set({ asOf: v })} ariaLabel={t('reports.asOf')} />
          </div>
        )}
        {inputs.comparison && (
          <Checkbox checked={compare} onChange={(e) => set({ comparison: e.target.checked ? 'PreviousYear' : undefined })}>
            {t('reports.compare')}
          </Checkbox>
        )}
      </div>

      {!ready && <Alert type="info" showIcon message={t(inputs.party ? 'reports.choosePartyFirst' : 'reports.chooseAccountFirst')} />}
      {query.isPending && ready && <Spin size="large" className="page-spinner" />}
      {query.isError && <Alert type="error" showIcon message={errorMessage(query.error, t)} />}
      {query.data && (
        <>
          <p className="muted report-subtitle">
            {reportSubtitle(query.data, settings.language)} · {query.data.currencyCode}
          </p>
          <ReportTable report={query.data} />
        </>
      )}
    </div>
  )
}
