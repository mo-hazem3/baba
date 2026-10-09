import { Alert, App, Button, Form, Input, Modal, Select, Space, Switch, Table, Tag, Typography } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { createUser, deleteUser, resetUserPassword, updateUser } from '../../api/generated/baba'
import type { RoleDto, UserDto } from '../../api/generated/model'
import { getListUsersQueryKey, getListRolesQueryKey } from '../../api/generated/baba'
import { useRoles, useUsers } from '../../api/hooks'
import { useSettings } from '../../settings/SettingsContext'
import { formatDateTime } from '../../utils/format'
import { minPasswordLength, securityMessage, securityMessages } from './messages'

export const roleName = (role: { nameEn: string; nameAr: string }, language: string): string =>
  (language === 'ar' ? role.nameAr || role.nameEn : role.nameEn || role.nameAr)

interface UserValues {
  userName: string
  displayName: string
  roleId: string
  password: string
  isActive: boolean
}

function UserModal({ open, editing, roles, onClose }: { open: boolean; editing?: UserDto; roles: readonly RoleDto[]; onClose: () => void }) {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const queryClient = useQueryClient()
  const [form] = Form.useForm<UserValues>()

  useEffect(() => {
    if (!open) return
    form.resetFields()
    form.setFieldsValue(
      editing
        ? { userName: editing.userName, displayName: editing.displayName, roleId: editing.roleId, isActive: editing.isActive }
        : { userName: '', displayName: '', roleId: undefined, password: '', isActive: true },
    )
  }, [open, editing, form])

  const save = useMutation({
    mutationFn: (values: UserValues) =>
      editing
        ? updateUser(editing.id, { displayName: values.displayName, roleId: values.roleId, isActive: values.isActive })
        : createUser({ userName: values.userName, displayName: values.displayName, roleId: values.roleId, password: values.password }),
    onSuccess: async () => {
      await Promise.all([queryClient.invalidateQueries({ queryKey: getListUsersQueryKey() }), queryClient.invalidateQueries({ queryKey: getListRolesQueryKey() })])
      onClose()
    },
  })

  return (
    <Modal
      open={open}
      title={editing ? t('security.users.edit') : t('security.users.new')}
      onCancel={onClose}
      onOk={() => form.submit()}
      okText={t('common.save')}
      cancelText={t('common.cancel')}
      confirmLoading={save.isPending}
      maskClosable={false}
      destroyOnHidden
    >
      <Form form={form} layout="vertical" requiredMark={false} onFinish={(values) => save.mutate(values)}>
        {save.isError && (
          <Alert type="error" showIcon className="form-alert" message={<ul className="issue-list">{securityMessages(save.error, t).map((m) => <li key={m}>{m}</li>)}</ul>} />
        )}
        <Form.Item name="displayName" label={t('security.displayName')} rules={[{ required: true, message: t('security.displayNameRequired') }]}>
          <Input autoFocus autoComplete="off" />
        </Form.Item>
        <Form.Item name="userName" label={t('security.userName')} extra={editing ? undefined : t('security.userNameHelp')} rules={[{ required: true, message: t('security.signIn.userRequired') }]}>
          <Input autoComplete="off" dir="ltr" disabled={editing !== undefined} />
        </Form.Item>
        <Form.Item name="roleId" label={t('security.role')} rules={[{ required: true, message: t('security.roleRequired') }]}>
          <Select
            options={roles.map((r) => ({ value: r.id, label: roleName(r, settings.language) }))}
            placeholder={t('common.choose')}
            aria-label={t('security.role')}
          />
        </Form.Item>
        {!editing && (
          <Form.Item
            name="password"
            label={t('security.firstPassword')}
            extra={t('security.firstPasswordHelp', { min: minPasswordLength })}
            rules={[{ required: true, min: minPasswordLength, message: t('security.issues.user.password-short', { min: minPasswordLength }) }]}
          >
            <Input.Password autoComplete="new-password" dir="ltr" />
          </Form.Item>
        )}
        {editing && (
          <Form.Item name="isActive" label={t('security.users.active')} valuePropName="checked" extra={t('security.users.activeHelp')}>
            <Switch aria-label={t('security.users.active')} />
          </Form.Item>
        )}
      </Form>
    </Modal>
  )
}

function ResetPasswordModal({ user, onClose }: { user?: UserDto; onClose: () => void }) {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [form] = Form.useForm<{ newPassword: string }>()
  const reset = useMutation({
    mutationFn: (values: { newPassword: string }) => resetUserPassword(user!.id, { newPassword: values.newPassword }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: getListUsersQueryKey() })
      void message.success(t('security.users.passwordReset', { name: user!.displayName }))
      onClose()
    },
  })

  return (
    <Modal
      open={user !== undefined}
      title={t('security.users.resetTitle', { name: user?.displayName ?? '' })}
      onCancel={onClose}
      onOk={() => form.submit()}
      okText={t('security.users.reset')}
      cancelText={t('common.cancel')}
      confirmLoading={reset.isPending}
      maskClosable={false}
      destroyOnHidden
    >
      <Typography.Paragraph>{t('security.users.resetIntro')}</Typography.Paragraph>
      <Form form={form} layout="vertical" requiredMark={false} onFinish={(values) => reset.mutate(values)}>
        {reset.isError && <Alert type="error" showIcon message={securityMessage(reset.error, t)} className="form-alert" />}
        <Form.Item
          name="newPassword"
          label={t('security.firstPassword')}
          extra={t('security.firstPasswordHelp', { min: minPasswordLength })}
          rules={[{ required: true, min: minPasswordLength, message: t('security.issues.user.password-short', { min: minPasswordLength }) }]}
        >
          <Input.Password autoFocus autoComplete="new-password" dir="ltr" />
        </Form.Item>
      </Form>
    </Modal>
  )
}

/** The people who can sign in to this company. */
export function UsersTab({ form, onClose, resetFor, onResetClose, onEdit, onReset }: {
  form: { open: boolean; editing?: UserDto }
  onClose: () => void
  resetFor?: UserDto
  onResetClose: () => void
  onEdit: (user: UserDto) => void
  onReset: (user: UserDto) => void
}) {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const { message, modal } = App.useApp()
  const queryClient = useQueryClient()
  const users = useUsers()
  const roles = useRoles()

  const remove = useMutation({
    mutationFn: (user: UserDto) => deleteUser(user.id),
    onSuccess: async () => {
      await Promise.all([queryClient.invalidateQueries({ queryKey: getListUsersQueryKey() }), queryClient.invalidateQueries({ queryKey: getListRolesQueryKey() })])
      void message.success(t('security.users.deleted'))
    },
    onError: (error) => void message.error(securityMessage(error, t)),
  })

  const columns: ColumnsType<UserDto> = [
    { title: t('security.displayName'), dataIndex: 'displayName', key: 'displayName' },
    { title: t('security.userName'), dataIndex: 'userName', key: 'userName', render: (name: string) => <span dir="ltr">{name}</span> },
    { title: t('security.role'), key: 'role', render: (_: unknown, u) => roleName({ nameEn: u.roleNameEn, nameAr: u.roleNameAr }, settings.language) },
    {
      title: t('parties.status'),
      key: 'status',
      render: (_: unknown, u) => (
        <Space size={4} wrap>
          {u.isActive ? t('parties.active') : <Tag>{t('parties.inactive')}</Tag>}
          {u.mustChangePassword && <Tag color="gold">{t('security.users.mustChange')}</Tag>}
        </Space>
      ),
    },
    {
      title: t('security.users.lastSignIn'),
      key: 'last',
      render: (_: unknown, u) => (u.lastSignInAt ? formatDateTime(u.lastSignInAt, settings.digits) : t('security.users.never')),
    },
    {
      title: <span className="visually-hidden">{t('voucher.rowActions')}</span>,
      key: 'actions',
      render: (_: unknown, u) => (
        <Space size={0} wrap>
          <Button type="link" onClick={() => onEdit(u)} aria-label={`${t('parties.edit')} ${u.userName}`}>
            {t('parties.edit')}
          </Button>
          <Button type="link" onClick={() => onReset(u)} aria-label={`${t('security.users.reset')} ${u.userName}`}>
            {t('security.users.reset')}
          </Button>
          <Button
            type="link"
            danger
            aria-label={`${t('parties.delete')} ${u.userName}`}
            onClick={() =>
              modal.confirm({
                title: t('security.users.deleteTitle', { name: u.displayName }),
                content: t('security.users.deleteBody'),
                okText: t('parties.delete'),
                okButtonProps: { danger: true },
                cancelText: t('common.cancel'),
                onOk: () => remove.mutateAsync(u).catch(() => undefined),
              })
            }
          >
            {t('parties.delete')}
          </Button>
        </Space>
      ),
    },
  ]

  return (
    <>
      <Table<UserDto> columns={columns} dataSource={[...(users.data ?? [])]} rowKey="id" loading={users.isPending} pagination={false} size="middle" bordered />
      <UserModal open={form.open} editing={form.editing} roles={roles.data ?? []} onClose={onClose} />
      <ResetPasswordModal user={resetFor} onClose={onResetClose} />
    </>
  )
}
