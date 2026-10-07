import { Alert, Form, Input, InputNumber, Modal } from 'antd'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { createParty, updateParty } from '../../api/generated/baba'
import type { PartyDto, PartyInput, PartyKind } from '../../api/generated/model'
import { ApiError } from '../../api/http'
import { refreshBooks } from '../../api/hooks'
import { errorMessage } from '../../layout/errors'

interface Values {
  code: string
  nameEn: string
  nameAr: string
  phone: string
  email: string
  address: string
  taxNumber: string
  creditLimit: number
  paymentTermsDays: number
  notes: string
}

/** Which form field each problem the API can report belongs to. */
const fieldOf: Record<string, keyof Values> = {
  code: 'code',
  name: 'nameEn',
  creditLimit: 'creditLimit',
  paymentTermsDays: 'paymentTermsDays',
}

/** The next free code such as C001, C002 ... (S001 for suppliers), so the user can just press Save. */
export const suggestPartyCode = (parties: readonly PartyDto[], kind: PartyKind): string => {
  const prefix = kind === 'Customer' ? 'C' : 'S'
  const highest = parties
    .filter((p) => p.kind === kind)
    .map((p) => Number(/^\D*(\d+)$/.exec(p.code)?.[1] ?? 0))
    .reduce((max, n) => Math.max(max, n), 0)
  return `${prefix}${String(highest + 1).padStart(3, '0')}`
}

/** Add or change a customer or a supplier (brief section 10.2). */
export function PartyFormModal({
  open,
  kind,
  editing,
  parties,
  minorUnits,
  onClose,
}: {
  open: boolean
  kind: PartyKind
  editing?: PartyDto
  parties: readonly PartyDto[]
  minorUnits: number
  onClose: () => void
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [form] = Form.useForm<Values>()
  const customer = kind === 'Customer'

  useEffect(() => {
    if (!open) return
    form.resetFields()
    form.setFieldsValue(
      editing
        ? {
            code: editing.code,
            nameEn: editing.nameEn,
            nameAr: editing.nameAr,
            phone: editing.phone ?? '',
            email: editing.email ?? '',
            address: editing.address ?? '',
            taxNumber: editing.taxNumber ?? '',
            creditLimit: editing.creditLimit,
            paymentTermsDays: editing.paymentTermsDays,
            notes: editing.notes ?? '',
          }
        : { code: suggestPartyCode(parties, kind), nameEn: '', nameAr: '', phone: '', email: '', address: '', taxNumber: '', creditLimit: 0, paymentTermsDays: 30, notes: '' },
    )
  }, [open, editing, form, parties, kind])

  const save = useMutation({
    mutationFn: (values: Values) => {
      const input: PartyInput = {
        kind: editing?.kind ?? kind,
        code: values.code,
        nameEn: values.nameEn ?? '',
        nameAr: values.nameAr ?? '',
        phone: values.phone || null,
        email: values.email || null,
        address: values.address || null,
        taxNumber: values.taxNumber || null,
        creditLimit: customer ? (values.creditLimit ?? 0) : 0,
        paymentTermsDays: values.paymentTermsDays ?? 0,
        notes: values.notes || null,
      }
      return editing ? updateParty(editing.id, input) : createParty(input)
    },
    onSuccess: async () => {
      await refreshBooks(queryClient)
      onClose()
    },
    onError: (error) => {
      if (!(error instanceof ApiError) || error.issues.length === 0) return
      form.setFields(
        error.issues.map((issue) => ({
          name: fieldOf[issue.field] ?? 'code',
          errors: [t(`parties.issues.${issue.code}`, { defaultValue: issue.code })],
        })),
      )
    },
  })

  const title = editing ? t(customer ? 'parties.editCustomer' : 'parties.editSupplier') : t(customer ? 'parties.newCustomer' : 'parties.newSupplier')

  return (
    <Modal
      open={open}
      title={title}
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
        {save.isError && !(save.error instanceof ApiError && save.error.issues.length > 0) && (
          <Alert type="error" showIcon message={errorMessage(save.error, t)} className="form-alert" />
        )}
        <Form.Item name="code" label={t('parties.code')} rules={[{ required: true, message: t('parties.issues.party.code-required') }]}>
          <Input dir="ltr" autoFocus />
        </Form.Item>
        <Form.Item name="nameEn" label={t('parties.nameEn')} extra={t('parties.nameHelp')}>
          <Input dir="ltr" />
        </Form.Item>
        <Form.Item name="nameAr" label={t('parties.nameAr')}>
          <Input dir="rtl" />
        </Form.Item>
        <div className="form-row">
          <Form.Item name="phone" label={t('parties.phone')}>
            <Input dir="ltr" />
          </Form.Item>
          <Form.Item name="email" label={t('parties.email')}>
            <Input dir="ltr" type="email" />
          </Form.Item>
        </div>
        <Form.Item name="address" label={t('parties.address')}>
          <Input.TextArea rows={2} />
        </Form.Item>
        <Form.Item name="taxNumber" label={t('parties.taxNumber')} extra={t('parties.taxNumberHelp')}>
          <Input dir="ltr" />
        </Form.Item>
        <div className="form-row">
          {customer && (
            <Form.Item name="creditLimit" label={t('parties.creditLimit')} extra={t('parties.creditLimitHelp')}>
              <InputNumber min={0} precision={minorUnits} controls={false} className="amount-input" />
            </Form.Item>
          )}
          <Form.Item name="paymentTermsDays" label={t('parties.terms')} extra={t('parties.termsHelp')}>
            <InputNumber min={0} max={365} precision={0} controls={false} className="amount-input" />
          </Form.Item>
        </div>
        <Form.Item name="notes" label={t('parties.notes')}>
          <Input.TextArea rows={2} />
        </Form.Item>
      </Form>
    </Modal>
  )
}
