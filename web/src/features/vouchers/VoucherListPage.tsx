import { Input, Segmented, Select, Table, Tag } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useQuery } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate } from 'react-router'
import { listVouchers } from '../../api/generated/baba'
import type { VoucherKind, VoucherStatus, VoucherSummary } from '../../api/generated/model'
import { useCurrentCompany, useCurrencies } from '../../api/hooks'
import { AmountText } from '../../layout/AmountText'
import { EmptyState } from '../../layout/EmptyState'
import { ExportControls } from '../../layout/ExportControls'
import { ListPage } from '../../layout/ListPage'
import { useShortcuts } from '../../layout/useShortcuts'
import { useSettings } from '../../settings/SettingsContext'
import { matchesSearch } from '../../utils/arabic'
import { formatDate, parseIsoDate } from '../../utils/format'
import { exportAndShow } from '../reports/exportReport'
import { presetRange, type RangePreset } from '../reports/reportModel'
import { routeOfKind } from '../accounting/voucherModel'

const presets: RangePreset[] = ['thisMonth', 'lastMonth', 'thisYear', 'lastYear', 'all']

/** The list of one kind of voucher (brief section 7.3): filters on top, one obvious "+ New", and a click on a row opens it. */
export function VoucherListPage({ kind }: { kind: VoucherKind }) {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const navigate = useNavigate()
  const company = useCurrentCompany()
  const currencies = useCurrencies()

  const [preset, setPreset] = useState<RangePreset>('thisYear')
  const [status, setStatus] = useState<VoucherStatus | 'All'>('All')
  const [search, setSearch] = useState('')

  const range = useMemo(() => presetRange(preset, new Date(), company.data?.fiscalYearStartMonth ?? 1), [preset, company.data?.fiscalYearStartMonth])
  const statusFilter = status === 'All' ? undefined : status
  const minorUnits = currencies.data?.find((c) => c.code === company.data?.baseCurrencyCode)?.minorUnits ?? 2

  const query = useQuery({
    queryKey: ['/api/vouchers', kind, status, range.from, range.to],
    queryFn: async () => (await listVouchers({ kind, status: statusFilter, from: range.from, to: range.to })).data,
  })

  const vouchers = useMemo(
    () => (query.data ?? []).filter((v) => matchesSearch(search, v.number ?? '', v.reference ?? '', v.memo ?? '')),
    [query.data, search],
  )
  const total = vouchers.reduce((sum, v) => sum + v.total, 0)
  const open = (id: string) => navigate(`/vouchers/${routeOfKind(kind)}/${id}`)
  const create = () => navigate(`/vouchers/${routeOfKind(kind)}/new`)

  useShortcuts({ 'ctrl+n': create })

  const columns: ColumnsType<VoucherSummary> = [
    {
      title: t('voucher.number'),
      key: 'number',
      width: 160,
      render: (_: unknown, v) => (
        <Link to={`/vouchers/${routeOfKind(kind)}/${v.id}`} dir="ltr">
          {v.number ?? t('voucher.draft')}
        </Link>
      ),
    },
    { title: t('voucher.date'), key: 'date', width: 190, render: (_: unknown, v) => formatDate(parseIsoDate(v.date), settings.digits, settings.hijri) },
    { title: t('voucher.reference'), dataIndex: 'reference', width: 150 },
    { title: t('voucher.notes'), dataIndex: 'memo' },
    {
      title: t('voucher.amount'),
      key: 'total',
      align: 'end',
      width: 150,
      render: (_: unknown, v) => <AmountText value={v.total} minorUnits={minorUnits} />,
    },
    {
      title: t('voucher.status'),
      key: 'status',
      width: 110,
      render: (_: unknown, v) => (v.status === 'Posted' ? <Tag color="blue">{t('voucher.posted')}</Tag> : <Tag>{t('voucher.draft')}</Tag>),
    },
  ]

  return (
    <ListPage
      title={t(`voucher.plural.${kind}`)}
      help="vouchers"
      newLabel={t(`voucher.new.${kind}`)}
      onNew={create}
      actions={
        <ExportControls
          run={(format, layout) => exportAndShow('vouchers', { From: range.from, To: range.to, Kind: kind, Status: statusFilter }, format, layout)}
        />
      }
      filters={
        <>
          <Input.Search
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder={t('voucher.search')}
            aria-label={t('voucher.search')}
            allowClear
            className="filter-search"
          />
          <Segmented
            value={preset}
            onChange={(value) => setPreset(value as RangePreset)}
            options={presets.map((p) => ({ value: p, label: t(`reports.presets.${p}`) }))}
            aria-label={t('reports.dates')}
          />
          <Select
            value={status}
            onChange={setStatus}
            aria-label={t('voucher.status')}
            options={[
              { value: 'All', label: `${t('voucher.status')}: ${t('voucher.allStatuses')}` },
              { value: 'Draft', label: `${t('voucher.status')}: ${t('voucher.draft')}` },
              { value: 'Posted', label: `${t('voucher.status')}: ${t('voucher.posted')}` },
            ]}
          />
        </>
      }
    >
      {query.isSuccess && vouchers.length === 0 ? (
        <EmptyState
          title={t(`voucher.emptyTitle.${kind}`)}
          body={search ? t('voucher.emptySearch') : t(`voucher.emptyBody.${kind}`)}
        />
      ) : (
        <Table<VoucherSummary>
          columns={columns}
          dataSource={vouchers}
          rowKey="id"
          loading={query.isPending}
          pagination={false}
          size="middle"
          bordered
          onRow={(v) => ({ onClick: () => open(v.id), style: { cursor: 'pointer' } })}
          summary={() =>
            vouchers.length > 0 ? (
              <Table.Summary.Row className="report-total">
                <Table.Summary.Cell index={0} colSpan={4}>
                  {t('voucher.totalOf', { count: vouchers.length })}
                </Table.Summary.Cell>
                <Table.Summary.Cell index={1} align="end">
                  <AmountText value={total} minorUnits={minorUnits} />
                </Table.Summary.Cell>
                <Table.Summary.Cell index={2} />
              </Table.Summary.Row>
            ) : null
          }
        />
      )}
    </ListPage>
  )
}
