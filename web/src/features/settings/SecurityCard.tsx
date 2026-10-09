import { App, Button, Card, Switch } from 'antd'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { setSecuritySettings } from '../../api/generated/baba'
import { securityMessage } from '../security/messages'
import { useSessionUpdate } from '../security/sessionUpdate'
import { TurnOnAccountsModal } from '../security/TurnOnAccountsModal'
import { useAccess } from '../security/useAccess'

/**
 * People and approval (brief section 10.4). User accounts are off until someone turns them on; once they are on, "ask for approval" makes
 * posting a voucher or issuing an invoice something only people with the approve right can do.
 */
export function SecurityCard() {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const { session, accountsOn, can } = useAccess()
  const update = useSessionUpdate()
  const queryClient = useQueryClient()
  const [turningOn, setTurningOn] = useState(false)

  const toggle = useMutation({
    mutationFn: (required: boolean) => setSecuritySettings({ approvalRequired: required }),
    onSuccess: async (response) => {
      await update(response)
      await queryClient.invalidateQueries()
    },
    onError: (error) => void message.error(securityMessage(error, t)),
  })

  if (!can('users', 'View')) return null

  return (
    <Card title={t('security.card.title')} className="settings-card">
      {!accountsOn ? (
        <>
          <p>{t('security.card.offBody')}</p>
          {can('users', 'Edit') && (
            <Button onClick={() => setTurningOn(true)}>{t('security.turnOn.open')}</Button>
          )}
        </>
      ) : (
        <>
          <p>{t('security.card.onBody')}</p>
          <p>
            <Link to="/users">{t('security.card.manage')}</Link>
          </p>
          {can('users', 'Edit') && (
            <p>
              <Switch
                checked={session?.approvalRequired === true}
                loading={toggle.isPending}
                onChange={(checked) => toggle.mutate(checked)}
                aria-label={t('security.card.approval')}
              />{' '}
              <strong>{t('security.card.approval')}</strong>
              <br />
              <span className="muted">{t('security.card.approvalHelp')}</span>
            </p>
          )}
        </>
      )}
      <TurnOnAccountsModal open={turningOn} onClose={() => setTurningOn(false)} />
    </Card>
  )
}
