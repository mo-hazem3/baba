import { Alert, Button, Form, Input, InputNumber, Modal, Segmented, Select } from 'antd'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { saveStockDocument } from '../../api/generated/baba'
import type { StockDocumentDto, StockDocumentInput, StockDocumentKind, StockLineInput } from '../../api/generated/model'
import { ApiError } from '../../api/http'
import { refreshBooks, useAccounts, useCurrentCompany, useCurrencies, useProducts, useStockLevels, useWarehouses } from '../../api/hooks'
import { errorMessage } from '../../layout/errors'
import { QuantityText } from '../../layout/QuantityText'
import { DateField } from '../../layout/DateField'
import { useSettings } from '../../settings/SettingsContext'
import { toIsoDate } from '../../utils/format'
import { AccountSelect } from '../accounting/AccountSelect'
import { itemLabel } from '../accounting/LookupSelects'

interface LineValues {
  productId?: string
  warehouseId?: string
  toWarehouseId?: string
  quantity?: number | null
  counted?: number | null
  unitCost?: number | null
}

interface Values {
  date: string
  memo?: string
  counterAccountId?: string
  lines: LineValues[]
}

type Mode = 'count' | 'change'

const round4 = (value: number) => Math.round(value * 10_000) / 10_000

/**
 * One stock document (brief section 10.4): opening stock, an adjustment (a stock count, or a plain change in quantity), or a
 * transfer between warehouses. Opening stock and adjustments are posted to the books; a transfer only moves stock.
 */
export function StockDocumentModal({ open, kind, editing, onClose }: { open: boolean; kind: StockDocumentKind; editing?: StockDocumentDto; onClose: () => void }) {
  // The dialog only exists while it is open, so every opening starts with a fresh form and no leftover problems.
  return open ? <StockDocumentDialog kind={kind} editing={editing} onClose={onClose} /> : null
}

function StockDocumentDialog({ kind, editing, onClose }: { kind: StockDocumentKind; editing?: StockDocumentDto; onClose: () => void }) {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const queryClient = useQueryClient()
  const [form] = Form.useForm<Values>()
  const accounts = useAccounts().data ?? []
  const products = (useProducts().data ?? []).filter((p) => p.isStockItem)
  const warehouses = (useWarehouses().data ?? []).filter((w) => w.isActive)
  const levels = useStockLevels().data ?? []
  const company = useCurrentCompany()
  const currencies = useCurrencies()
  const minorUnits = currencies.data?.find((c) => c.code === company.data?.baseCurrencyCode)?.minorUnits ?? 2
  const [mode, setMode] = useState<Mode>(editing ? 'change' : 'count')
  const [problems, setProblems] = useState<string[]>([])
  const counting = kind === 'Adjustment' && mode === 'count'

  const onHand = (productId?: string, warehouseId?: string) =>
    levels.find((l) => l.productId === productId && l.warehouseId === warehouseId)?.quantity ?? 0

  useEffect(() => {
    const first = warehouses.find((w) => w.isDefault)?.id ?? warehouses[0]?.id
    if (editing) {
      form.setFieldsValue({
        date: editing.date,
        memo: editing.memo ?? '',
        counterAccountId: editing.counterAccountId ?? undefined,
        lines: editing.lines.map((l) => ({ productId: l.productId, warehouseId: l.warehouseId, toWarehouseId: l.toWarehouseId ?? undefined, quantity: l.quantity, unitCost: l.unitCost })),
      })
    } else {
      form.setFieldsValue({ date: toIsoDate(new Date()), memo: '', lines: [{ warehouseId: first, quantity: null, counted: null, unitCost: null }] })
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- the form is filled when the window opens, not when the lists change under it
  }, [editing, form, kind])

  /** A stock count starts from everything the chosen warehouse holds, so only the differences have to be typed. */
  const loadFromWarehouse = () => {
    const warehouseId = form.getFieldValue(['lines', 0, 'warehouseId']) ?? warehouses[0]?.id
    const rows = levels
      .filter((l) => l.warehouseId === warehouseId && l.quantity !== 0)
      .map((l) => ({ productId: l.productId, warehouseId, counted: l.quantity, quantity: null, unitCost: null }))
    form.setFieldValue('lines', rows.length > 0 ? rows : [{ warehouseId, counted: null, quantity: null, unitCost: null }])
  }

  const save = useMutation({
    mutationFn: (input: StockDocumentInput) => saveStockDocument({ id: editing?.id ?? null, input }),
    onSuccess: async () => {
      await refreshBooks(queryClient)
      onClose()
    },
    onError: (error) => {
      if (!(error instanceof ApiError) || error.issues.length === 0) {
        setProblems([errorMessage(error, t)])
        return
      }
      setProblems(
        error.issues.map((issue) => {
          const row = /^lines\[(\d+)\]/.exec(issue.field)
          const text = t([`inventory.issues.${issue.code}`, `trade.issues.${issue.code}`, `voucher.issues.${issue.code}`], { defaultValue: issue.code })
          return row ? `${t('inventory.line')} ${Number(row[1]) + 1}: ${text}` : text
        }),
      )
    },
  })

  const submit = (values: Values) => {
    const rows: StockLineInput[] = []
    for (const line of values.lines ?? []) {
      if (!line.productId || !line.warehouseId) continue
      let quantity = line.quantity ?? 0
      if (counting) {
        if (line.counted === null || line.counted === undefined) continue
        quantity = round4(line.counted - onHand(line.productId, line.warehouseId))
        if (quantity === 0) continue // counted exactly what is recorded: nothing to change
      }
      rows.push({
        productId: line.productId,
        warehouseId: line.warehouseId,
        toWarehouseId: kind === 'Transfer' ? (line.toWarehouseId ?? null) : null,
        quantity,
        unitCost: kind === 'Transfer' ? null : (line.unitCost ?? null),
      })
    }
    if (rows.length === 0) {
      setProblems([t(counting ? 'inventory.nothingCounted' : 'voucher.issues.lines.required')])
      return
    }
    setProblems([])
    save.mutate({ kind, date: values.date, memo: values.memo?.trim() || null, counterAccountId: kind === 'Transfer' ? null : (values.counterAccountId ?? null), lines: rows })
  }

  const productOptions = useMemo(() => products.filter((p) => p.isActive).map((p) => ({ value: p.id, label: itemLabel(p, settings.language) })), [products, settings.language])
  const warehouseOptions = warehouses.map((w) => ({ value: w.id, label: itemLabel(w, settings.language) }))
  const title = editing ? t(`inventory.edit.${kind}`) : t(`inventory.new.${kind}`)

  return (
    <Modal
      open
      title={title}
      onCancel={onClose}
      onOk={() => form.submit()}
      okText={t('common.save')}
      cancelText={t('common.cancel')}
      confirmLoading={save.isPending}
      maskClosable={false}
      destroyOnHidden
      width={900}
    >
      <Form form={form} layout="vertical" onFinish={submit} requiredMark={false}>
        {problems.length > 0 && (
          <Alert type="error" showIcon className="form-alert" message={problems.length === 1 ? problems[0] : <ul className="problem-list">{problems.map((p) => <li key={p}>{p}</li>)}</ul>} />
        )}
        <p className="muted">{t(`inventory.intro.${kind}`)}</p>
        <div className="form-row">
          <Form.Item name="date" label={t('voucher.date')} rules={[{ required: true }]}>
            <DateField value={undefined} onChange={() => undefined} ariaLabel={t('voucher.date')} />
          </Form.Item>
          <Form.Item name="memo" label={t('inventory.notes')}>
            <Input />
          </Form.Item>
        </div>
        {kind !== 'Transfer' && (
          <Form.Item name="counterAccountId" label={t('inventory.counterAccount')} extra={t('inventory.counterAccountHelp')}>
            <AccountSelect value={undefined} onChange={() => undefined} accounts={accounts} ariaLabel={t('inventory.counterAccount')} />
          </Form.Item>
        )}
        {kind === 'Adjustment' && !editing && (
          <div className="form-row">
            <Segmented
              value={mode}
              onChange={(value) => setMode(value as Mode)}
              options={[
                { value: 'count', label: t('inventory.modeCount') },
                { value: 'change', label: t('inventory.modeChange') },
              ]}
              aria-label={t('inventory.mode')}
            />
            {counting && <Button onClick={loadFromWarehouse}>{t('inventory.loadStock')}</Button>}
          </div>
        )}
        <Form.List name="lines">
          {(fields, { add, remove }) => (
            <div className="price-lines">
              {fields.map((field, index) => (
                <div className="form-row" key={field.key}>
                  <Form.Item name={[field.name, 'productId']} label={index === 0 ? t('trade.product') : undefined} className="grow">
                    <Select showSearch optionFilterProp="label" popupMatchSelectWidth={false} options={productOptions} aria-label={`${t('trade.product')} ${index + 1}`} />
                  </Form.Item>
                  <Form.Item name={[field.name, 'warehouseId']} label={index === 0 ? t(kind === 'Transfer' ? 'inventory.fromWarehouse' : 'inventory.warehouse') : undefined}>
                    <Select options={warehouseOptions} popupMatchSelectWidth={false} aria-label={`${t('inventory.warehouse')} ${index + 1}`} />
                  </Form.Item>
                  {kind === 'Transfer' && (
                    <Form.Item name={[field.name, 'toWarehouseId']} label={index === 0 ? t('inventory.toWarehouse') : undefined}>
                      <Select options={warehouseOptions} popupMatchSelectWidth={false} aria-label={`${t('inventory.toWarehouse')} ${index + 1}`} />
                    </Form.Item>
                  )}
                  {counting ? (
                    <>
                      <Form.Item label={index === 0 ? t('inventory.onHand') : undefined} shouldUpdate>
                        {() => (
                          <span className="static-field">
                            <QuantityText value={onHand(form.getFieldValue(['lines', field.name, 'productId']), form.getFieldValue(['lines', field.name, 'warehouseId']))} />
                          </span>
                        )}
                      </Form.Item>
                      <Form.Item name={[field.name, 'counted']} label={index === 0 ? t('inventory.counted') : undefined}>
                        <InputNumber precision={4} controls={false} className="amount-input" aria-label={`${t('inventory.counted')} ${index + 1}`} />
                      </Form.Item>
                    </>
                  ) : (
                    <Form.Item name={[field.name, 'quantity']} label={index === 0 ? t(kind === 'Adjustment' ? 'inventory.change' : 'inventory.quantity') : undefined}>
                      <InputNumber precision={4} min={kind === 'Adjustment' ? undefined : 0} controls={false} className="amount-input" aria-label={`${t('inventory.quantity')} ${index + 1}`} />
                    </Form.Item>
                  )}
                  {kind !== 'Transfer' && (
                    <Form.Item name={[field.name, 'unitCost']} label={index === 0 ? t('inventory.unitCost') : undefined}>
                      <InputNumber min={0} precision={Math.max(minorUnits, 2)} controls={false} className="amount-input" aria-label={`${t('inventory.unitCost')} ${index + 1}`} />
                    </Form.Item>
                  )}
                  <Button type="link" danger onClick={() => remove(field.name)} aria-label={`${t('voucher.removeRow')} ${index + 1}`}>
                    {t('voucher.removeRow')}
                  </Button>
                </div>
              ))}
              <Button onClick={() => add({ warehouseId: warehouses.find((w) => w.isDefault)?.id ?? warehouses[0]?.id })}>{t('inventory.addLine')}</Button>
            </div>
          )}
        </Form.List>
        {kind !== 'Transfer' && <p className="muted">{t('inventory.unitCostHelp')}</p>}
      </Form>
    </Modal>
  )
}
