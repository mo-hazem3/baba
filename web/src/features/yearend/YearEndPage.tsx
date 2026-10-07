import { Alert, App, Button, Card, Space, Table, Tag } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { closeFiscalYear, reopenFiscalYear } from '../../api/generated/baba'
import type { FiscalYearDto } from '../../api/generated/model'
import { ApiError } from '../../api/http'
import { refreshBooks, useCurrencies, useCurrentCompany, useFiscalYears } from '../../api/hooks'
import { AmountText } from '../../layout/AmountText'
import { errorMessage } from '../../layout/errors'
import { PageHeader } from '../../layout/PageHeader'
import { useSettings } from '../../settings/SettingsContext'
import { formatDate, parseIsoDate } from '../../utils/format'

/**
 * Year-end (brief section 10.2): closing a fiscal year brings its revenue and expenses to zero with one closing entry, puts the profit
 * into retained earnings, and locks the year's months. A copy of the company file is saved first. A closed year can be reopened as
 * long as no later year has been closed.
 */
export function YearEndPage() {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const { message, modal } = App.useApp()
  const queryClient = useQueryClient()
  const company = useCurrentCompany()
  const currencies = useCurrencies()
  const years = useFiscalYears()
  const minorUnits = currencies.data?.find((c) => c.code === company.data?.baseCurrencyCode)?.minorUnits ?? 2
  const latestClosed = years.data?.find((y) => y.isClosed)?.year // the list is newest first

  const explain = (error: unknown) => {
    const code = error instanceof ApiError ? error.issues[0]?.code : undefined
    void message.error(code ? t(`yearEnd.issues.${code}`, { defaultValue: errorMessage(error, t) }) : errorMessage(error, t))
  }

  const close = useMutation({
    mutationFn: (year: number) => closeFiscalYear(year),
    onSuccess: async (response) => {
      await refreshBooks(queryClient)
      if (response.status === 200)
        modal.success({
          title: t('yearEnd.closedTitle', { year: response.data.year.year }),
          content: t('yearEnd.closedBody', { number: response.data.voucherNumber, path: response.data.backupPath }),
          okText: t('common.dismiss'),
        })
    },
    onError: explain,
  })

  const reopen = useMutation({
    mutationFn: (year: number) => reopenFiscalYear(year),
    onSuccess: async (_, year) => {
      await refreshBooks(queryClient)
      void message.success(t('yearEnd.reopened', { year }))
    },
    onError: explain,
  })

  const confirmClose = (year: FiscalYearDto) =>
    modal.confirm({
      title: t('yearEnd.confirmCloseTitle', { year: year.year }),
      content: (
        <div>
          <p>{t('yearEnd.confirmCloseBody')}</p>
          <ul>
            <li>{t('yearEnd.stepProfit')}</li>
            <li>{t('yearEnd.stepLock')}</li>
            <li>{t('yearEnd.stepBackup')}</li>
          </ul>
          <p>{t('yearEnd.canReopen')}</p>
        </div>
      ),
      okText: t('yearEnd.close'),
      cancelText: t('common.cancel'),
      onOk: () => close.mutateAsync(year.year).catch(() => undefined),
    })

  const confirmReopen = (year: FiscalYearDto) =>
    modal.confirm({
      title: t('yearEnd.confirmReopenTitle', { year: year.year }),
      content: t('yearEnd.confirmReopenBody'),
      okText: t('yearEnd.reopen'),
      cancelText: t('common.cancel'),
      onOk: () => reopen.mutateAsync(year.year).catch(() => undefined),
    })

  const columns: ColumnsType<FiscalYearDto> = [
    {
      title: t('yearEnd.year'),
      key: 'year',
      render: (_: unknown, y) => (
        <span>
          <strong>{y.year}</strong>{' '}
          <span className="muted">
            ({formatDate(parseIsoDate(y.start), settings.digits, settings.hijri)} – {formatDate(parseIsoDate(y.end), settings.digits, settings.hijri)})
          </span>
        </span>
      ),
    },
    { title: t('yearEnd.profit'), key: 'profit', width: 170, align: 'end', render: (_: unknown, y) => <AmountText value={y.profit} minorUnits={minorUnits} /> },
    {
      title: t('yearEnd.status'),
      key: 'status',
      width: 190,
      render: (_: unknown, y) =>
        y.isClosed ? <Tag color="blue">{t('yearEnd.closedTag')}</Tag> : y.hasEnded ? <Tag color="gold">{t('yearEnd.readyTag')}</Tag> : <Tag>{t('yearEnd.runningTag')}</Tag>,
    },
    {
      title: t('yearEnd.drafts'),
      key: 'drafts',
      width: 140,
      render: (_: unknown, y) => (y.draftVouchers > 0 ? <Tag color="red">{t('yearEnd.draftsCount', { count: y.draftVouchers })}</Tag> : null),
    },
    {
      title: <span className="visually-hidden">{t('voucher.rowActions')}</span>,
      key: 'actions',
      width: 330,
      render: (_: unknown, y) => (
        <Space size={0} wrap>
          {y.isClosed && y.closingVoucherId && (
            <Link to={`/vouchers/closing/${y.closingVoucherId}`}>
              <Button type="link" aria-label={`${t('yearEnd.viewEntry')} ${y.year}`}>
                {t('yearEnd.viewEntry')}
              </Button>
            </Link>
          )}
          {!y.isClosed && y.hasEnded && (
            <Button type="primary" onClick={() => confirmClose(y)} loading={close.isPending && close.variables === y.year} aria-label={`${t('yearEnd.close')} ${y.year}`}>
              {t('yearEnd.close')}
            </Button>
          )}
          {y.isClosed && y.year === latestClosed && (
            <Button onClick={() => confirmReopen(y)} loading={reopen.isPending && reopen.variables === y.year} aria-label={`${t('yearEnd.reopen')} ${y.year}`}>
              {t('yearEnd.reopen')}
            </Button>
          )}
        </Space>
      ),
    },
  ]

  return (
    <div>
      <PageHeader title={t('yearEnd.title')} help="yearEnd" crumbs={[{ label: t('breadcrumb.home'), to: '/' }, { label: t('yearEnd.title') }]} />
      <Alert type="info" showIcon className="form-alert" message={t('yearEnd.intro')} />
      <Card>
        <Table<FiscalYearDto> columns={columns} dataSource={years.data ?? []} rowKey="year" loading={years.isPending} pagination={false} size="middle" bordered />
      </Card>
    </div>
  )
}
