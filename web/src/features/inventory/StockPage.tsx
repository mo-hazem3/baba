import { App, Button, Segmented, Space, Table, Tag } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useSearchParams } from 'react-router'
import { deleteStockDocument } from '../../api/generated/baba'
import type { StockDocumentDto, StockDocumentKind, StockLevelDto } from '../../api/generated/model'
import { ApiError } from '../../api/http'
import { refreshBooks, useCurrencies, useCurrentCompany, useProducts, useStockDocuments, useStockLevels, useWarehouses } from '../../api/hooks'
import { AmountText } from '../../layout/AmountText'
import { EmptyState } from '../../layout/EmptyState'
import { errorMessage } from '../../layout/errors'
import { ExportControls } from '../../layout/ExportControls'
import { ImportModal } from '../../layout/ImportModal'
import { ListPage } from '../../layout/ListPage'
import { QuantityText } from '../../layout/QuantityText'
import { useSettings } from '../../settings/SettingsContext'
import { formatDate, toIsoDate } from '../../utils/format'
import { itemName } from '../accounting/LookupSelects'
import { exportAndShow } from '../reports/exportReport'
import { StockDocumentModal } from './StockDocumentModal'

type Tab = 'on-hand' | 'documents'

const kinds: StockDocumentKind[] = ['Opening', 'Adjustment', 'Transfer']

/** What is in stock and what it is worth, and the documents that change it: opening stock, adjustments and counts, transfers (brief section 10.4). */
export function StockPage() {
  const { t } = useTranslation()
  const [params, setParams] = useSearchParams()
  useWarehouses() // read early, so the default warehouse exists when a document form opens
  const tab: Tab = params.get('tab') === 'documents' ? 'documents' : 'on-hand'
  const [modal, setModal] = useState<{ open: boolean; kind: StockDocumentKind; editing?: StockDocumentDto }>({ open: false, kind: 'Opening' })
  const [importing, setImporting] = useState(false)

  const tabs = (
    <Segmented
      value={tab}
      onChange={(value) => setParams(value === 'on-hand' ? {} : { tab: value as string })}
      options={[
        { value: 'on-hand', label: t('inventory.onHandTab') },
        { value: 'documents', label: t('inventory.documentsTab') },
      ]}
      aria-label={t('inventory.stockTabs')}
    />
  )

  return (
    <ListPage
      title={t('inventory.stock')}
      help="stock"
      actions={
        <Space wrap>
          <ExportControls
            run={(format, layout) =>
              tab === 'on-hand' ? exportAndShow('stock-valuation', { AsOf: toIsoDate(new Date()) }, format, layout) : exportAndShow('stock-documents', {}, format, layout)
            }
          />
          <Button onClick={() => setImporting(true)}>{t('inventory.importOpening')}</Button>
          {kinds.map((kind) => (
            <Button key={kind} type={kind === 'Opening' ? 'default' : 'primary'} onClick={() => setModal({ open: true, kind })}>
              + {t(`inventory.new.${kind}`)}
            </Button>
          ))}
        </Space>
      }
      filters={tabs}
    >
      {tab === 'on-hand' ? <OnHandTable /> : <DocumentsTable onEdit={(d) => setModal({ open: true, kind: d.kind, editing: d })} />}
      <StockDocumentModal open={modal.open} kind={modal.kind} editing={modal.editing} onClose={() => setModal((m) => ({ ...m, open: false }))} />
      <ImportModal
        open={importing}
        onClose={() => setImporting(false)}
        title={t('inventory.importTitle')}
        intro={t('inventory.importIntro')}
        columns="Product code, Warehouse code, Quantity, Unit cost"
        url="/api/import/opening-stock"
        templateKey="opening-stock"
      />
    </ListPage>
  )
}

function OnHandTable() {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const levels = useStockLevels()
  const productList = useProducts()
  const products = useMemo(() => productList.data ?? [], [productList.data])
  const warehouses = useWarehouses().data ?? []
  const company = useCurrentCompany()
  const currencies = useCurrencies()
  const minorUnits = currencies.data?.find((c) => c.code === company.data?.baseCurrencyCode)?.minorUnits ?? 2

  const rows = useMemo(() => {
    const byId = new Map(products.map((p) => [p.id, p]))
    return [...(levels.data ?? [])]
      .filter((l) => byId.has(l.productId))
      .sort((a, b) => (byId.get(a.productId)!.code).localeCompare(byId.get(b.productId)!.code, 'en', { numeric: true }))
  }, [levels.data, products])
  const total = rows.reduce((sum, l) => sum + l.value, 0)
  const product = (id: string) => products.find((p) => p.id === id)
  const warehouse = (id: string) => warehouses.find((w) => w.id === id)

  const columns: ColumnsType<StockLevelDto> = [
    { title: t('parties.code'), key: 'code', width: 120, render: (_: unknown, l) => <span dir="ltr">{product(l.productId)?.code}</span> },
    { title: t('trade.product'), key: 'name', render: (_: unknown, l) => (product(l.productId) ? itemName(product(l.productId)!, settings.language) : '') },
    { title: t('inventory.warehouse'), key: 'warehouse', render: (_: unknown, l) => (warehouse(l.warehouseId) ? itemName(warehouse(l.warehouseId)!, settings.language) : '') },
    { title: t('inventory.onHand'), key: 'quantity', width: 130, align: 'end', render: (_: unknown, l) => <QuantityText value={l.quantity} /> },
    {
      title: t('inventory.averageCost'),
      key: 'cost',
      width: 140,
      align: 'end',
      render: (_: unknown, l) => (l.quantity === 0 ? null : <AmountText value={l.value / l.quantity} minorUnits={Math.max(minorUnits, 2)} />),
    },
    { title: t('inventory.value'), key: 'value', width: 150, align: 'end', render: (_: unknown, l) => <AmountText value={l.value} minorUnits={minorUnits} /> },
  ]

  if (levels.isSuccess && rows.length === 0) return <EmptyState title={t('inventory.emptyStockTitle')} body={t('inventory.emptyStockBody')} />
  return (
    <>
      <Table<StockLevelDto>
        columns={columns}
        dataSource={rows}
        rowKey={(l) => `${l.productId}-${l.warehouseId}`}
        loading={levels.isPending}
        pagination={false}
        size="middle"
        bordered
        summary={() => (
          <Table.Summary.Row>
            <Table.Summary.Cell index={0} colSpan={5}>
              <strong>{t('inventory.totalValue')}</strong>
            </Table.Summary.Cell>
            <Table.Summary.Cell index={5} align="end">
              <strong>
                <AmountText value={total} minorUnits={minorUnits} />
              </strong>
            </Table.Summary.Cell>
          </Table.Summary.Row>
        )}
      />
      <p className="muted">
        <Link to="/reports/stock-valuation">{t('inventory.openValuationReport')}</Link>
      </p>
    </>
  )
}

function DocumentsTable({ onEdit }: { onEdit: (document: StockDocumentDto) => void }) {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const { message, modal } = App.useApp()
  const queryClient = useQueryClient()
  const query = useStockDocuments()
  const company = useCurrentCompany()
  const currencies = useCurrencies()
  const minorUnits = currencies.data?.find((c) => c.code === company.data?.baseCurrencyCode)?.minorUnits ?? 2

  const remove = useMutation({
    mutationFn: (d: StockDocumentDto) => deleteStockDocument(d.id),
    onSuccess: async () => {
      await refreshBooks(queryClient)
      void message.success(t('inventory.documentDeleted'))
    },
    onError: (error) => {
      const known = error instanceof ApiError ? error.issues[0] : undefined
      void message.error(known ? t([`inventory.issues.${known.code}`, `trade.issues.${known.code}`, `voucher.issues.${known.code}`], { defaultValue: errorMessage(error, t) }) : errorMessage(error, t))
    },
  })

  const columns: ColumnsType<StockDocumentDto> = [
    { title: t('trade.number'), dataIndex: 'number', width: 150, render: (n: string) => <span dir="ltr">{n}</span> },
    { title: t('voucher.date'), key: 'date', width: 130, render: (_: unknown, d) => formatDate(d.date, settings.digits, settings.hijri) },
    { title: t('inventory.type'), key: 'kind', width: 160, render: (_: unknown, d) => <Tag>{t(`inventory.kinds.${d.kind}`)}</Tag> },
    { title: t('inventory.notes'), dataIndex: 'memo' },
    { title: t('inventory.lines'), key: 'lines', width: 90, align: 'end', render: (_: unknown, d) => d.lines.length },
    { title: t('inventory.value'), key: 'value', width: 150, align: 'end', render: (_: unknown, d) => (d.kind === 'Transfer' ? null : <AmountText value={d.value} minorUnits={minorUnits} />) },
    {
      title: <span className="visually-hidden">{t('voucher.rowActions')}</span>,
      key: 'actions',
      width: 200,
      render: (_: unknown, d) => (
        <Space size={0} wrap>
          <Button type="link" onClick={() => onEdit(d)} aria-label={`${t('parties.edit')} ${d.number}`}>
            {t('parties.edit')}
          </Button>
          <Button
            type="link"
            danger
            aria-label={`${t('parties.delete')} ${d.number}`}
            onClick={() =>
              modal.confirm({
                title: t('inventory.deleteDocumentTitle', { number: d.number }),
                content: t('inventory.deleteDocumentBody'),
                okText: t('parties.delete'),
                okButtonProps: { danger: true },
                cancelText: t('common.cancel'),
                onOk: () => remove.mutateAsync(d).catch(() => undefined),
              })
            }
          >
            {t('parties.delete')}
          </Button>
        </Space>
      ),
    },
  ]

  const documents = query.data ?? []
  if (query.isSuccess && documents.length === 0) return <EmptyState title={t('inventory.emptyDocumentsTitle')} body={t('inventory.emptyDocumentsBody')} />
  return <Table<StockDocumentDto> columns={columns} dataSource={[...documents]} rowKey="id" loading={query.isPending} pagination={false} size="middle" bordered />
}
