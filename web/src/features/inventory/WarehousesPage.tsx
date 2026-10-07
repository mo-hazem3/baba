import { Alert, App, Button, Form, Input, Modal, Space, Table, Tag } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { createWarehouse, deleteWarehouse, setDefaultWarehouse, setWarehouseActive, updateWarehouse } from '../../api/generated/baba'
import type { WarehouseDto } from '../../api/generated/model'
import { ApiError } from '../../api/http'
import { refreshBooks, useWarehouses } from '../../api/hooks'
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

function WarehouseFormModal({ open, editing, onClose }: { open: boolean; editing?: WarehouseDto; onClose: () => void }) {
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
      return editing ? updateWarehouse(editing.id, input) : createWarehouse(input)
    },
    onSuccess: async () => {
      await refreshBooks(queryClient)
      onClose()
    },
    onError: (error) => {
      if (!(error instanceof ApiError) || error.issues.length === 0) return
      form.setFields(error.issues.map((issue) => ({ name: fieldOf[issue.field] ?? 'code', errors: [t(`inventory.issues.${issue.code}`, { defaultValue: issue.code })] })))
    },
  })

  return (
    <Modal
      open={open}
      title={editing ? t('inventory.editWarehouse') : t('inventory.newWarehouse')}
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
        <Form.Item name="code" label={t('parties.code')} rules={[{ required: true, message: t('inventory.issues.warehouse.code-required') }]}>
          <Input dir="ltr" autoFocus />
        </Form.Item>
        <Form.Item name="nameEn" label={t('parties.nameEn')} extra={t('parties.nameHelp')}>
          <Input dir="ltr" />
        </Form.Item>
        <Form.Item name="nameAr" label={t('parties.nameAr')}>
          <Input dir="rtl" />
        </Form.Item>
      </Form>
    </Modal>
  )
}

/** The places stock is kept (brief section 10.4): every stock movement names one, and transfers move stock between them. */
export function WarehousesPage() {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const { message, modal } = App.useApp()
  const queryClient = useQueryClient()
  const query = useWarehouses()
  const warehouses = query.data ?? []
  const [form, setForm] = useState<{ open: boolean; editing?: WarehouseDto }>({ open: false })

  const onError = (error: unknown) => {
    const known = error instanceof ApiError ? error.issues.find((i) => i.code.startsWith('warehouse.')) : undefined
    void message.error(known ? t(`inventory.issues.${known.code}`) : errorMessage(error, t))
  }

  const toggle = useMutation({
    mutationFn: (w: WarehouseDto) => setWarehouseActive(w.id, { active: !w.isActive }),
    onSuccess: () => refreshBooks(queryClient),
    onError,
  })
  const makeDefault = useMutation({
    mutationFn: (w: WarehouseDto) => setDefaultWarehouse(w.id),
    onSuccess: () => refreshBooks(queryClient),
    onError,
  })
  const remove = useMutation({
    mutationFn: (w: WarehouseDto) => deleteWarehouse(w.id),
    onSuccess: async () => {
      await refreshBooks(queryClient)
      void message.success(t('inventory.warehouseDeleted'))
    },
    onError,
  })

  useShortcuts({ 'ctrl+n': () => setForm({ open: true }) })

  const columns: ColumnsType<WarehouseDto> = [
    { title: t('parties.code'), dataIndex: 'code', width: 130, render: (code: string) => <span dir="ltr">{code}</span> },
    {
      title: t('parties.name'),
      key: 'name',
      render: (_: unknown, w) => (
        <span className={w.isActive ? '' : 'muted'}>
          {itemName(w, settings.language)} {w.isDefault && <Tag color="blue">{t('inventory.defaultWarehouse')}</Tag>}
        </span>
      ),
    },
    { title: t('parties.status'), key: 'status', width: 110, render: (_: unknown, w) => (w.isActive ? t('parties.active') : <Tag>{t('parties.inactive')}</Tag>) },
    {
      title: <span className="visually-hidden">{t('voucher.rowActions')}</span>,
      key: 'actions',
      width: 380,
      render: (_: unknown, w) => (
        <Space size={0} wrap>
          <Button type="link" onClick={() => setForm({ open: true, editing: w })} aria-label={`${t('parties.edit')} ${w.code}`}>
            {t('parties.edit')}
          </Button>
          {!w.isDefault && w.isActive && (
            <Button type="link" onClick={() => makeDefault.mutate(w)} aria-label={`${t('inventory.makeDefault')} ${w.code}`}>
              {t('inventory.makeDefault')}
            </Button>
          )}
          <Button type="link" onClick={() => toggle.mutate(w)} aria-label={`${w.isActive ? t('parties.deactivate') : t('parties.activate')} ${w.code}`}>
            {w.isActive ? t('parties.deactivate') : t('parties.activate')}
          </Button>
          {!w.inUse && (
            <Button
              type="link"
              danger
              aria-label={`${t('parties.delete')} ${w.code}`}
              onClick={() =>
                modal.confirm({
                  title: t('inventory.deleteWarehouseTitle', { name: `${w.code} ${itemName(w, settings.language)}` }),
                  okText: t('parties.delete'),
                  okButtonProps: { danger: true },
                  cancelText: t('common.cancel'),
                  onOk: () => remove.mutateAsync(w).catch(() => undefined),
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
      title={t('inventory.warehouses')}
      help="warehouses"
      newLabel={t('inventory.newWarehouse')}
      onNew={() => setForm({ open: true })}
      actions={<ExportControls run={(format, layout) => exportAndShow('warehouses', {}, format, layout)} />}
    >
      {query.isSuccess && warehouses.length === 0 ? (
        <EmptyState title={t('inventory.emptyWarehousesTitle')} body={t('inventory.emptyWarehousesBody')} />
      ) : (
        <Table<WarehouseDto> columns={columns} dataSource={[...warehouses]} rowKey="id" loading={query.isPending} pagination={false} size="middle" bordered />
      )}
      <WarehouseFormModal open={form.open} editing={form.editing} onClose={() => setForm({ open: false })} />
    </ListPage>
  )
}
