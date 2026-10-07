import { Alert, App, Button, Form, Input, InputNumber, Modal, Segmented, Select, Space, Switch, Table, Tag } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useSearchParams } from 'react-router'
import { createPriceList, createProduct, deletePriceList, deleteProduct, setPriceListActive, setProductActive, updatePriceList, updateProduct } from '../../api/generated/baba'
import type { PriceListDto, PriceListInput, ProductDto, ProductInput } from '../../api/generated/model'
import { ApiError } from '../../api/http'
import { refreshBooks, useAccounts, useCurrencies, useCurrentCompany, useModules, usePriceLists, useProducts, useTaxCodes } from '../../api/hooks'
import { AmountText } from '../../layout/AmountText'
import { EmptyState } from '../../layout/EmptyState'
import { errorMessage } from '../../layout/errors'
import { ExportControls } from '../../layout/ExportControls'
import { ImportModal } from '../../layout/ImportModal'
import { ListPage } from '../../layout/ListPage'
import { exportAndShow } from '../reports/exportReport'
import { useShortcuts } from '../../layout/useShortcuts'
import { useSettings } from '../../settings/SettingsContext'
import { matchesSearch } from '../../utils/arabic'
import { AccountSelect } from '../accounting/AccountSelect'
import { itemName } from '../accounting/LookupSelects'

type Tab = 'products' | 'price-lists'

/** Products and services, and the price lists a customer can be given (brief section 10.3). */
export function ProductsPage() {
  const { t } = useTranslation()
  const [params, setParams] = useSearchParams()
  const tab: Tab = params.get('tab') === 'price-lists' ? 'price-lists' : 'products'
  const [productForm, setProductForm] = useState<{ open: boolean; editing?: ProductDto }>({ open: false })
  const [listForm, setListForm] = useState<{ open: boolean; editing?: PriceListDto }>({ open: false })
  const [importing, setImporting] = useState(false)

  const tabs = (
    <Segmented
      value={tab}
      onChange={(value) => setParams(value === 'products' ? {} : { tab: value as string })}
      options={[
        { value: 'products', label: t('trade.products') },
        { value: 'price-lists', label: t('trade.priceLists') },
      ]}
      aria-label={t('trade.productTabs')}
    />
  )

  useShortcuts({ 'ctrl+n': () => (tab === 'products' ? setProductForm({ open: true }) : setListForm({ open: true })) })

  return (
    <ListPage
      title={t('trade.productsAndPrices')}
      help="products"
      newLabel={tab === 'products' ? t('trade.newProduct') : t('trade.newPriceList')}
      onNew={() => (tab === 'products' ? setProductForm({ open: true }) : setListForm({ open: true }))}
      actions={
        tab === 'products' ? (
          <>
            <ExportControls run={(format, layout) => exportAndShow('products', {}, format, layout)} />
            <Button onClick={() => setImporting(true)}>{t('import.productsButton')}</Button>
          </>
        ) : undefined
      }
      filters={tabs}
    >
      {tab === 'products' ? <ProductsTable onEdit={(p) => setProductForm({ open: true, editing: p })} /> : <PriceListsTable onEdit={(l) => setListForm({ open: true, editing: l })} />}
      <ProductFormModal open={productForm.open} editing={productForm.editing} onClose={() => setProductForm({ open: false })} />
      <ImportModal
        open={importing}
        onClose={() => setImporting(false)}
        title={t('import.productsTitle')}
        intro={t('import.productsIntro')}
        columns="Code, Name, Name (Arabic), Unit, Sale price, Purchase price, Revenue account, Expense account, Tax code, Stock item, Barcode, Reorder level"
        url="/api/import/products"
        templateKey="products"
      />
      <PriceListFormModal open={listForm.open} editing={listForm.editing} onClose={() => setListForm({ open: false })} />
    </ListPage>
  )
}

function ProductsTable({ onEdit }: { onEdit: (product: ProductDto) => void }) {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const { message, modal } = App.useApp()
  const queryClient = useQueryClient()
  const query = useProducts()
  const company = useCurrentCompany()
  const currencies = useCurrencies()
  const minorUnits = currencies.data?.find((c) => c.code === company.data?.baseCurrencyCode)?.minorUnits ?? 2
  const [search, setSearch] = useState('')
  const products = useMemo(() => query.data ?? [], [query.data])
  const rows = useMemo(() => products.filter((p) => matchesSearch(search, p.code, p.nameAr, p.nameEn)), [products, search])

  const toggle = useMutation({
    mutationFn: (p: ProductDto) => setProductActive(p.id, { active: !p.isActive }),
    onSuccess: () => refreshBooks(queryClient),
    onError: (error) => void message.error(errorMessage(error, t)),
  })
  const remove = useMutation({
    mutationFn: (p: ProductDto) => deleteProduct(p.id),
    onSuccess: async () => {
      await refreshBooks(queryClient)
      void message.success(t('trade.productDeleted'))
    },
    onError: (error) => void message.error(errorMessage(error, t)),
  })

  const columns: ColumnsType<ProductDto> = [
    { title: t('parties.code'), dataIndex: 'code', width: 120, render: (code: string) => <span dir="ltr">{code}</span> },
    { title: t('parties.name'), key: 'name', render: (_: unknown, p) => <span className={p.isActive ? '' : 'muted'}>{itemName(p, settings.language)}</span> },
    { title: t('trade.unit'), dataIndex: 'unit', width: 100 },
    { title: t('inventory.stockItem'), key: 'stock', width: 100, render: (_: unknown, p) => (p.isStockItem ? <Tag color="blue">{t('inventory.stockTag')}</Tag> : null) },
    { title: t('trade.salePrice'), key: 'sale', width: 140, align: 'end', render: (_: unknown, p) => <AmountText value={p.salePrice} minorUnits={minorUnits} /> },
    { title: t('trade.purchasePrice'), key: 'purchase', width: 140, align: 'end', render: (_: unknown, p) => <AmountText value={p.purchasePrice} minorUnits={minorUnits} /> },
    { title: t('parties.status'), key: 'status', width: 100, render: (_: unknown, p) => (p.isActive ? t('parties.active') : <Tag>{t('parties.inactive')}</Tag>) },
    {
      title: <span className="visually-hidden">{t('voucher.rowActions')}</span>,
      key: 'actions',
      width: 260,
      render: (_: unknown, p) => (
        <Space size={0} wrap>
          <Button type="link" onClick={() => onEdit(p)} aria-label={`${t('parties.edit')} ${p.code}`}>
            {t('parties.edit')}
          </Button>
          <Button type="link" onClick={() => toggle.mutate(p)} aria-label={`${p.isActive ? t('parties.deactivate') : t('parties.activate')} ${p.code}`}>
            {p.isActive ? t('parties.deactivate') : t('parties.activate')}
          </Button>
          {!p.inUse && (
            <Button
              type="link"
              danger
              aria-label={`${t('parties.delete')} ${p.code}`}
              onClick={() =>
                modal.confirm({
                  title: t('trade.deleteProductTitle', { name: `${p.code} ${itemName(p, settings.language)}` }),
                  okText: t('parties.delete'),
                  okButtonProps: { danger: true },
                  cancelText: t('common.cancel'),
                  onOk: () => remove.mutateAsync(p).catch(() => undefined),
                })
              }
            >
              {t('parties.delete')}
            </Button>
          )}
        </Space>
      ),
    },
  ]

  return (
    <>
      <div className="filter-bar">
        <Input.Search value={search} onChange={(e) => setSearch(e.target.value)} placeholder={t('trade.searchProducts')} aria-label={t('trade.searchProducts')} allowClear className="filter-search" />
      </div>
      {query.isSuccess && products.length === 0 ? (
        <EmptyState title={t('trade.emptyProductsTitle')} body={t('trade.emptyProductsBody')} />
      ) : (
        <Table<ProductDto> columns={columns} dataSource={rows} rowKey="id" loading={query.isPending} pagination={false} size="middle" bordered />
      )}
    </>
  )
}

function PriceListsTable({ onEdit }: { onEdit: (list: PriceListDto) => void }) {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const { message, modal } = App.useApp()
  const queryClient = useQueryClient()
  const query = usePriceLists()
  const lists = query.data ?? []

  const toggle = useMutation({
    mutationFn: (l: PriceListDto) => setPriceListActive(l.id, { active: !l.isActive }),
    onSuccess: () => refreshBooks(queryClient),
    onError: (error) => void message.error(errorMessage(error, t)),
  })
  const remove = useMutation({
    mutationFn: (l: PriceListDto) => deletePriceList(l.id),
    onSuccess: async () => {
      await refreshBooks(queryClient)
      void message.success(t('trade.priceListDeleted'))
    },
    onError: (error) => {
      const inUse = error instanceof ApiError && error.issues.some((i) => i.code === 'price-list.in-use')
      void message.error(inUse ? t('trade.issues.price-list.in-use') : errorMessage(error, t))
    },
  })

  const columns: ColumnsType<PriceListDto> = [
    { title: t('parties.name'), key: 'name', render: (_: unknown, l) => <span className={l.isActive ? '' : 'muted'}>{itemName(l, settings.language)}</span> },
    { title: t('voucher.currency'), dataIndex: 'currencyCode', width: 120 },
    { title: t('trade.pricedProducts'), key: 'count', width: 140, render: (_: unknown, l) => l.lines.length },
    { title: t('parties.status'), key: 'status', width: 100, render: (_: unknown, l) => (l.isActive ? t('parties.active') : <Tag>{t('parties.inactive')}</Tag>) },
    {
      title: <span className="visually-hidden">{t('voucher.rowActions')}</span>,
      key: 'actions',
      width: 260,
      render: (_: unknown, l) => (
        <Space size={0} wrap>
          <Button type="link" onClick={() => onEdit(l)} aria-label={`${t('parties.edit')} ${itemName(l, settings.language)}`}>
            {t('parties.edit')}
          </Button>
          <Button type="link" onClick={() => toggle.mutate(l)} aria-label={`${l.isActive ? t('parties.deactivate') : t('parties.activate')} ${itemName(l, settings.language)}`}>
            {l.isActive ? t('parties.deactivate') : t('parties.activate')}
          </Button>
          <Button
            type="link"
            danger
            aria-label={`${t('parties.delete')} ${itemName(l, settings.language)}`}
            onClick={() =>
              modal.confirm({
                title: t('trade.deletePriceListTitle', { name: itemName(l, settings.language) }),
                okText: t('parties.delete'),
                okButtonProps: { danger: true },
                cancelText: t('common.cancel'),
                onOk: () => remove.mutateAsync(l).catch(() => undefined),
              })
            }
          >
            {t('parties.delete')}
          </Button>
        </Space>
      ),
    },
  ]

  return query.isSuccess && lists.length === 0 ? (
    <EmptyState title={t('trade.emptyPriceListsTitle')} body={t('trade.emptyPriceListsBody')} />
  ) : (
    <Table<PriceListDto> columns={columns} dataSource={lists} rowKey="id" loading={query.isPending} pagination={false} size="middle" bordered />
  )
}

interface ProductValues {
  code: string
  nameEn: string
  nameAr: string
  unit: string
  salePrice: number
  purchasePrice: number
  salesAccountId?: string
  purchaseAccountId?: string
  taxCodeId?: string
  isStockItem?: boolean
  barcode?: string
  reorderLevel?: number
  inventoryAccountId?: string
  costOfSalesAccountId?: string
}

const productFieldOf: Record<string, keyof ProductValues> = {
  code: 'code',
  name: 'nameEn',
  salePrice: 'salePrice',
  purchasePrice: 'purchasePrice',
  salesAccount: 'salesAccountId',
  purchaseAccount: 'purchaseAccountId',
  taxCode: 'taxCodeId',
  isStockItem: 'isStockItem',
  barcode: 'barcode',
  reorderLevel: 'reorderLevel',
  inventoryAccount: 'inventoryAccountId',
  costOfSalesAccount: 'costOfSalesAccountId',
}

function ProductFormModal({ open, editing, onClose }: { open: boolean; editing?: ProductDto; onClose: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [form] = Form.useForm<ProductValues>()
  const accounts = useAccounts().data ?? []
  const products = useProducts().data ?? []
  const taxCodes = useTaxCodes().data ?? []
  const company = useCurrentCompany()
  const currencies = useCurrencies()
  const minorUnits = currencies.data?.find((c) => c.code === company.data?.baseCurrencyCode)?.minorUnits ?? 2
  const inventoryOn = useModules().has('inventory')
  const isStockItem = Form.useWatch('isStockItem', form)

  useEffect(() => {
    if (!open) return
    form.resetFields()
    const next = Math.max(0, ...products.map((p) => Number(/^\D*(\d+)$/.exec(p.code)?.[1] ?? 0))) + 1
    form.setFieldsValue(
      editing
        ? {
            code: editing.code,
            nameEn: editing.nameEn,
            nameAr: editing.nameAr,
            unit: editing.unit ?? '',
            salePrice: editing.salePrice,
            purchasePrice: editing.purchasePrice,
            salesAccountId: editing.salesAccountId ?? undefined,
            purchaseAccountId: editing.purchaseAccountId ?? undefined,
            taxCodeId: editing.taxCodeId ?? undefined,
            isStockItem: editing.isStockItem ?? false,
            barcode: editing.barcode ?? '',
            reorderLevel: editing.reorderLevel ?? 0,
            inventoryAccountId: editing.inventoryAccountId ?? undefined,
            costOfSalesAccountId: editing.costOfSalesAccountId ?? undefined,
          }
        : { code: `P${String(next).padStart(3, '0')}`, nameEn: '', nameAr: '', unit: '', salePrice: 0, purchasePrice: 0, isStockItem: false, barcode: '', reorderLevel: 0 },
    )
    // eslint-disable-next-line react-hooks/exhaustive-deps -- the suggested code is worked out when the window opens
  }, [open, editing, form])

  const save = useMutation({
    mutationFn: (values: ProductValues) => {
      const input: ProductInput = {
        code: values.code,
        nameEn: values.nameEn ?? '',
        nameAr: values.nameAr ?? '',
        unit: values.unit || null,
        salePrice: values.salePrice ?? 0,
        purchasePrice: values.purchasePrice ?? 0,
        salesAccountId: values.salesAccountId ?? null,
        purchaseAccountId: values.purchaseAccountId ?? null,
        taxCodeId: values.taxCodeId ?? null,
        isStockItem: values.isStockItem ?? false,
        barcode: values.barcode?.trim() || null,
        reorderLevel: values.reorderLevel ?? 0,
        inventoryAccountId: values.isStockItem ? (values.inventoryAccountId ?? null) : null,
        costOfSalesAccountId: values.isStockItem ? (values.costOfSalesAccountId ?? null) : null,
      }
      return editing ? updateProduct(editing.id, input) : createProduct(input)
    },
    onSuccess: async () => {
      await refreshBooks(queryClient)
      onClose()
    },
    onError: (error) => {
      if (!(error instanceof ApiError) || error.issues.length === 0) return
      form.setFields(error.issues.map((issue) => ({ name: productFieldOf[issue.field] ?? 'code', errors: [t([`trade.issues.${issue.code}`], { defaultValue: issue.code })] })))
    },
  })

  return (
    <Modal
      open={open}
      title={editing ? t('trade.editProduct') : t('trade.newProduct')}
      onCancel={onClose}
      onOk={() => form.submit()}
      okText={t('common.save')}
      cancelText={t('common.cancel')}
      confirmLoading={save.isPending}
      maskClosable={false}
      destroyOnHidden
      width={620}
    >
      <Form form={form} layout="vertical" onFinish={(values) => save.mutate(values)} requiredMark="optional">
        {save.isError && !(save.error instanceof ApiError && save.error.issues.length > 0) && <Alert type="error" showIcon message={errorMessage(save.error, t)} className="form-alert" />}
        <Form.Item name="code" label={t('parties.code')} rules={[{ required: true, message: t('trade.issues.product.code-required') }]}>
          <Input dir="ltr" autoFocus />
        </Form.Item>
        <Form.Item name="nameEn" label={t('parties.nameEn')} extra={t('parties.nameHelp')}>
          <Input dir="ltr" />
        </Form.Item>
        <Form.Item name="nameAr" label={t('parties.nameAr')}>
          <Input dir="rtl" />
        </Form.Item>
        <div className="form-row">
          <Form.Item name="unit" label={t('trade.unit')} extra={t('trade.unitHelp')}>
            <Input />
          </Form.Item>
          <Form.Item name="salePrice" label={t('trade.salePrice')}>
            <InputNumber min={0} precision={Math.max(minorUnits, 2)} controls={false} className="amount-input" />
          </Form.Item>
          <Form.Item name="purchasePrice" label={t('trade.purchasePrice')}>
            <InputNumber min={0} precision={Math.max(minorUnits, 2)} controls={false} className="amount-input" />
          </Form.Item>
        </div>
        <Form.Item name="salesAccountId" label={t('trade.revenueAccount')} extra={t('trade.productAccountHelp')}>
          <AccountSelect value={undefined} onChange={() => undefined} accounts={accounts.filter((a) => a.type === 'Revenue')} ariaLabel={t('trade.revenueAccount')} />
        </Form.Item>
        {taxCodes.length > 0 && (
          <Form.Item name="taxCodeId" label={t('trade.taxCode')} extra={t('trade.productTaxHelp')}>
            <Select allowClear options={taxCodes.filter((c) => c.isActive || c.id === editing?.taxCodeId).map((c) => ({ value: c.id, label: `${c.code} (${c.rate}%)` }))} />
          </Form.Item>
        )}
        <Form.Item name="purchaseAccountId" label={t('trade.expenseAccount')}>
          <AccountSelect value={undefined} onChange={() => undefined} accounts={accounts.filter((a) => a.type === 'Expense' || a.type === 'Asset')} ariaLabel={t('trade.expenseAccount')} />
        </Form.Item>
        {(inventoryOn || editing?.isStockItem) && (
          <>
            <Form.Item name="isStockItem" label={t('inventory.stockItem')} valuePropName="checked" extra={t('inventory.stockItemHelp')}>
              <Switch aria-label={t('inventory.stockItem')} />
            </Form.Item>
            {isStockItem && (
              <>
                <div className="form-row">
                  <Form.Item name="barcode" label={t('inventory.barcode')}>
                    <Input dir="ltr" />
                  </Form.Item>
                  <Form.Item name="reorderLevel" label={t('inventory.reorderLevel')} extra={t('inventory.reorderLevelHelp')}>
                    <InputNumber min={0} precision={4} controls={false} className="amount-input" />
                  </Form.Item>
                </div>
                <Form.Item name="inventoryAccountId" label={t('inventory.stockAccount')} extra={t('inventory.stockAccountHelp')}>
                  <AccountSelect value={undefined} onChange={() => undefined} accounts={accounts.filter((a) => a.type === 'Asset')} ariaLabel={t('inventory.stockAccount')} />
                </Form.Item>
                <Form.Item name="costOfSalesAccountId" label={t('inventory.costOfSalesAccount')} extra={t('inventory.costOfSalesAccountHelp')}>
                  <AccountSelect value={undefined} onChange={() => undefined} accounts={accounts.filter((a) => a.type === 'Expense')} ariaLabel={t('inventory.costOfSalesAccount')} />
                </Form.Item>
              </>
            )}
          </>
        )}
      </Form>
    </Modal>
  )
}

interface ListValues {
  nameEn: string
  nameAr: string
  currencyCode?: string
  lines: { productId?: string; price: number | null }[]
}

function PriceListFormModal({ open, editing, onClose }: { open: boolean; editing?: PriceListDto; onClose: () => void }) {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const queryClient = useQueryClient()
  const [form] = Form.useForm<ListValues>()
  const products = useProducts().data ?? []
  const currencies = useCurrencies().data ?? []
  const company = useCurrentCompany()
  const base = company.data?.baseCurrencyCode ?? ''
  const [problem, setProblem] = useState<string>()

  useEffect(() => {
    if (!open) return
    form.resetFields()
    form.setFieldsValue(
      editing
        ? { nameEn: editing.nameEn, nameAr: editing.nameAr, currencyCode: editing.currencyCode, lines: editing.lines.map((l) => ({ productId: l.productId, price: l.price })) }
        : { nameEn: '', nameAr: '', currencyCode: base, lines: [{ price: null }] },
    )
  }, [open, editing, form, base])

  // What went wrong last time must not greet the next price list.
  const close = () => {
    setProblem(undefined)
    onClose()
  }

  const save = useMutation({
    mutationFn: (values: ListValues) => {
      const input: PriceListInput = {
        nameEn: values.nameEn ?? '',
        nameAr: values.nameAr ?? '',
        currencyCode: !values.currencyCode || values.currencyCode === base ? null : values.currencyCode,
        lines: (values.lines ?? []).filter((l) => l.productId).map((l) => ({ productId: l.productId!, price: l.price ?? 0 })),
      }
      return editing ? updatePriceList(editing.id, input) : createPriceList(input)
    },
    onSuccess: async () => {
      await refreshBooks(queryClient)
      close()
    },
    onError: (error) => {
      if (error instanceof ApiError && error.issues.length > 0) setProblem(error.issues.map((i) => t([`trade.issues.${i.code}`], { defaultValue: i.code })).join(' '))
      else setProblem(errorMessage(error, t))
    },
  })

  return (
    <Modal
      open={open}
      title={editing ? t('trade.editPriceList') : t('trade.newPriceList')}
      onCancel={close}
      onOk={() => form.submit()}
      okText={t('common.save')}
      cancelText={t('common.cancel')}
      confirmLoading={save.isPending}
      maskClosable={false}
      destroyOnHidden
      width={680}
    >
      <Form form={form} layout="vertical" onFinish={(values) => save.mutate(values)} requiredMark="optional">
        {problem && <Alert type="error" showIcon message={problem} className="form-alert" />}
        <Form.Item name="nameEn" label={t('parties.nameEn')} extra={t('parties.nameHelp')}>
          <Input dir="ltr" autoFocus />
        </Form.Item>
        <Form.Item name="nameAr" label={t('parties.nameAr')}>
          <Input dir="rtl" />
        </Form.Item>
        <Form.Item name="currencyCode" label={t('voucher.currency')} extra={t('trade.priceListCurrencyHelp')}>
          <Select
            showSearch
            optionFilterProp="label"
            options={currencies.map((c) => ({ value: c.code, label: `${c.code} — ${settings.language === 'ar' ? c.nameAr : c.nameEn}` }))}
          />
        </Form.Item>
        <Form.List name="lines">
          {(fields, { add, remove }) => (
            <div className="price-lines">
              <strong>{t('trade.pricedProducts')}</strong>
              {fields.map((field, index) => (
                <div className="form-row" key={field.key}>
                  <Form.Item name={[field.name, 'productId']} label={index === 0 ? t('trade.product') : undefined}>
                    <Select
                      showSearch
                      optionFilterProp="label"
                      allowClear
                      popupMatchSelectWidth={false}
                      aria-label={`${t('trade.product')} ${index + 1}`}
                      options={products.filter((p) => p.isActive).map((p) => ({ value: p.id, label: `${p.code} — ${itemName(p, settings.language)}` }))}
                    />
                  </Form.Item>
                  <Form.Item name={[field.name, 'price']} label={index === 0 ? t('trade.unitPrice') : undefined}>
                    <InputNumber min={0} precision={4} controls={false} className="amount-input" aria-label={`${t('trade.unitPrice')} ${index + 1}`} />
                  </Form.Item>
                  <Button type="link" danger onClick={() => remove(field.name)} aria-label={`${t('voucher.removeRow')} ${index + 1}`}>
                    {t('voucher.removeRow')}
                  </Button>
                </div>
              ))}
              <Button onClick={() => add({ price: null })}>{t('trade.addPriceLine')}</Button>
            </div>
          )}
        </Form.List>
      </Form>
    </Modal>
  )
}
