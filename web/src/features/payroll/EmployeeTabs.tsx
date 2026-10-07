import { Alert, App, Button, Form, Input, InputNumber, Modal, Select, Space, Switch, Table, Tag } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { addLeave, createSalaryComponent, deleteLeave, deleteSalaryComponent, setSalaryComponentActive, updateLeave, updateSalaryComponent } from '../../api/generated/baba'
import type { LeaveBalanceDto, LeaveDto, LeaveInput, SalaryComponentDto, SalaryComponentInput } from '../../api/generated/model'
import { ApiError } from '../../api/http'
import { refreshBooks, useAccounts, useEmployees, useLeave, useLeaveBalances, useSalaryComponents } from '../../api/hooks'
import { DateField } from '../../layout/DateField'
import { EmptyState } from '../../layout/EmptyState'
import { errorMessage } from '../../layout/errors'
import { QuantityText } from '../../layout/QuantityText'
import { AccountSelect } from '../accounting/AccountSelect'
import { itemName } from '../accounting/LookupSelects'
import { useSettings } from '../../settings/SettingsContext'
import { formatDate, toIsoDate } from '../../utils/format'

export type PayrollTab = 'employees' | 'leave' | 'components'

// ---------------------------------------------------------------- Leave

export function LeaveTab({ formOpen, onCloseForm }: { formOpen: boolean; onCloseForm: () => void }) {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const { message, modal } = App.useApp()
  const queryClient = useQueryClient()
  const query = useLeave()
  const today = useMemo(() => toIsoDate(new Date()), [])
  const balances = useLeaveBalances(today)
  const employees = useEmployees().data ?? []
  const [editing, setEditing] = useState<LeaveDto>()
  const name = (id: string) => {
    const e = employees.find((x) => x.id === id)
    return e ? `${e.code} — ${itemName(e, settings.language)}` : ''
  }

  const remove = useMutation({
    mutationFn: (l: LeaveDto) => deleteLeave(l.id),
    onSuccess: () => refreshBooks(queryClient),
    onError: (error) => void message.error(errorMessage(error, t)),
  })

  const balanceColumns: ColumnsType<LeaveBalanceDto> = [
    { title: t('employees.employee'), key: 'employee', render: (_: unknown, b) => name(b.employeeId) },
    { title: t('employees.leaveEarned'), key: 'earned', width: 130, align: 'end', render: (_: unknown, b) => <QuantityText value={b.earned} /> },
    { title: t('employees.leaveTaken'), key: 'taken', width: 130, align: 'end', render: (_: unknown, b) => <QuantityText value={b.taken} /> },
    { title: t('employees.leaveBalance'), key: 'balance', width: 130, align: 'end', render: (_: unknown, b) => <QuantityText value={b.balance} /> },
  ]

  const columns: ColumnsType<LeaveDto> = [
    { title: t('employees.employee'), key: 'employee', render: (_: unknown, l) => name(l.employeeId) },
    { title: t('employees.leaveKind'), key: 'kind', width: 120, render: (_: unknown, l) => t(`employees.leaveKinds.${l.kind}`) },
    { title: t('reports.from'), key: 'from', width: 120, render: (_: unknown, l) => formatDate(l.from, settings.digits, settings.hijri) },
    { title: t('reports.to'), key: 'to', width: 120, render: (_: unknown, l) => formatDate(l.to, settings.digits, settings.hijri) },
    { title: t('employees.leaveDays'), key: 'days', width: 90, align: 'end', render: (_: unknown, l) => <QuantityText value={l.days} /> },
    { title: t('inventory.notes'), dataIndex: 'notes' },
    {
      title: <span className="visually-hidden">{t('voucher.rowActions')}</span>,
      key: 'actions',
      width: 170,
      render: (_: unknown, l) => (
        <Space size={0}>
          <Button type="link" onClick={() => setEditing(l)} aria-label={`${t('parties.edit')} ${name(l.employeeId)}`}>
            {t('parties.edit')}
          </Button>
          <Button
            type="link"
            danger
            aria-label={`${t('parties.delete')} ${name(l.employeeId)}`}
            onClick={() =>
              modal.confirm({
                title: t('employees.deleteLeaveTitle'),
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

  const leave = query.data ?? []
  return (
    <>
      <h3>{t('employees.leaveBalances')}</h3>
      <Table<LeaveBalanceDto> columns={balanceColumns} dataSource={[...(balances.data ?? [])]} rowKey="employeeId" loading={balances.isPending} pagination={false} size="middle" bordered />
      <h3>{t('employees.leaveTaken')}</h3>
      {query.isSuccess && leave.length === 0 ? (
        <EmptyState title={t('employees.emptyLeaveTitle')} body={t('employees.emptyLeaveBody')} />
      ) : (
        <Table<LeaveDto> columns={columns} dataSource={[...leave]} rowKey="id" loading={query.isPending} pagination={false} size="middle" bordered />
      )}
      {(formOpen || editing) && <LeaveForm editing={editing} onClose={() => { setEditing(undefined); onCloseForm() }} />}
    </>
  )
}

interface LeaveValues {
  employeeId?: string
  kind: LeaveInput['kind']
  from: string
  to: string
  days: number | null
  notes?: string
}

function LeaveForm({ editing, onClose }: { editing?: LeaveDto; onClose: () => void }) {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const queryClient = useQueryClient()
  const [form] = Form.useForm<LeaveValues>()
  const employees = useEmployees().data ?? []
  const [problems, setProblems] = useState<string[]>([])

  useEffect(() => {
    const today = toIsoDate(new Date())
    form.setFieldsValue(editing ? { employeeId: editing.employeeId, kind: editing.kind, from: editing.from, to: editing.to, days: editing.days, notes: editing.notes ?? '' } : { kind: 'Annual', from: today, to: today, days: null })
  }, [editing, form])

  const save = useMutation({
    mutationFn: (values: LeaveValues) => {
      const input: LeaveInput = { employeeId: values.employeeId!, kind: values.kind, from: values.from, to: values.to, days: values.days ?? null, notes: values.notes?.trim() || null }
      return editing ? updateLeave(editing.id, input) : addLeave(input)
    },
    onSuccess: async () => {
      await refreshBooks(queryClient)
      onClose()
    },
    onError: (error) =>
      setProblems(error instanceof ApiError && error.issues.length > 0 ? error.issues.map((i) => t([`employees.issues.${i.code}`], { defaultValue: i.code })) : [errorMessage(error, t)]),
  })

  return (
    <Modal open title={editing ? t('employees.editLeave') : t('employees.new.leave')} onCancel={onClose} onOk={() => form.submit()} okText={t('common.save')} cancelText={t('common.cancel')} confirmLoading={save.isPending} maskClosable={false}>
      <Form form={form} layout="vertical" onFinish={(values) => save.mutate(values)} requiredMark={false}>
        {problems.length > 0 && <Alert type="error" showIcon className="form-alert" message={problems.join(' ')} />}
        <Form.Item name="employeeId" label={t('employees.employee')} rules={[{ required: true, message: t('employees.issues.leave.employee-unknown') }]}>
          <Select showSearch optionFilterProp="label" aria-label={t('employees.employee')} options={employees.filter((e) => e.isActive).map((e) => ({ value: e.id, label: `${e.code} — ${itemName(e, settings.language)}` }))} />
        </Form.Item>
        <Form.Item name="kind" label={t('employees.leaveKind')}>
          <Select aria-label={t('employees.leaveKind')} options={(['Annual', 'Sick', 'Unpaid', 'Other'] as const).map((k) => ({ value: k, label: t(`employees.leaveKinds.${k}`) }))} />
        </Form.Item>
        <div className="form-row">
          <Form.Item name="from" label={t('reports.from')}>
            <DateField value={undefined} onChange={() => undefined} ariaLabel={t('reports.from')} />
          </Form.Item>
          <Form.Item name="to" label={t('reports.to')}>
            <DateField value={undefined} onChange={() => undefined} ariaLabel={t('reports.to')} />
          </Form.Item>
          <Form.Item name="days" label={t('employees.leaveDays')} extra={t('employees.leaveDaysHelp')}>
            <InputNumber min={0} precision={2} controls={false} className="amount-input" />
          </Form.Item>
        </div>
        <Form.Item name="notes" label={t('inventory.notes')}>
          <Input />
        </Form.Item>
      </Form>
    </Modal>
  )
}

// ---------------------------------------------------------------- Salary components

export function ComponentsTab({ formOpen, onCloseForm }: { formOpen: boolean; onCloseForm: () => void }) {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const { message, modal } = App.useApp()
  const queryClient = useQueryClient()
  const query = useSalaryComponents()
  const [editing, setEditing] = useState<SalaryComponentDto>()

  const toggle = useMutation({
    mutationFn: (c: SalaryComponentDto) => setSalaryComponentActive(c.id, { active: !c.isActive }),
    onSuccess: () => refreshBooks(queryClient),
    onError: (error) => void message.error(errorMessage(error, t)),
  })
  const remove = useMutation({
    mutationFn: (c: SalaryComponentDto) => deleteSalaryComponent(c.id),
    onSuccess: () => refreshBooks(queryClient),
    onError: (error) => {
      const inUse = error instanceof ApiError && error.issues.some((i) => i.code === 'component.in-use')
      void message.error(inUse ? t('employees.issues.component.in-use') : errorMessage(error, t))
    },
  })

  const columns: ColumnsType<SalaryComponentDto> = [
    { title: t('parties.code'), dataIndex: 'code', width: 130, render: (code: string) => <span dir="ltr">{code}</span> },
    { title: t('parties.name'), key: 'name', render: (_: unknown, c) => <span className={c.isActive ? '' : 'muted'}>{itemName(c, settings.language)}</span> },
    { title: t('assets.type'), key: 'kind', width: 120, render: (_: unknown, c) => t(`employees.componentKinds.${c.kind}`) },
    { title: t('employees.calculation'), key: 'calculation', width: 170, render: (_: unknown, c) => `${t(`employees.calculations.${c.calculation}`)}: ${c.defaultValue}` },
    { title: t('parties.status'), key: 'status', width: 100, render: (_: unknown, c) => (c.isActive ? t('parties.active') : <Tag>{t('parties.inactive')}</Tag>) },
    {
      title: <span className="visually-hidden">{t('voucher.rowActions')}</span>,
      key: 'actions',
      width: 260,
      render: (_: unknown, c) => (
        <Space size={0} wrap>
          <Button type="link" onClick={() => setEditing(c)} aria-label={`${t('parties.edit')} ${c.code}`}>
            {t('parties.edit')}
          </Button>
          <Button type="link" onClick={() => toggle.mutate(c)} aria-label={`${c.isActive ? t('parties.deactivate') : t('parties.activate')} ${c.code}`}>
            {c.isActive ? t('parties.deactivate') : t('parties.activate')}
          </Button>
          {!c.inUse && (
            <Button
              type="link"
              danger
              aria-label={`${t('parties.delete')} ${c.code}`}
              onClick={() =>
                modal.confirm({
                  title: t('employees.deleteComponentTitle', { name: `${c.code} ${itemName(c, settings.language)}` }),
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

  const components = query.data ?? []
  return (
    <>
      <p className="muted">{t('employees.componentsIntro')}</p>
      {query.isSuccess && components.length === 0 ? (
        <EmptyState title={t('employees.emptyComponentsTitle')} body={t('employees.emptyComponentsBody')} />
      ) : (
        <Table<SalaryComponentDto> columns={columns} dataSource={[...components]} rowKey="id" loading={query.isPending} pagination={false} size="middle" bordered />
      )}
      {(formOpen || editing) && <ComponentForm editing={editing} onClose={() => { setEditing(undefined); onCloseForm() }} />}
    </>
  )
}

interface ComponentValues {
  code: string
  nameEn: string
  nameAr: string
  kind: SalaryComponentInput['kind']
  calculation: SalaryComponentInput['calculation']
  defaultValue: number
  accountId?: string
  isInsurable: boolean
  inEndOfService: boolean
}

const componentFieldOf: Record<string, keyof ComponentValues> = { code: 'code', name: 'nameEn', defaultValue: 'defaultValue', account: 'accountId' }

function ComponentForm({ editing, onClose }: { editing?: SalaryComponentDto; onClose: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [form] = Form.useForm<ComponentValues>()
  const accounts = useAccounts().data ?? []
  const kind = Form.useWatch('kind', form)
  const [problems, setProblems] = useState<string[]>([])

  useEffect(() => {
    form.setFieldsValue(
      editing
        ? { code: editing.code, nameEn: editing.nameEn, nameAr: editing.nameAr, kind: editing.kind, calculation: editing.calculation, defaultValue: editing.defaultValue, accountId: editing.accountId ?? undefined, isInsurable: editing.isInsurable, inEndOfService: editing.inEndOfService }
        : { code: '', nameEn: '', nameAr: '', kind: 'Earning', calculation: 'Fixed', defaultValue: 0, isInsurable: false, inEndOfService: false },
    )
  }, [editing, form])

  const save = useMutation({
    mutationFn: (values: ComponentValues) => {
      const input: SalaryComponentInput = {
        code: values.code,
        nameEn: values.nameEn ?? '',
        nameAr: values.nameAr ?? '',
        kind: values.kind,
        calculation: values.calculation,
        defaultValue: values.defaultValue ?? 0,
        accountId: values.accountId ?? null,
        isInsurable: values.kind === 'Earning' && values.isInsurable,
        inEndOfService: values.kind === 'Earning' && values.inEndOfService,
      }
      return editing ? updateSalaryComponent(editing.id, input) : createSalaryComponent(input)
    },
    onSuccess: async () => {
      await refreshBooks(queryClient)
      onClose()
    },
    onError: (error) => {
      if (!(error instanceof ApiError) || error.issues.length === 0) {
        setProblems([errorMessage(error, t)])
        return
      }
      form.setFields(error.issues.filter((i) => componentFieldOf[i.field]).map((issue) => ({ name: componentFieldOf[issue.field]!, errors: [t([`employees.issues.${issue.code}`], { defaultValue: issue.code })] })))
      setProblems(error.issues.filter((i) => !componentFieldOf[i.field]).map((i) => t([`employees.issues.${i.code}`], { defaultValue: i.code })))
    },
  })

  return (
    <Modal open title={editing ? t('employees.editComponent') : t('employees.new.components')} onCancel={onClose} onOk={() => form.submit()} okText={t('common.save')} cancelText={t('common.cancel')} confirmLoading={save.isPending} maskClosable={false}>
      <Form form={form} layout="vertical" onFinish={(values) => save.mutate(values)} requiredMark={false}>
        {problems.length > 0 && <Alert type="error" showIcon className="form-alert" message={problems.join(' ')} />}
        <Form.Item name="code" label={t('parties.code')} rules={[{ required: true, message: t('employees.issues.component.code-required') }]}>
          <Input dir="ltr" autoFocus />
        </Form.Item>
        <Form.Item name="nameEn" label={t('parties.nameEn')} extra={t('parties.nameHelp')}>
          <Input dir="ltr" />
        </Form.Item>
        <Form.Item name="nameAr" label={t('parties.nameAr')}>
          <Input dir="rtl" />
        </Form.Item>
        <div className="form-row">
          <Form.Item name="kind" label={t('assets.type')}>
            <Select aria-label={t('assets.type')} options={(['Earning', 'Deduction'] as const).map((k) => ({ value: k, label: t(`employees.componentKinds.${k}`) }))} />
          </Form.Item>
          <Form.Item name="calculation" label={t('employees.calculation')}>
            <Select aria-label={t('employees.calculation')} options={(['Fixed', 'PercentOfBasic'] as const).map((k) => ({ value: k, label: t(`employees.calculations.${k}`) }))} />
          </Form.Item>
          <Form.Item name="defaultValue" label={t('employees.defaultValue')} extra={t('employees.defaultValueHelp')}>
            <InputNumber min={0} precision={3} controls={false} className="amount-input" />
          </Form.Item>
        </div>
        <Form.Item name="accountId" label={t(kind === 'Deduction' ? 'employees.deductionAccount' : 'employees.earningAccount')} extra={t(kind === 'Deduction' ? 'employees.deductionAccountHelp' : 'employees.earningAccountHelp')}>
          <AccountSelect value={undefined} onChange={() => undefined} accounts={accounts.filter((a) => (kind === 'Deduction' ? true : a.type === 'Expense'))} ariaLabel={t('employees.componentAccount')} />
        </Form.Item>
        {kind !== 'Deduction' && (
          <>
            <Form.Item name="isInsurable" label={t('employees.insurable')} valuePropName="checked" extra={t('employees.insurableHelp')}>
              <Switch aria-label={t('employees.insurable')} />
            </Form.Item>
            <Form.Item name="inEndOfService" label={t('employees.inEndOfService')} valuePropName="checked" extra={t('employees.inEndOfServiceHelp')}>
              <Switch aria-label={t('employees.inEndOfService')} />
            </Form.Item>
          </>
        )}
      </Form>
    </Modal>
  )
}
