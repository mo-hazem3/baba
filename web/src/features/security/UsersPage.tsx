import { Button, Tabs } from 'antd'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { RoleDto, UserDto } from '../../api/generated/model'
import { EmptyState } from '../../layout/EmptyState'
import { PageHeader } from '../../layout/PageHeader'
import { RolesTab } from './RolesTab'
import { TurnOnAccountsModal } from './TurnOnAccountsModal'
import { UsersTab } from './UsersTab'
import { useAccess } from './useAccess'

/** Who can sign in and what each role may do (brief section 10.4). While the company has no accounts there is only the way to turn them on. */
export function UsersPage() {
  const { t } = useTranslation()
  const { accountsOn, can } = useAccess()
  const [tab, setTab] = useState<'users' | 'roles'>('users')
  const [userForm, setUserForm] = useState<{ open: boolean; editing?: UserDto }>({ open: false })
  const [resetFor, setResetFor] = useState<UserDto>()
  const [roleForm, setRoleForm] = useState<{ open: boolean; editing?: RoleDto }>({ open: false })
  const [turningOn, setTurningOn] = useState(false)
  const mayEdit = can('users', 'Edit')

  return (
    <div>
      <PageHeader
        title={t('security.users.title')}
        help="users"
        crumbs={[{ label: t('breadcrumb.home'), to: '/' }, { label: t('security.users.title') }]}
        action={
          accountsOn && mayEdit ? (
            tab === 'users' ? (
              <Button type="primary" onClick={() => setUserForm({ open: true })}>
                + {t('security.users.new')}
              </Button>
            ) : (
              <Button type="primary" onClick={() => setRoleForm({ open: true })}>
                + {t('security.roles.new')}
              </Button>
            )
          ) : undefined
        }
      />
      {!accountsOn ? (
        <EmptyState
          title={t('security.users.offTitle')}
          body={t('security.users.offBody')}
          action={
            mayEdit ? (
              <Button type="primary" onClick={() => setTurningOn(true)}>
                {t('security.turnOn.open')}
              </Button>
            ) : undefined
          }
        />
      ) : (
        <Tabs
          activeKey={tab}
          onChange={(key) => setTab(key as 'users' | 'roles')}
          items={[
            {
              key: 'users',
              label: t('security.users.tab'),
              children: (
                <UsersTab
                  form={userForm}
                  onClose={() => setUserForm({ open: false })}
                  resetFor={resetFor}
                  onResetClose={() => setResetFor(undefined)}
                  onEdit={(user) => setUserForm({ open: true, editing: user })}
                  onReset={setResetFor}
                />
              ),
            },
            {
              key: 'roles',
              label: t('security.roles.tab'),
              children: <RolesTab form={roleForm} onClose={() => setRoleForm({ open: false })} onEdit={(role) => setRoleForm({ open: true, editing: role })} />,
            },
          ]}
        />
      )}
      <TurnOnAccountsModal open={turningOn} onClose={() => setTurningOn(false)} />
    </div>
  )
}
