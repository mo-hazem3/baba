import { Alert, App, Button, Form, Input, Modal, Space, Table, Tag } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { createCostCenter, deleteCostCenter, setCostCenterActive, updateCostCenter } from '../../api/generated/baba'
import type { CostCenterDto } from '../../api/generated/model'
import { ApiError } from '../../api/http'
import { refreshBooks, useCostCenters } from '../../api/hooks'
import { EmptyState } from '../../layout/EmptyState'
import { errorMessage } from '../../layout/errors'
import { ExportControls } from '../../layout/ExportControls'
import { ListPage } from '../../layout/ListPage'
import { useShortcuts } from '../../layout/useShortcuts'
import { useSettings } from '../../settings/SettingsContext'
import { itemName } from '../accounting/LookupSelects'
import { exportAndShow } from '../reports/exportReport'

interface Values {
  code: string
  nameEn: string
  nameAr: string
}

const fieldOf: Record<string, keyof Values> = { code: 'code', name: 'nameEn' }

function CostCenterFormModal({ open, editing, onClose }: { open: boolean; editing?: CostCenterDto; onClose: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [form] = Form.useForm<Values>()

  useEffect(() => {
    if (!open) return
    form.resetFields()
    form.setFieldsValue(editing ? { code: editing.code, nameEn: editing.nameEn, nameAr: editing.nameAr } : { code: '', nameEn: '', nameAr: '' })
  }, [open, editing, form])

  const save = useMutation({
    mutationFn: (values: Values) => {
      const input = { code: values.code, nameEn: values.nameEn ?? '', nameAr: values.nameAr ?? '' }
      return editing ? updateCostCenter(editing.id, input) : createCostCenter(input)
    },
    onSuccess: async () => {
      await refreshBooks(queryClient)
      onClose()
    },
    onError: (error) => {
      if (!(error instanceof ApiError) || error.issues.length === 0) return
      form.setFields(
        error.issues.map((issue) => ({ name: fieldOf[issue.field] ?? 'code', errors: [t(`costCenters.issues.${issue.code}`, { defaultValue: issue.code })] })),
      )
    },
  })

  return (
    <Modal
      open={open}
      title={editing ? t('costCenters.edit') : t('costCenters.new')}
      onCancel={onClose}
      onOk={() => form.submit()}
      okText={t('common.save')}
      cancelText={t('common.cancel')}
      confirmLoading={save.isPending}
      maskClosable={false}
      destroyOnHidden
    >
      <Form form={form} layout="vertical" onFinish={(values) => save.mutate(values)} requiredMark="optional">
        {save.isError && !(save.error instanceof ApiError && save.error.issues.length > 0) && (
          <Alert type="error" showIcon message={errorMessage(save.error, t)} className="form-alert" />
        )}
        <Form.Item name="code" label={t('costCenters.code')} rules={[{ required: true, message: t('costCenters.issues.cost-center.code-required') }]}>
          <Input dir="ltr" autoFocus />
        </Form.Item>
        <Form.Item name="nameEn" label={t('costCenters.nameEn')} extra={t('costCenters.nameHelp')}>
          <Input dir="ltr" />
        </Form.Item>
        <Form.Item name="nameAr" label={t('costCenters.nameAr')}>
          <Input dir="rtl" />
        </Form.Item>
      </Form>
    </Modal>
  )
}

/** Cost centers and projects (brief section 10.2): tags that voucher lines can carry, so a profit and loss can be read for each. */
export function CostCentersPage() {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const { message, modal } = App.useApp()
  const queryClient = useQueryClient()
  const query = useCostCenters()
  const costCenters = query.data ?? []
  const [form, setForm] = useState<{ open: boolean; editing?: CostCenterDto }>({ open: false })

  const toggle = useMutation({
    mutationFn: (c: CostCenterDto) => setCostCenterActive(c.id, { active: !c.isActive }),
    onSuccess: () => refreshBooks(queryClient),
    onError: (error) => void message.error(errorMessage(error, t)),
  })

  const remove = useMutation({
    mutationFn: (c: CostCenterDto) => deleteCostCenter(c.id),
    onSuccess: async () => {
      await refreshBooks(queryClient)
      void message.success(t('costCenters.deleted'))
    },
    onError: (error) => {
      const inUse = error instanceof ApiError && error.issues.some((i) => i.code === 'cost-center.in-use')
      void message.error(inUse ? t('costCenters.issues.cost-center.in-use') : errorMessage(error, t))
    },
  })

  const confirmDelete = (costCenter: CostCenterDto) =>
    modal.confirm({
      title: t('costCenters.deleteTitle', { name: `${costCenter.code} ${itemName(costCenter, settings.language)}` }),
      content: t('costCenters.deleteBody'),
      okText: t('costCenters.delete'),
      okButtonProps: { danger: true },
      cancelText: t('common.cancel'),
      onOk: () => remove.mutateAsync(costCenter).catch(() => undefined),
    })

  useShortcuts({ 'ctrl+n': () => setForm({ open: true }) })

  const columns: ColumnsType<CostCenterDto> = [
    { title: t('costCenters.code'), dataIndex: 'code', width: 130, render: (code: string) => <span dir="ltr">{code}</span> },
    { title: t('costCenters.name'), key: 'name', render: (_: unknown, row) => <span className={row.isActive ? '' : 'muted'}>{itemName(row, settings.language)}</span> },
    { title: t('costCenters.status'), key: 'status', width: 110, render: (_: unknown, row) => (row.isActive ? t('costCenters.active') : <Tag>{t('costCenters.inactive')}</Tag>) },
    {
      title: <span className="visually-hidden">{t('voucher.rowActions')}</span>,
      key: 'actions',
      width: 340,
      render: (_: unknown, row) => (
        <Space size={0} wrap>
          <Link to={`/reports/profit-and-loss?costCenterId=${row.id}`}>
            <Button type="link" aria-label={`${t('costCenters.profitAndLoss')} ${row.code}`}>
              {t('costCenters.profitAndLoss')}
            </Button>
          </Link>
          <Button type="link" onClick={() => setForm({ open: true, editing: row })} aria-label={`${t('costCenters.editShort')} ${row.code}`}>
            {t('costCenters.editShort')}
          </Button>
          <Button type="link" onClick={() => toggle.mutate(row)} aria-label={`${row.isActive ? t('costCenters.deactivate') : t('costCenters.activate')} ${row.code}`}>
            {row.isActive ? t('costCenters.deactivate') : t('costCenters.activate')}
          </Button>
          <Button type="link" danger onClick={() => confirmDelete(row)} aria-label={`${t('costCenters.delete')} ${row.code}`}>
            {t('costCenters.delete')}
          </Button>
        </Space>
      ),
    },
  ]

  return (
    <ListPage
      title={t('costCenters.title')}
      help="costCenters"
      newLabel={t('costCenters.new')}
      onNew={() => setForm({ open: true })}
      actions={<ExportControls run={(format, layout) => exportAndShow('cost-center-list', {}, format, layout)} />}
    >
      {query.isSuccess && costCenters.length === 0 ? (
        <EmptyState title={t('costCenters.emptyTitle')} body={t('costCenters.emptyBody')} />
      ) : (
        <Table<CostCenterDto> columns={columns} dataSource={[...costCenters]} rowKey="id" loading={query.isPending} pagination={false} size="middle" bordered />
      )}
      <CostCenterFormModal open={form.open} editing={form.editing} onClose={() => setForm({ open: false })} />
    </ListPage>
  )
}
