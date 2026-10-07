import { Alert, Form, Input, Modal, Radio, Select } from 'antd'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { createAccount, updateAccount } from '../../api/generated/baba'
import type { AccountDto, AccountInput, AccountRole, AccountType } from '../../api/generated/model'
import { ApiError } from '../../api/http'
import { refreshBooks } from '../../api/hooks'
import { errorMessage } from '../../layout/errors'
import { useSettings } from '../../settings/SettingsContext'
import { accountLabel, possibleParents, suggestCode } from '../accounting/accountTree'

const types: AccountType[] = ['Asset', 'Liability', 'Equity', 'Revenue', 'Expense']
const roles: AccountRole[] = ['None', 'CashOrBank', 'Receivable', 'Payable', 'RetainedEarnings', 'ExchangeDifference']

interface Values {
  parentId: string | null
  type: AccountType
  code: string
  nameEn: string
  nameAr: string
  isPosting: boolean
  role: AccountRole
}

/** Which form field each problem the API can report belongs to. */
const fieldOf: Record<string, keyof Values> = { code: 'code', name: 'nameEn', parent: 'parentId', type: 'type', isPosting: 'isPosting', role: 'role' }

/** Add or change an account, including moving it in the tree by choosing another parent (brief section 10.1). */
export function AccountFormModal({
  open,
  onClose,
  accounts,
  editing,
}: {
  open: boolean
  onClose: () => void
  accounts: readonly AccountDto[]
  editing?: AccountDto
}) {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const queryClient = useQueryClient()
  const [form] = Form.useForm<Values>()
  const type = Form.useWatch('type', form) ?? 'Asset'
  const isPosting = Form.useWatch('isPosting', form) ?? true

  useEffect(() => {
    if (!open) return
    form.resetFields()
    form.setFieldsValue(
      editing
        ? { parentId: editing.parentId ?? null, type: editing.type, code: editing.code, nameEn: editing.nameEn, nameAr: editing.nameAr, isPosting: editing.isPosting, role: editing.role }
        : { parentId: null, type: 'Asset', code: '', nameEn: '', nameAr: '', isPosting: true, role: 'None' },
    )
  }, [open, editing, form])

  const save = useMutation({
    mutationFn: (values: Values) => {
      const input: AccountInput = {
        code: values.code,
        nameEn: values.nameEn ?? '',
        nameAr: values.nameAr ?? '',
        parentId: values.parentId ?? null,
        type: values.type,
        isPosting: values.isPosting,
        role: values.isPosting ? values.role : 'None',
      }
      return editing ? updateAccount(editing.id, input) : createAccount(input)
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
          errors: [t(`accounts.issues.${issue.code}`, { defaultValue: issue.code })],
        })),
      )
    },
  })

  const parents = possibleParents(accounts, type, editing?.id)

  const onParentChange = (parentId: string | null) => {
    const parent = accounts.find((a) => a.id === parentId)
    if (!parent) return
    form.setFieldValue('type', parent.type) // an account always has the type of the group it sits in
    if (!editing && !form.getFieldValue('code')) form.setFieldValue('code', suggestCode(accounts, parent))
  }

  return (
    <Modal
      open={open}
      title={editing ? t('accounts.editTitle') : t('accounts.newTitle')}
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
        <Form.Item name="parentId" label={t('accounts.parent')} extra={t('accounts.parentHelp')}>
          <Select
            allowClear
            showSearch
            optionFilterProp="label"
            placeholder={t('accounts.noParent')}
            onChange={(value) => onParentChange(value ?? null)}
            options={parents.map((a) => ({ value: a.id, label: accountLabel(a, settings.language) }))}
          />
        </Form.Item>
        <div className="form-row">
          <Form.Item name="code" label={t('accounts.code')} rules={[{ required: true, message: t('accounts.issues.account.code-required') }]}>
            <Input dir="ltr" autoFocus />
          </Form.Item>
          <Form.Item name="type" label={t('accounts.type')}>
            <Select
              disabled={Boolean(Form.useWatch('parentId', form))}
              options={types.map((value) => ({ value, label: t(`accounts.types.${value}`) }))}
            />
          </Form.Item>
        </div>
        <Form.Item name="nameEn" label={t('accounts.nameEn')} extra={t('accounts.nameHelp')}>
          <Input dir="ltr" />
        </Form.Item>
        <Form.Item name="nameAr" label={t('accounts.nameAr')}>
          <Input dir="rtl" />
        </Form.Item>
        <Form.Item name="isPosting" label={t('accounts.kind')} extra={t('accounts.kindHelp')}>
          <Radio.Group
            options={[
              { value: true, label: t('accounts.postable') },
              { value: false, label: t('accounts.group') },
            ]}
          />
        </Form.Item>
        {isPosting && (
          <Form.Item name="role" label={t('accounts.role')} extra={t('accounts.roleHelp')}>
            <Select options={roles.map((value) => ({ value, label: t(`accounts.roles.${value}`) }))} />
          </Form.Item>
        )}
      </Form>
    </Modal>
  )
}
