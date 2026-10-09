import { Alert, App, Button, Checkbox, Form, Input, Modal, Select, Space, Table, Tag, Typography } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { createRole, deleteRole, getListRolesQueryKey, getListUsersQueryKey, updateRole } from '../../api/generated/baba'
import type { PermissionAreaDto, RoleDto } from '../../api/generated/model'
import { useModules, usePermissionCatalog, useRoles } from '../../api/hooks'
import { useSettings } from '../../settings/SettingsContext'
import { securityMessage, securityMessages } from './messages'
import { roleName } from './UsersTab'

const actions = ['View', 'Edit', 'Delete', 'Approve'] as const

interface Values {
  nameEn: string
  nameAr: string
}

/**
 * The permission table of a role: a row per part of the program, a box per action. Ticking Edit, Delete or Approve ticks View too (you
 * cannot change what you cannot see), and unticking View clears the rest of the row.
 */
function PermissionTable({ areas, value, onChange, disabled }: { areas: readonly PermissionAreaDto[]; value: ReadonlySet<string>; onChange: (next: Set<string>) => void; disabled: boolean }) {
  const { t } = useTranslation()

  const toggle = (area: string, action: (typeof actions)[number], checked: boolean) => {
    const next = new Set(value)
    if (checked) {
      next.add(`${area}.${action}`)
      if (action !== 'View') next.add(`${area}.View`)
    } else {
      next.delete(`${area}.${action}`)
      if (action === 'View') for (const a of actions) next.delete(`${area}.${a}`)
    }
    onChange(next)
  }

  return (
    <div className="table-scroll">
      <table className="permission-table">
        <thead>
          <tr>
            <th scope="col" className="perm-head">{t('security.roles.area')}</th>
            {actions.map((a) => (
              <th key={a} scope="col" className="perm-head">
                {t(`security.actions.${a}`)}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {areas.map((area) => (
            <tr key={area.area}>
              <th scope="row" className="perm-area">{t(`security.areas.${area.area}`)}</th>
              {actions.map((a) => (
                <td key={a} className="perm-cell">
                  <Checkbox
                    checked={value.has(`${area.area}.${a}`)}
                    disabled={disabled}
                    onChange={(e) => toggle(area.area, a, e.target.checked)}
                    aria-label={`${t(`security.areas.${area.area}`)}: ${t(`security.actions.${a}`)}`}
                  />
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

// Mounted only while the dialog is open, so every opening starts from the role being edited (or an empty one).
function RoleModalBody({ editing, onClose }: { editing?: RoleDto; onClose: () => void }) {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const queryClient = useQueryClient()
  const roles = useRoles()
  const catalog = usePermissionCatalog()
  const modules = useModules()
  const [form] = Form.useForm<Values>()
  const [permissions, setPermissions] = useState<Set<string>>(() => new Set(editing?.permissions ?? []))
  const readOnly = editing?.isBuiltIn === true

  // Areas of optional parts the company has switched off are left out, unless the role already has rights there.
  const areas = useMemo(
    () => (catalog.data ?? []).filter((a) => a.module === null || modules.has(a.module) || [...permissions].some((p) => p.startsWith(`${a.area}.`))),
    [catalog.data, modules, permissions],
  )

  const save = useMutation({
    mutationFn: (values: Values) => {
      const input = { nameEn: values.nameEn ?? '', nameAr: values.nameAr ?? '', permissions: [...permissions] }
      return editing ? updateRole(editing.id, input) : createRole(input)
    },
    onSuccess: async () => {
      await Promise.all([queryClient.invalidateQueries({ queryKey: getListRolesQueryKey() }), queryClient.invalidateQueries({ queryKey: getListUsersQueryKey() })])
      onClose()
    },
  })

  return (
    <Modal
      open
      title={readOnly ? t('security.roles.view', { name: roleName(editing, settings.language) }) : editing ? t('security.roles.edit') : t('security.roles.new')}
      onCancel={onClose}
      onOk={() => (readOnly ? onClose() : form.submit())}
      okText={readOnly ? t('common.close') : t('common.save')}
      cancelText={t('common.cancel')}
      cancelButtonProps={readOnly ? { style: { display: 'none' } } : undefined}
      confirmLoading={save.isPending}
      maskClosable={false}
      width={720}
    >
      {readOnly && <Alert type="info" showIcon message={t('security.roles.builtInNote')} className="form-alert" />}
      <Form form={form} layout="vertical" requiredMark={false} initialValues={{ nameEn: editing?.nameEn ?? '', nameAr: editing?.nameAr ?? '' }} onFinish={(values) => save.mutate(values)}>
        {save.isError && (
          <Alert type="error" showIcon className="form-alert" message={<ul className="issue-list">{securityMessages(save.error, t).map((m) => <li key={m}>{m}</li>)}</ul>} />
        )}
        <Form.Item name="nameEn" label={t('parties.nameEn')} rules={[{ required: true, message: t('security.roles.nameRequired') }]}>
          <Input dir="ltr" autoFocus={!readOnly} disabled={readOnly} />
        </Form.Item>
        <Form.Item name="nameAr" label={t('parties.nameAr')}>
          <Input dir="rtl" disabled={readOnly} />
        </Form.Item>
        {!editing && (
          <Form.Item label={t('security.roles.copyFrom')} extra={t('security.roles.copyFromHelp')}>
            <Select
              allowClear
              placeholder={t('common.choose')}
              aria-label={t('security.roles.copyFrom')}
              options={(roles.data ?? []).map((r) => ({ value: r.id, label: roleName(r, settings.language) }))}
              onChange={(id: string | undefined) => setPermissions(new Set(roles.data?.find((r) => r.id === id)?.permissions ?? []))}
            />
          </Form.Item>
        )}
      </Form>
      <Typography.Paragraph>{t('security.roles.permissionsIntro')}</Typography.Paragraph>
      <PermissionTable areas={areas} value={permissions} onChange={setPermissions} disabled={readOnly} />
      <ul className="muted action-legend">
        {actions.map((a) => (
          <li key={a}>
            <strong>{t(`security.actions.${a}`)}</strong>: {t(`security.actions.${a}Help`)}
          </li>
        ))}
      </ul>
    </Modal>
  )
}

function RoleModal({ open, editing, onClose }: { open: boolean; editing?: RoleDto; onClose: () => void }) {
  return open ? <RoleModalBody key={editing?.id ?? 'new'} editing={editing} onClose={onClose} /> : null
}

/** The sets of permissions people are given. The five built-in roles cannot be changed; a company makes its own by copying one. */
export function RolesTab({ form, onClose, onEdit }: { form: { open: boolean; editing?: RoleDto }; onClose: () => void; onEdit: (role: RoleDto) => void }) {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const { message, modal } = App.useApp()
  const queryClient = useQueryClient()
  const roles = useRoles()

  const remove = useMutation({
    mutationFn: (role: RoleDto) => deleteRole(role.id),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: getListRolesQueryKey() })
      void message.success(t('security.roles.deleted'))
    },
    onError: (error) => void message.error(securityMessage(error, t)),
  })

  const columns: ColumnsType<RoleDto> = [
    {
      title: t('security.role'),
      key: 'name',
      render: (_: unknown, r) => (
        <span>
          {roleName(r, settings.language)} {r.isBuiltIn && <Tag>{t('security.roles.builtIn')}</Tag>}
        </span>
      ),
    },
    { title: t('security.roles.people'), dataIndex: 'users', key: 'users', width: 120 },
    { title: t('security.roles.rights'), key: 'rights', width: 160, render: (_: unknown, r) => t('security.roles.rightsCount', { count: r.permissions.length }) },
    {
      title: <span className="visually-hidden">{t('voucher.rowActions')}</span>,
      key: 'actions',
      width: 220,
      render: (_: unknown, r) => (
        <Space size={0} wrap>
          <Button type="link" onClick={() => onEdit(r)} aria-label={`${r.isBuiltIn ? t('security.roles.open') : t('parties.edit')} ${roleName(r, settings.language)}`}>
            {r.isBuiltIn ? t('security.roles.open') : t('parties.edit')}
          </Button>
          {!r.isBuiltIn && (
            <Button
              type="link"
              danger
              aria-label={`${t('parties.delete')} ${roleName(r, settings.language)}`}
              onClick={() =>
                modal.confirm({
                  title: t('security.roles.deleteTitle', { name: roleName(r, settings.language) }),
                  okText: t('parties.delete'),
                  okButtonProps: { danger: true },
                  cancelText: t('common.cancel'),
                  onOk: () => remove.mutateAsync(r).catch(() => undefined),
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
      <Table<RoleDto> columns={columns} dataSource={[...(roles.data ?? [])]} rowKey="id" loading={roles.isPending} pagination={false} size="middle" bordered />
      <RoleModal open={form.open} editing={form.editing} onClose={onClose} />
    </>
  )
}
