import { Alert, App, Button, Card, InputNumber, Select, Table } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { deleteExchangeRate, setExchangeRate } from '../../api/generated/baba'
import type { CurrencyRateDto } from '../../api/generated/model'
import { ApiError } from '../../api/http'
import { refreshBooks, useCurrencies, useCurrentCompany, useExchangeRates } from '../../api/hooks'
import { DateField } from '../../layout/DateField'
import { EmptyState } from '../../layout/EmptyState'
import { errorMessage } from '../../layout/errors'
import { PageHeader } from '../../layout/PageHeader'
import { useSettings } from '../../settings/SettingsContext'
import { formatDate, parseIsoDate, toIsoDate } from '../../utils/format'

/**
 * Exchange rates (brief section 5): for each foreign currency, how many units of the company's currency it was worth from a date on.
 * A voucher or document in that currency starts from the latest rate on or before its date.
 */
export function ExchangeRatesPage() {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const company = useCurrentCompany()
  const currencies = useCurrencies()
  const rates = useExchangeRates()
  const base = company.data?.baseCurrencyCode ?? ''

  const [currency, setCurrency] = useState<string>()
  const [date, setDate] = useState(toIsoDate(new Date()))
  const [rate, setRate] = useState<number | null>(null)
  const [problem, setProblem] = useState<string>()

  const save = useMutation({
    mutationFn: () => setExchangeRate({ currencyCode: currency ?? '', date, rate: rate ?? 0 }),
    onSuccess: async () => {
      setProblem(undefined)
      setRate(null)
      await refreshBooks(queryClient)
      void message.success(t('rates.saved'))
    },
    onError: (error) => {
      const code = error instanceof ApiError ? error.issues[0]?.code : undefined
      setProblem(code ? t(`rates.issues.${code}`, { defaultValue: errorMessage(error, t) }) : errorMessage(error, t))
    },
  })

  const remove = useMutation({
    mutationFn: (id: string) => deleteExchangeRate(id),
    onSuccess: () => refreshBooks(queryClient),
    onError: (error) => void message.error(errorMessage(error, t)),
  })

  const options = (currencies.data ?? [])
    .filter((c) => c.code !== base)
    .map((c) => ({ value: c.code, label: `${c.code} — ${settings.language === 'ar' ? c.nameAr : c.nameEn}` }))

  const columns: ColumnsType<CurrencyRateDto> = [
    { title: t('rates.currency'), dataIndex: 'currencyCode', width: 130, render: (code: string) => <span dir="ltr">{code}</span> },
    { title: t('rates.from'), key: 'date', width: 170, render: (_: unknown, r) => formatDate(parseIsoDate(r.date), settings.digits, settings.hijri) },
    {
      title: t('rates.worth', { currency: base }),
      key: 'rate',
      align: 'end',
      render: (_: unknown, r) => (
        <bdi dir="ltr" className="numbers">
          {r.rate.toFixed(6).replace(/0+$/, '').replace(/\.$/, '.0')}
        </bdi>
      ),
    },
    {
      title: <span className="visually-hidden">{t('voucher.rowActions')}</span>,
      key: 'actions',
      width: 120,
      render: (_: unknown, r) => (
        <Button type="link" danger onClick={() => remove.mutate(r.id)} aria-label={`${t('rates.delete')} ${r.currencyCode} ${r.date}`}>
          {t('rates.delete')}
        </Button>
      ),
    },
  ]

  return (
    <div>
      <PageHeader title={t('rates.title')} help="rates" crumbs={[{ label: t('breadcrumb.home'), to: '/' }, { label: t('rates.title') }]} />
      <Alert type="info" showIcon className="form-alert" message={t('rates.intro', { currency: base })} />

      <Card title={t('rates.add')} className="reconcile-summary">
        <div className="voucher-header">
          <div className="field">
            <label htmlFor="rate-currency">{t('rates.currency')}</label>
            <Select id="rate-currency" value={currency} onChange={setCurrency} options={options} showSearch optionFilterProp="label" aria-label={t('rates.currency')} className="currency-select" />
          </div>
          <div className="field">
            <label htmlFor="rate-date">{t('rates.from')}</label>
            <DateField id="rate-date" value={date} onChange={(v) => v && setDate(v)} ariaLabel={t('rates.from')} />
          </div>
          <div className="field">
            <label htmlFor="rate-value">{t('rates.worth', { currency: base })}</label>
            <InputNumber id="rate-value" value={rate} onChange={(v) => setRate(typeof v === 'number' ? v : null)} min={0} precision={6} controls={false} className="amount-input" aria-label={t('rates.worth', { currency: base })} />
          </div>
          <div className="field">
            <label>&nbsp;</label>
            <Button type="primary" onClick={() => save.mutate()} loading={save.isPending} disabled={!currency || rate === null}>
              {t('common.save')}
            </Button>
          </div>
        </div>
        {problem && <div className="cell-error">{problem}</div>}
      </Card>

      {rates.isSuccess && (rates.data?.length ?? 0) === 0 ? (
        <EmptyState title={t('rates.emptyTitle')} body={t('rates.emptyBody')} />
      ) : (
        <Table<CurrencyRateDto> columns={columns} dataSource={rates.data ?? []} rowKey="id" loading={rates.isPending} pagination={false} size="middle" bordered />
      )}
    </div>
  )
}
