import { Input, Segmented, Select, Table, Tag } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useQuery } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { listDocuments, listOutstandingInvoices } from '../../api/generated/baba'
import type { DocumentKind, DocumentStatus, DocumentSummary } from '../../api/generated/model'
import { useCurrencies, useCurrentCompany, useParties } from '../../api/hooks'
import { AmountText } from '../../layout/AmountText'
import { EmptyState } from '../../layout/EmptyState'
import { Button } from 'antd'
import { ListPage } from '../../layout/ListPage'
import { useShortcuts } from '../../layout/useShortcuts'
import { useSettings } from '../../settings/SettingsContext'
import { matchesSearch } from '../../utils/arabic'
import { formatDate, parseIsoDate } from '../../utils/format'
import { itemName } from '../accounting/LookupSelects'
import { presetRange, type RangePreset } from '../reports/reportModel'
import { documentPath, kindFromSegment, kindsOf, segmentOfKind, type Side } from './documentModel'

const presets: RangePreset[] = ['thisMonth', 'lastMonth', 'thisYear', 'lastYear', 'all']

/**
 * Sales or purchases (brief section 10.3): one tab for each kind of document, the same filters as a voucher list, and one
 * obvious "+ New". A click on a row opens the document.
 */
export function DocumentListPage({ side }: { side: Side }) {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const navigate = useNavigate()
  const [params, setParams] = useSearchParams()
  const company = useCurrentCompany()
  const currencies = useCurrencies()
  const partiesQuery = useParties()

  const invoiceKind: DocumentKind = side === 'sales' ? 'SalesInvoice' : 'PurchaseInvoice'
  const requested = kindFromSegment(params.get('kind') ?? undefined)
  const kind = requested && kindsOf[side].includes(requested) ? requested : invoiceKind

  const [preset, setPreset] = useState<RangePreset>('thisYear')
  const [status, setStatus] = useState<DocumentStatus | 'All'>('All')
  const [search, setSearch] = useState('')

  const range = useMemo(() => presetRange(preset, new Date(), company.data?.fiscalYearStartMonth ?? 1), [preset, company.data?.fiscalYearStartMonth])
  const baseCode = company.data?.baseCurrencyCode ?? ''
  const parties = useMemo(() => new Map((partiesQuery.data ?? []).map((p) => [p.id, p])), [partiesQuery.data])

  const query = useQuery({
    queryKey: ['/api/documents', kind, status, range.from, range.to],
    queryFn: async () =>
      (await listDocuments({ kind, status: status === 'All' ? undefined : status, from: range.from, to: range.to })).data,
  })

  const invoices = kind === 'SalesInvoice' || kind === 'PurchaseInvoice'
  const outstandingQuery = useQuery({
    queryKey: ['/api/settlements/outstanding', 'all'],
    enabled: invoices,
    queryFn: async () => (await listOutstandingInvoices()).data,
  })
  const outstandingOf = useMemo(() => new Map((outstandingQuery.data ?? []).map((o) => [o.documentId, o.outstanding])), [outstandingQuery.data])

  const partyName = (id: string) => {
    const party = parties.get(id)
    return party ? itemName(party, settings.language) : ''
  }

  const documents = useMemo(
    () => (query.data ?? []).filter((d) => matchesSearch(search, d.number ?? '', d.reference ?? '', partyName(d.partyId))),
    // eslint-disable-next-line react-hooks/exhaustive-deps -- partyName reads `parties` and the language
    [query.data, search, parties, settings.language],
  )

  const totalsByCurrency = Object.entries(
    documents.reduce<Record<string, number>>((all, d) => ({ ...all, [d.currencyCode || baseCode]: (all[d.currencyCode || baseCode] ?? 0) + d.total }), {}),
  )

  const create = () => navigate(`/documents/${segmentOfKind(kind)}/new`)
  useShortcuts({ 'ctrl+n': create })

  const amount = (value: number, code?: string) => (
    <span>
      <AmountText value={value} minorUnits={currencies.data?.find((c) => c.code === (code || baseCode))?.minorUnits ?? 2} />
      {code && code !== baseCode && <span className="muted"> {code}</span>}
    </span>
  )

  const statusTag = (d: DocumentSummary) =>
    d.status === 'Draft' ? (
      <Tag>{t('trade.status.Draft')}</Tag>
    ) : d.status === 'Converted' ? (
      <Tag color="gold">{t('trade.status.Converted')}</Tag>
    ) : invoices && outstandingOf.get(d.id) === 0 ? (
      <Tag color="green">{t('trade.paid')}</Tag>
    ) : (
      <Tag color="blue">{t('trade.status.Issued')}</Tag>
    )

  const columns: ColumnsType<DocumentSummary> = [
    {
      title: t('trade.number'),
      key: 'number',
      width: 160,
      render: (_: unknown, d) => (
        <Link to={documentPath(d.kind, d.id)} dir="ltr">
          {d.number ?? t('trade.status.Draft')}
        </Link>
      ),
    },
    { title: t('voucher.date'), key: 'date', width: 190, render: (_: unknown, d) => formatDate(parseIsoDate(d.date), settings.digits, settings.hijri) },
    { title: side === 'sales' ? t('trade.customer') : t('trade.supplier'), key: 'party', render: (_: unknown, d) => partyName(d.partyId) },
    { title: t('trade.dueDate'), key: 'due', width: 190, render: (_: unknown, d) => (d.dueDate ? formatDate(parseIsoDate(d.dueDate), settings.digits, settings.hijri) : '') },
    { title: t('voucher.reference'), dataIndex: 'reference', width: 140 },
    { title: t('voucher.amount'), key: 'total', align: 'end', width: 160, render: (_: unknown, d) => amount(d.total, d.currencyCode) },
    ...(invoices
      ? [
          {
            title: t('trade.outstanding'),
            key: 'outstanding',
            align: 'end' as const,
            width: 160,
            render: (_: unknown, d: DocumentSummary) => (d.status === 'Issued' && outstandingOf.has(d.id) ? amount(outstandingOf.get(d.id)!, d.currencyCode) : null),
          },
        ]
      : []),
    { title: t('voucher.status'), key: 'status', width: 120, render: (_: unknown, d) => statusTag(d) },
  ]

  return (
    <ListPage
      title={t(`trade.side.${side}`)}
      help="documents"
      newLabel={t(`trade.new.${kind}`)}
      onNew={create}
      actions={
        <Button onClick={() => navigate(side === 'sales' ? '/settlements/receive' : '/settlements/pay')}>
          {t(side === 'sales' ? 'trade.receivePayment' : 'trade.payInvoices')}
        </Button>
      }
      filters={
        <>
          <Segmented
            value={kind}
            onChange={(value) => setParams({ kind: segmentOfKind(value as DocumentKind) })}
            options={kindsOf[side].map((k) => ({ value: k, label: t(`trade.plural.${k}`) }))}
            aria-label={t('trade.kindTabs')}
          />
          <Input.Search
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder={t('trade.search')}
            aria-label={t('trade.search')}
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
              { value: 'Draft', label: `${t('voucher.status')}: ${t('trade.status.Draft')}` },
              { value: 'Issued', label: `${t('voucher.status')}: ${t('trade.status.Issued')}` },
              { value: 'Converted', label: `${t('voucher.status')}: ${t('trade.status.Converted')}` },
            ]}
          />
        </>
      }
    >
      {query.isSuccess && documents.length === 0 ? (
        <EmptyState title={t(`trade.emptyTitle.${kind}`)} body={search ? t('voucher.emptySearch') : t(`trade.emptyBody.${kind}`)} />
      ) : (
        <Table<DocumentSummary>
          columns={columns}
          dataSource={documents}
          rowKey="id"
          loading={query.isPending}
          pagination={false}
          size="middle"
          bordered
          onRow={(d) => ({ onClick: () => navigate(documentPath(d.kind, d.id)), style: { cursor: 'pointer' } })}
          summary={() =>
            documents.length > 0 ? (
              <Table.Summary.Row className="report-total">
                <Table.Summary.Cell index={0} colSpan={5}>
                  {t('voucher.totalOf', { count: documents.length })}
                </Table.Summary.Cell>
                <Table.Summary.Cell index={1} align="end">
                  {totalsByCurrency.map(([code, sum]) => (
                    <div key={code}>{amount(sum, code)}</div>
                  ))}
                </Table.Summary.Cell>
                <Table.Summary.Cell index={2} colSpan={invoices ? 2 : 1} />
              </Table.Summary.Row>
            ) : null
          }
        />
      )}
    </ListPage>
  )
}
