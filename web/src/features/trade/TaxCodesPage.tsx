import { Alert, App, Button, Form, Input, InputNumber, Modal, Select, Space, Table, Tag } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { createTaxCode, deleteTaxCode, setDefaultTaxCode, setTaxCodeActive, updateTaxCode } from '../../api/generated/baba'
import type { TaxCodeDto, TaxCodeInput, TaxTreatment } from '../../api/generated/model'
import { ApiError } from '../../api/http'
import { refreshBooks, useAccounts, useTaxCodes } from '../../api/hooks'
import { AmountText } from '../../layout/AmountText'
import { DateField } from '../../layout/DateField'
import { EmptyState } from '../../layout/EmptyState'
import { errorMessage } from '../../layout/errors'
import { ExportControls } from '../../layout/ExportControls'
import { ListPage } from '../../layout/ListPage'
import { exportAndShow } from '../reports/exportReport'
import { useShortcuts } from '../../layout/useShortcuts'
import { useSettings } from '../../settings/SettingsContext'
import { formatDate, parseIsoDate } from '../../utils/format'
import { AccountSelect } from '../accounting/AccountSelect'
import { itemName } from '../accounting/LookupSelects'

const treatments: TaxTreatment[] = ['Standard', 'Zero', 'Exempt', 'OutOfScope']

/** The company's tax codes (brief sections 8 and 10.3): the ones of its country, which it can switch off, and its own. */
export function TaxCodesPage() {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const { message, modal } = App.useApp()
  const queryClient = useQueryClient()
  const query = useTaxCodes()
  const accounts = useAccounts().data ?? []
  const codes = query.data ?? []
  const [form, setForm] = useState<{ open: boolean; editing?: TaxCodeDto }>({ open: false })

  const accountName = (id: string | null) => {
    const account = accounts.find((a) => a.id === id)
    return account ? `${account.code} ${itemName(account, settings.language)}` : ''
  }
  const date = (value: string | null) => (value ? formatDate(parseIsoDate(value), settings.digits, settings.hijri) : '')

  const toggle = useMutation({
    mutationFn: (c: TaxCodeDto) => setTaxCodeActive(c.id, { active: !c.isActive }),
    onSuccess: () => refreshBooks(queryClient),
    onError: (error) => void message.error(errorMessage(error, t)),
  })
  const makeDefault = useMutation({
    mutationFn: (c: TaxCodeDto) => setDefaultTaxCode(c.id),
    onSuccess: () => refreshBooks(queryClient),
    onError: (error) => void message.error(errorMessage(error, t)),
  })
  const remove = useMutation({
    mutationFn: (c: TaxCodeDto) => deleteTaxCode(c.id),
    onSuccess: async () => {
      await refreshBooks(queryClient)
      void message.success(t('tax.deleted'))
    },
    onError: (error) => void message.error(errorMessage(error, t)),
  })

  useShortcuts({ 'ctrl+n': () => setForm({ open: true }) })

  const columns: ColumnsType<TaxCodeDto> = [
    { title: t('parties.code'), dataIndex: 'code', width: 150, render: (code: string) => <span dir="ltr">{code}</span> },
    {
      title: t('parties.name'),
      key: 'name',
      render: (_: unknown, c) => (
        <Space size={6}>
          <span className={c.isActive ? '' : 'muted'}>{itemName(c, settings.language)}</span>
          {c.isDefault && <Tag color="blue">{t('tax.default')}</Tag>}
        </Space>
      ),
    },
    { title: t('tax.rate'), key: 'rate', width: 90, align: 'end', render: (_: unknown, c) => <AmountText value={c.rate} minorUnits={2} /> },
    { title: t('tax.treatment'), key: 'treatment', width: 140, render: (_: unknown, c) => t(`tax.treatments.${c.treatment}`) },
    { title: t('tax.effective'), key: 'effective', width: 200, render: (_: unknown, c) => [date(c.effectiveFrom), date(c.effectiveTo)].filter(Boolean).join(' → ') },
    { title: t('tax.outputAccount'), key: 'out', render: (_: unknown, c) => accountName(c.outputAccountId) || <span className="muted">{t('tax.noAccount')}</span> },
    { title: t('tax.inputAccount'), key: 'in', render: (_: unknown, c) => accountName(c.inputAccountId) || <span className="muted">{t('tax.noAccount')}</span> },
    {
      title: <span className="visually-hidden">{t('voucher.rowActions')}</span>,
      key: 'actions',
      width: 340,
      render: (_: unknown, c) => (
        <Space size={0} wrap>
          <Button type="link" onClick={() => setForm({ open: true, editing: c })} aria-label={`${t('parties.edit')} ${c.code}`}>
            {t('parties.edit')}
          </Button>
          {c.isActive && !c.isDefault && (
            <Button type="link" onClick={() => makeDefault.mutate(c)} aria-label={`${t('tax.makeDefault')} ${c.code}`}>
              {t('tax.makeDefault')}
            </Button>
          )}
          <Button type="link" onClick={() => toggle.mutate(c)} aria-label={`${c.isActive ? t('parties.deactivate') : t('parties.activate')} ${c.code}`}>
            {c.isActive ? t('parties.deactivate') : t('parties.activate')}
          </Button>
          {!c.fromPack && !c.inUse && (
            <Button
              type="link"
              danger
              aria-label={`${t('parties.delete')} ${c.code}`}
              onClick={() =>
                modal.confirm({
                  title: t('tax.deleteTitle', { name: c.code }),
                  okText: t('parties.delete'),
                  okButtonProps: { danger: true },
                  cancelText: t('common.cancel'),
                  onOk: () => remove.mutateAsync(c).catch(() => undefined),
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
    <ListPage
      title={t('tax.title')}
      help="taxCodes"
      newLabel={t('tax.new')}
      onNew={() => setForm({ open: true })}
      actions={<ExportControls run={(format, layout) => exportAndShow('tax-codes', {}, format, layout)} />}
    >
      <Alert type="info" showIcon className="form-alert" message={t('tax.intro')} />
      {query.isSuccess && codes.length === 0 ? (
        <EmptyState title={t('tax.emptyTitle')} body={t('tax.emptyBody')} />
      ) : (
        <Table<TaxCodeDto> columns={columns} dataSource={codes} rowKey="id" loading={query.isPending} pagination={false} size="middle" bordered />
      )}
      <TaxCodeModal open={form.open} editing={form.editing} onClose={() => setForm({ open: false })} />
    </ListPage>
  )
}

interface Values {
  code: string
  nameEn: string
  nameAr: string
  rate: number
  treatment: TaxTreatment
  effectiveFrom?: string
  effectiveTo?: string
  outputAccountId?: string
  inputAccountId?: string
}

const fieldOf: Record<string, keyof Values> = {
  code: 'code',
  name: 'nameEn',
  rate: 'rate',
  effectiveTo: 'effectiveTo',
  outputAccount: 'outputAccountId',
  inputAccount: 'inputAccountId',
}

function TaxCodeModal({ open, editing, onClose }: { open: boolean; editing?: TaxCodeDto; onClose: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [form] = Form.useForm<Values>()
  const accounts = useAccounts().data ?? []
  const fixed = editing?.fromPack === true // the law decides a pack code's rate, treatment and dates
  const treatment = Form.useWatch('treatment', form)

  useEffect(() => {
    if (!open) return
    form.resetFields()
    form.setFieldsValue(
      editing
        ? {
            code: editing.code,
            nameEn: editing.nameEn,
            nameAr: editing.nameAr,
            rate: editing.rate,
            treatment: editing.treatment,
            effectiveFrom: editing.effectiveFrom ?? undefined,
            effectiveTo: editing.effectiveTo ?? undefined,
            outputAccountId: editing.outputAccountId ?? undefined,
            inputAccountId: editing.inputAccountId ?? undefined,
          }
        : { code: '', nameEn: '', nameAr: '', rate: 0, treatment: 'Standard' },
    )
  }, [open, editing, form])

  const save = useMutation({
    mutationFn: (values: Values) => {
      const input: TaxCodeInput = {
        code: values.code,
        nameEn: values.nameEn ?? '',
        nameAr: values.nameAr ?? '',
        rate: values.rate ?? 0,
        treatment: values.treatment,
        effectiveFrom: values.effectiveFrom ?? null,
        effectiveTo: values.effectiveTo ?? null,
        outputAccountId: values.outputAccountId ?? null,
        inputAccountId: values.inputAccountId ?? null,
      }
      return editing ? updateTaxCode(editing.id, input) : createTaxCode(input)
    },
    onSuccess: async () => {
      await refreshBooks(queryClient)
      onClose()
    },
    onError: (error) => {
      if (!(error instanceof ApiError) || error.issues.length === 0) return
      form.setFields(error.issues.map((issue) => ({ name: fieldOf[issue.field] ?? 'code', errors: [t(`tax.issues.${issue.code}`, { defaultValue: issue.code })] })))
    },
  })

  return (
    <Modal
      open={open}
      title={editing ? t('tax.edit') : t('tax.new')}
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
        {fixed && <Alert type="info" showIcon className="form-alert" message={t('tax.packNote')} />}
        <Form.Item name="code" label={t('parties.code')} rules={[{ required: true, message: t('tax.issues.tax.code-required') }]}>
          <Input dir="ltr" disabled={fixed} autoFocus={!fixed} />
        </Form.Item>
        <Form.Item name="nameEn" label={t('parties.nameEn')}>
          <Input dir="ltr" />
        </Form.Item>
        <Form.Item name="nameAr" label={t('parties.nameAr')}>
          <Input dir="rtl" />
        </Form.Item>
        <div className="form-row">
          <Form.Item name="treatment" label={t('tax.treatment')}>
            <Select disabled={fixed} options={treatments.map((x) => ({ value: x, label: t(`tax.treatments.${x}`) }))} />
          </Form.Item>
          <Form.Item name="rate" label={t('tax.rate')} extra={t('tax.rateHelp')}>
            <InputNumber min={0} max={100} precision={2} controls={false} className="amount-input" disabled={fixed || (treatment !== undefined && treatment !== 'Standard')} addonAfter="%" />
          </Form.Item>
        </div>
        <div className="form-row">
          <Form.Item name="effectiveFrom" label={t('tax.effectiveFrom')}>
            <DateField value={undefined} onChange={() => undefined} allowEmpty disabled={fixed} />
          </Form.Item>
          <Form.Item name="effectiveTo" label={t('tax.effectiveTo')}>
            <DateField value={undefined} onChange={() => undefined} allowEmpty disabled={fixed} />
          </Form.Item>
        </div>
        <Form.Item name="outputAccountId" label={t('tax.outputAccount')} extra={t('tax.outputAccountHelp')}>
          <AccountSelect value={undefined} onChange={() => undefined} accounts={accounts.filter((a) => a.type === 'Liability')} ariaLabel={t('tax.outputAccount')} />
        </Form.Item>
        <Form.Item name="inputAccountId" label={t('tax.inputAccount')} extra={t('tax.inputAccountHelp')}>
          <AccountSelect value={undefined} onChange={() => undefined} accounts={accounts.filter((a) => a.type === 'Asset')} ariaLabel={t('tax.inputAccount')} />
        </Form.Item>
      </Form>
    </Modal>
  )
}
