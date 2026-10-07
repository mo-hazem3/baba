import { Alert, Form, InputNumber, Modal, Switch } from 'antd'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { savePayrollSettings } from '../../api/generated/baba'
import type { PayrollSettingsInput } from '../../api/generated/model'
import { ApiError } from '../../api/http'
import { refreshBooks, useAccounts, usePayrollSettings } from '../../api/hooks'
import { errorMessage } from '../../layout/errors'
import { useSettings } from '../../settings/SettingsContext'
import { AccountSelect } from '../accounting/AccountSelect'

type Values = Omit<PayrollSettingsInput, 'insuranceFloor' | 'insuranceCeiling'> & { insuranceFloor?: number | null; insuranceCeiling?: number | null }

const fieldOf: Record<string, keyof Values> = {
  nationalEmployeePercent: 'nationalEmployeePercent',
  nationalEmployerPercent: 'nationalEmployerPercent',
  foreignEmployeePercent: 'foreignEmployeePercent',
  foreignEmployerPercent: 'foreignEmployerPercent',
  insuranceCeiling: 'insuranceCeiling',
  salaryExpenseAccount: 'salaryExpenseAccountId',
  salariesPayableAccount: 'salariesPayableAccountId',
  insuranceExpenseAccount: 'insuranceExpenseAccountId',
  insurancePayableAccount: 'insurancePayableAccountId',
  endOfServiceExpenseAccount: 'endOfServiceExpenseAccountId',
  endOfServiceProvisionAccount: 'endOfServiceProvisionAccountId',
}

/** Where payroll posts, whether new months are posted by themselves, and the social insurance rates (copied from the country, then the company's own). */
export function PayrollSettingsModal({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation()
  const { settings: ui } = useSettings()
  const queryClient = useQueryClient()
  const [form] = Form.useForm<Values>()
  const query = usePayrollSettings()
  const accounts = useAccounts().data ?? []
  const [problems, setProblems] = useState<string[]>([])
  const data = query.data

  useEffect(() => {
    if (!data) return
    form.setFieldsValue({
      autoPost: data.autoPost,
      nationalEmployeePercent: data.nationalEmployeePercent,
      nationalEmployerPercent: data.nationalEmployerPercent,
      foreignEmployeePercent: data.foreignEmployeePercent,
      foreignEmployerPercent: data.foreignEmployerPercent,
      insuranceFloor: data.insuranceFloor,
      insuranceCeiling: data.insuranceCeiling,
      salaryExpenseAccountId: data.salaryExpenseAccountId,
      salariesPayableAccountId: data.salariesPayableAccountId,
      insuranceExpenseAccountId: data.insuranceExpenseAccountId,
      insurancePayableAccountId: data.insurancePayableAccountId,
      endOfServiceExpenseAccountId: data.endOfServiceExpenseAccountId,
      endOfServiceProvisionAccountId: data.endOfServiceProvisionAccountId,
    })
  }, [data, form])

  const save = useMutation({
    mutationFn: (values: Values) =>
      savePayrollSettings({
        ...values,
        insuranceFloor: values.insuranceFloor ?? null,
        insuranceCeiling: values.insuranceCeiling ?? null,
        salaryExpenseAccountId: values.salaryExpenseAccountId ?? null,
        salariesPayableAccountId: values.salariesPayableAccountId ?? null,
        insuranceExpenseAccountId: values.insuranceExpenseAccountId ?? null,
        insurancePayableAccountId: values.insurancePayableAccountId ?? null,
        endOfServiceExpenseAccountId: values.endOfServiceExpenseAccountId ?? null,
        endOfServiceProvisionAccountId: values.endOfServiceProvisionAccountId ?? null,
      }),
    onSuccess: async () => {
      await refreshBooks(queryClient)
      onClose()
    },
    onError: (error) => {
      if (!(error instanceof ApiError) || error.issues.length === 0) {
        setProblems([errorMessage(error, t)])
        return
      }
      form.setFields(error.issues.filter((i) => fieldOf[i.field]).map((issue) => ({ name: fieldOf[issue.field]!, errors: [t([`payroll.issues.${issue.code}`], { defaultValue: issue.code })] })))
      setProblems(error.issues.filter((i) => !fieldOf[i.field]).map((i) => t([`payroll.issues.${i.code}`], { defaultValue: i.code })))
    },
  })

  const insuranceName = data?.insuranceNameEn ? (ui.language === 'ar' ? (data.insuranceNameAr ?? data.insuranceNameEn) : data.insuranceNameEn) : null
  const percent = (name: keyof Values, label: string) => (
    <Form.Item name={name} label={label}>
      <InputNumber min={0} max={100} precision={2} controls={false} className="amount-input" addonAfter="%" />
    </Form.Item>
  )
  const account = (name: keyof Values, label: string, type: 'Expense' | 'Liability') => (
    <Form.Item name={name} label={label}>
      <AccountSelect value={undefined} onChange={() => undefined} accounts={accounts.filter((a) => a.type === type)} ariaLabel={label} />
    </Form.Item>
  )

  return (
    <Modal open title={t('payroll.settings')} onCancel={onClose} onOk={() => form.submit()} okText={t('common.save')} cancelText={t('common.cancel')} confirmLoading={save.isPending} maskClosable={false} width={720}>
      <Form form={form} layout="vertical" onFinish={(values) => save.mutate(values)} requiredMark={false}>
        {problems.length > 0 && <Alert type="error" showIcon className="form-alert" message={problems.join(' ')} />}
        <Form.Item name="autoPost" label={t('payroll.autoPost')} valuePropName="checked" extra={t('payroll.autoPostHelp')}>
          <Switch aria-label={t('payroll.autoPost')} />
        </Form.Item>

        <h4>{t('payroll.insurance')}</h4>
        <p className="muted">{insuranceName ? t('payroll.insuranceIntro', { name: insuranceName }) : t('payroll.insuranceNone')}</p>
        <div className="form-row">
          {percent('nationalEmployeePercent', t('payroll.nationalEmployee'))}
          {percent('nationalEmployerPercent', t('payroll.nationalEmployer'))}
        </div>
        <div className="form-row">
          {percent('foreignEmployeePercent', t('payroll.foreignEmployee'))}
          {percent('foreignEmployerPercent', t('payroll.foreignEmployer'))}
        </div>
        <div className="form-row">
          <Form.Item name="insuranceFloor" label={t('payroll.insuranceFloor')} extra={t('payroll.insuranceFloorHelp')}>
            <InputNumber min={0} precision={3} controls={false} className="amount-input" />
          </Form.Item>
          <Form.Item name="insuranceCeiling" label={t('payroll.insuranceCeiling')} extra={t('payroll.insuranceCeilingHelp')}>
            <InputNumber min={0} precision={3} controls={false} className="amount-input" />
          </Form.Item>
        </div>

        <h4>{t('payroll.accounts')}</h4>
        {account('salaryExpenseAccountId', t('payroll.salaryExpenseAccount'), 'Expense')}
        {account('salariesPayableAccountId', t('payroll.salariesPayableAccount'), 'Liability')}
        {account('insuranceExpenseAccountId', t('payroll.insuranceExpenseAccount'), 'Expense')}
        {account('insurancePayableAccountId', t('payroll.insurancePayableAccount'), 'Liability')}
        {data?.hasEndOfService && (
          <>
            {account('endOfServiceExpenseAccountId', t('payroll.eosExpenseAccount'), 'Expense')}
            {account('endOfServiceProvisionAccountId', t('payroll.eosProvisionAccount'), 'Liability')}
          </>
        )}
      </Form>
    </Modal>
  )
}
