import { Alert, App, Button, Form, Input, InputNumber, Modal, Segmented, Select, Space, Switch, Table, Tag } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useSearchParams } from 'react-router'
import { createEmployee, deleteEmployee, setEmployeeActive, updateEmployee } from '../../api/generated/baba'
import type { EmployeeDto, EmployeeInput } from '../../api/generated/model'
import { ApiError } from '../../api/http'
import { refreshBooks, useCostCenters, useCurrencies, useCurrentCompany, useEmployees, useModules, useSalaryComponents } from '../../api/hooks'
import { AmountText } from '../../layout/AmountText'
import { DateField } from '../../layout/DateField'
import { EmptyState } from '../../layout/EmptyState'
import { errorMessage } from '../../layout/errors'
import { ExportControls } from '../../layout/ExportControls'
import { ImportModal } from '../../layout/ImportModal'
import { ListPage } from '../../layout/ListPage'
import { useShortcuts } from '../../layout/useShortcuts'
import { useSettings } from '../../settings/SettingsContext'
import { formatDate, toIsoDate } from '../../utils/format'
import { matchesSearch } from '../../utils/arabic'
import { CostCenterSelect, itemName } from '../accounting/LookupSelects'
import { exportAndShow } from '../reports/exportReport'
import { ComponentsTab, LeaveTab, type PayrollTab } from './EmployeeTabs'

/** Employees, their leave and the salary components they can be given (brief section 10.4). */
export function EmployeesPage() {
  const { t } = useTranslation()
  const [params, setParams] = useSearchParams()
  const tab: PayrollTab = params.get('tab') === 'leave' ? 'leave' : params.get('tab') === 'components' ? 'components' : 'employees'
  const [form, setForm] = useState<{ open: boolean; editing?: EmployeeDto }>({ open: false })
  const [importing, setImporting] = useState(false)
  const [componentForm, setComponentForm] = useState(false)
  const [leaveForm, setLeaveForm] = useState(false)

  const tabs = (
    <Segmented
      value={tab}
      onChange={(value) => setParams(value === 'employees' ? {} : { tab: value as string })}
      options={[
        { value: 'employees', label: t('employees.tabs.employees') },
        { value: 'leave', label: t('employees.tabs.leave') },
        { value: 'components', label: t('employees.tabs.components') },
      ]}
      aria-label={t('employees.tabs.label')}
    />
  )

  const onNew = () => (tab === 'employees' ? setForm({ open: true }) : tab === 'leave' ? setLeaveForm(true) : setComponentForm(true))
  useShortcuts({ 'ctrl+n': onNew })

  return (
    <ListPage
      title={t('employees.title')}
      help="employees"
      newLabel={t(`employees.new.${tab}`)}
      onNew={onNew}
      actions={
        tab === 'employees' ? (
          <>
            <ExportControls run={(format, layout) => exportAndShow('employees', {}, format, layout)} />
            <Button onClick={() => setImporting(true)}>{t('employees.import')}</Button>
          </>
        ) : tab === 'components' ? (
          <ExportControls run={(format, layout) => exportAndShow('salary-components', {}, format, layout)} />
        ) : (
          <ExportControls run={(format, layout) => exportAndShow('leave-balances', { AsOf: toIsoDate(new Date()) }, format, layout)} />
        )
      }
      filters={tabs}
    >
      {tab === 'employees' && <EmployeesTable onEdit={(e) => setForm({ open: true, editing: e })} />}
      {tab === 'leave' && <LeaveTab formOpen={leaveForm} onCloseForm={() => setLeaveForm(false)} />}
      {tab === 'components' && <ComponentsTab formOpen={componentForm} onCloseForm={() => setComponentForm(false)} />}
      <EmployeeFormModal open={form.open} editing={form.editing} onClose={() => setForm({ open: false })} />
      <ImportModal
        open={importing}
        onClose={() => setImporting(false)}
        title={t('employees.importTitle')}
        intro={t('employees.importIntro')}
        columns="Code, Name, Name (Arabic), Job title, ID number, National, Join date, Basic salary, Bank, Account number, Annual leave days"
        url="/api/import/employees"
        templateKey="employees"
      />
    </ListPage>
  )
}

function EmployeesTable({ onEdit }: { onEdit: (employee: EmployeeDto) => void }) {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const { message, modal } = App.useApp()
  const queryClient = useQueryClient()
  const query = useEmployees()
  const company = useCurrentCompany()
  const currencies = useCurrencies()
  const minorUnits = currencies.data?.find((c) => c.code === company.data?.baseCurrencyCode)?.minorUnits ?? 2
  const [search, setSearch] = useState('')
  const employees = useMemo(() => query.data ?? [], [query.data])
  const rows = useMemo(() => employees.filter((e) => matchesSearch(search, e.code, e.nameAr, e.nameEn)), [employees, search])

  const toggle = useMutation({
    mutationFn: (e: EmployeeDto) => setEmployeeActive(e.id, { active: !e.isActive }),
    onSuccess: () => refreshBooks(queryClient),
    onError: (error) => void message.error(errorMessage(error, t)),
  })
  const remove = useMutation({
    mutationFn: (e: EmployeeDto) => deleteEmployee(e.id),
    onSuccess: async () => {
      await refreshBooks(queryClient)
      void message.success(t('employees.deleted'))
    },
    onError: (error) => {
      const inUse = error instanceof ApiError && error.issues.some((i) => i.code === 'employee.in-use')
      void message.error(inUse ? t('employees.issues.employee.in-use') : errorMessage(error, t))
    },
  })

  const columns: ColumnsType<EmployeeDto> = [
    { title: t('parties.code'), dataIndex: 'code', width: 110, render: (code: string) => <span dir="ltr">{code}</span> },
    { title: t('parties.name'), key: 'name', render: (_: unknown, e) => <span className={e.isActive ? '' : 'muted'}>{itemName(e, settings.language)}</span> },
    { title: t('employees.jobTitle'), dataIndex: 'jobTitle', width: 170 },
    { title: t('employees.joined'), key: 'joined', width: 120, render: (_: unknown, e) => formatDate(e.joinDate, settings.digits, settings.hijri) },
    { title: t('employees.basicSalary'), key: 'basic', width: 130, align: 'end', render: (_: unknown, e) => <AmountText value={e.basicSalary} minorUnits={minorUnits} /> },
    { title: t('parties.status'), key: 'status', width: 120, render: (_: unknown, e) => (e.leaveDate ? <Tag>{t('employees.left')}</Tag> : e.isActive ? t('parties.active') : <Tag>{t('parties.inactive')}</Tag>) },
    {
      title: <span className="visually-hidden">{t('voucher.rowActions')}</span>,
      key: 'actions',
      width: 260,
      render: (_: unknown, e) => (
        <Space size={0} wrap>
          <Button type="link" onClick={() => onEdit(e)} aria-label={`${t('parties.edit')} ${e.code}`}>
            {t('parties.edit')}
          </Button>
          <Button type="link" onClick={() => toggle.mutate(e)} aria-label={`${e.isActive ? t('parties.deactivate') : t('parties.activate')} ${e.code}`}>
            {e.isActive ? t('parties.deactivate') : t('parties.activate')}
          </Button>
          {!e.inUse && (
            <Button
              type="link"
              danger
              aria-label={`${t('parties.delete')} ${e.code}`}
              onClick={() =>
                modal.confirm({
                  title: t('employees.deleteTitle', { name: `${e.code} ${itemName(e, settings.language)}` }),
                  okText: t('parties.delete'),
                  okButtonProps: { danger: true },
                  cancelText: t('common.cancel'),
                  onOk: () => remove.mutateAsync(e).catch(() => undefined),
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
        <Input.Search value={search} onChange={(e) => setSearch(e.target.value)} placeholder={t('employees.search')} aria-label={t('employees.search')} allowClear className="filter-search" />
      </div>
      {query.isSuccess && employees.length === 0 ? (
        <EmptyState title={t('employees.emptyTitle')} body={t('employees.emptyBody')} />
      ) : (
        <Table<EmployeeDto> columns={columns} dataSource={rows} rowKey="id" loading={query.isPending} pagination={false} size="middle" bordered />
      )}
    </>
  )
}

interface Values {
  code: string
  nameEn: string
  nameAr: string
  jobTitle?: string
  nationalId?: string
  isNational: boolean
  joinDate: string
  leaveDate?: string
  basicSalary: number
  bankName?: string
  bankAccount?: string
  costCenterId?: string
  annualLeaveDays: number
  leaveBalanceDays: number
  leaveBalanceDate?: string
  notes?: string
  components: { componentId?: string; value: number | null }[]
}

const fieldOf: Record<string, keyof Values> = {
  code: 'code',
  name: 'nameEn',
  joinDate: 'joinDate',
  leaveDate: 'leaveDate',
  basicSalary: 'basicSalary',
  annualLeaveDays: 'annualLeaveDays',
  costCenter: 'costCenterId',
}

function EmployeeFormModal({ open, editing, onClose }: { open: boolean; editing?: EmployeeDto; onClose: () => void }) {
  return open ? <EmployeeForm editing={editing} onClose={onClose} /> : null
}

function EmployeeForm({ editing, onClose }: { editing?: EmployeeDto; onClose: () => void }) {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const queryClient = useQueryClient()
  const [form] = Form.useForm<Values>()
  const employees = useEmployees().data ?? []
  const components = useSalaryComponents().data ?? []
  const costCenters = useCostCenters().data ?? []
  const modules = useModules()
  const [problems, setProblems] = useState<string[]>([])

  useEffect(() => {
    const next = Math.max(0, ...employees.map((e) => Number(/^\D*(\d+)$/.exec(e.code)?.[1] ?? 0))) + 1
    form.setFieldsValue(
      editing
        ? {
            code: editing.code,
            nameEn: editing.nameEn,
            nameAr: editing.nameAr,
            jobTitle: editing.jobTitle ?? '',
            nationalId: editing.nationalId ?? '',
            isNational: editing.isNational,
            joinDate: editing.joinDate,
            leaveDate: editing.leaveDate ?? undefined,
            basicSalary: editing.basicSalary,
            bankName: editing.bankName ?? '',
            bankAccount: editing.bankAccount ?? '',
            costCenterId: editing.costCenterId ?? undefined,
            annualLeaveDays: editing.annualLeaveDays,
            leaveBalanceDays: editing.leaveBalanceDays,
            leaveBalanceDate: editing.leaveBalanceDate ?? undefined,
            notes: editing.notes ?? '',
            components: editing.components.map((c) => ({ componentId: c.componentId, value: c.value })),
          }
        : { code: `E${String(next).padStart(3, '0')}`, nameEn: '', nameAr: '', isNational: false, joinDate: toIsoDate(new Date()), basicSalary: 0, annualLeaveDays: 30, leaveBalanceDays: 0, components: [] },
    )
    // eslint-disable-next-line react-hooks/exhaustive-deps -- the suggested code is worked out when the window opens
  }, [editing, form])

  const save = useMutation({
    mutationFn: (values: Values) => {
      const input: EmployeeInput = {
        code: values.code,
        nameEn: values.nameEn ?? '',
        nameAr: values.nameAr ?? '',
        jobTitle: values.jobTitle?.trim() || null,
        nationalId: values.nationalId?.trim() || null,
        isNational: values.isNational ?? false,
        joinDate: values.joinDate,
        leaveDate: values.leaveDate ?? null,
        basicSalary: values.basicSalary ?? 0,
        bankName: values.bankName?.trim() || null,
        bankAccount: values.bankAccount?.trim() || null,
        costCenterId: values.costCenterId ?? null,
        annualLeaveDays: values.annualLeaveDays ?? 30,
        leaveBalanceDays: values.leaveBalanceDays ?? 0,
        leaveBalanceDate: values.leaveBalanceDays ? (values.leaveBalanceDate ?? values.joinDate) : (values.leaveBalanceDate ?? null),
        notes: values.notes?.trim() || null,
        components: (values.components ?? []).filter((c) => c.componentId).map((c) => ({ componentId: c.componentId!, value: c.value ?? 0 })),
      }
      return editing ? updateEmployee(editing.id, input) : createEmployee(input)
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
      form.setFields(error.issues.filter((i) => fieldOf[i.field]).map((issue) => ({ name: fieldOf[issue.field]!, errors: [t([`employees.issues.${issue.code}`], { defaultValue: issue.code })] })))
      setProblems(error.issues.filter((i) => !fieldOf[i.field]).map((i) => t([`employees.issues.${i.code}`], { defaultValue: i.code })))
    },
  })

  return (
    <Modal
      open
      title={editing ? t('employees.edit') : t('employees.new.employees')}
      onCancel={onClose}
      onOk={() => form.submit()}
      okText={t('common.save')}
      cancelText={t('common.cancel')}
      confirmLoading={save.isPending}
      maskClosable={false}
      width={720}
    >
      <Form form={form} layout="vertical" onFinish={(values) => save.mutate(values)} requiredMark={false}>
        {problems.length > 0 && <Alert type="error" showIcon className="form-alert" message={problems.join(' ')} />}
        <div className="form-row">
          <Form.Item name="code" label={t('parties.code')} rules={[{ required: true, message: t('employees.issues.employee.code-required') }]}>
            <Input dir="ltr" autoFocus />
          </Form.Item>
          <Form.Item name="jobTitle" label={t('employees.jobTitle')}>
            <Input />
          </Form.Item>
        </div>
        <Form.Item name="nameEn" label={t('parties.nameEn')} extra={t('parties.nameHelp')}>
          <Input dir="ltr" />
        </Form.Item>
        <Form.Item name="nameAr" label={t('parties.nameAr')}>
          <Input dir="rtl" />
        </Form.Item>
        <div className="form-row">
          <Form.Item name="nationalId" label={t('employees.nationalId')}>
            <Input dir="ltr" />
          </Form.Item>
          <Form.Item name="isNational" label={t('employees.isNational')} valuePropName="checked" extra={t('employees.isNationalHelp')}>
            <Switch aria-label={t('employees.isNational')} />
          </Form.Item>
        </div>
        <div className="form-row">
          <Form.Item name="joinDate" label={t('employees.joined')}>
            <DateField value={undefined} onChange={() => undefined} ariaLabel={t('employees.joined')} />
          </Form.Item>
          <Form.Item name="leaveDate" label={t('employees.leaveDate')} extra={t('employees.leaveDateHelp')}>
            <DateField value={undefined} onChange={() => undefined} allowEmpty ariaLabel={t('employees.leaveDate')} />
          </Form.Item>
          <Form.Item name="basicSalary" label={t('employees.basicSalary')}>
            <InputNumber min={0} precision={3} controls={false} className="amount-input" />
          </Form.Item>
        </div>
        <div className="form-row">
          <Form.Item name="bankName" label={t('employees.bankName')}>
            <Input />
          </Form.Item>
          <Form.Item name="bankAccount" label={t('employees.bankAccount')}>
            <Input dir="ltr" />
          </Form.Item>
        </div>
        {modules.has('cost-centers') && (
          <Form.Item name="costCenterId" label={t('reports.costCenter')}>
            <CostCenterSelect value={undefined} onChange={() => undefined} costCenters={costCenters} ariaLabel={t('reports.costCenter')} />
          </Form.Item>
        )}
        <Form.List name="components">
          {(fields, { add, remove }) => (
            <div className="price-lines">
              <strong>{t('employees.allowancesAndDeductions')}</strong>
              {fields.map((field, index) => (
                <div className="form-row" key={field.key}>
                  <Form.Item name={[field.name, 'componentId']} label={index === 0 ? t('employees.component') : undefined} className="grow">
                    <Select
                      showSearch
                      optionFilterProp="label"
                      popupMatchSelectWidth={false}
                      aria-label={`${t('employees.component')} ${index + 1}`}
                      options={components.filter((c) => c.isActive || editing?.components.some((x) => x.componentId === c.id)).map((c) => ({ value: c.id, label: `${c.code} — ${itemName(c, settings.language)}` }))}
                      onChange={(id: string) => {
                        const chosen = components.find((c) => c.id === id)
                        if (chosen) form.setFieldValue(['components', field.name, 'value'], chosen.defaultValue)
                      }}
                    />
                  </Form.Item>
                  <Form.Item name={[field.name, 'value']} label={index === 0 ? t('employees.componentValue') : undefined}>
                    <InputNumber min={0} precision={3} controls={false} className="amount-input" aria-label={`${t('employees.componentValue')} ${index + 1}`} />
                  </Form.Item>
                  <Button type="link" danger onClick={() => remove(field.name)} aria-label={`${t('voucher.removeRow')} ${index + 1}`}>
                    {t('voucher.removeRow')}
                  </Button>
                </div>
              ))}
              <Button onClick={() => add({ value: null })}>{t('employees.addComponent')}</Button>
              <p className="muted">{t('employees.componentValueHelp')}</p>
            </div>
          )}
        </Form.List>
        <div className="form-row">
          <Form.Item name="annualLeaveDays" label={t('employees.annualLeaveDays')}>
            <InputNumber min={0} max={366} precision={0} controls={false} className="amount-input" />
          </Form.Item>
          <Form.Item name="leaveBalanceDays" label={t('employees.leaveBroughtForward')} extra={t('employees.leaveBroughtForwardHelp')}>
            <InputNumber min={0} precision={2} controls={false} className="amount-input" />
          </Form.Item>
          <Form.Item name="leaveBalanceDate" label={t('employees.leaveBalanceDate')}>
            <DateField value={undefined} onChange={() => undefined} allowEmpty ariaLabel={t('employees.leaveBalanceDate')} />
          </Form.Item>
        </div>
        <Form.Item name="notes" label={t('inventory.notes')}>
          <Input />
        </Form.Item>
      </Form>
    </Modal>
  )
}
