import { Alert, Modal, Typography } from 'antd'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { deleteAccount, moveAccountEntries } from '../../api/generated/baba'
import type { AccountDto } from '../../api/generated/model'
import { ApiError } from '../../api/http'
import { refreshBooks } from '../../api/hooks'
import { errorMessage } from '../../layout/errors'
import { useSettings } from '../../settings/SettingsContext'
import { AccountSelect } from '../accounting/AccountSelect'
import { accountLabel } from '../accounting/accountTree'

/**
 * An account with entries cannot be deleted (brief section 10.1). The app says why and offers the way out: move its entries to
 * another account of the same type, after which the account is empty and is deleted.
 */
export function MoveEntriesModal({
  account,
  accounts,
  onClose,
}: {
  account: AccountDto | undefined
  accounts: readonly AccountDto[]
  onClose: () => void
}) {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const queryClient = useQueryClient()
  const [target, setTarget] = useState<string>()

  const moveAndDelete = useMutation({
    mutationFn: async () => {
      await moveAccountEntries(account!.id, { targetAccountId: target! })
      await deleteAccount(account!.id)
    },
    onSuccess: async () => {
      await refreshBooks(queryClient)
      setTarget(undefined)
      onClose()
    },
  })

  const candidates = accounts.filter((a) => account && a.id !== account.id && a.type === account.type)

  const close = () => {
    setTarget(undefined)
    moveAndDelete.reset()
    onClose()
  }

  return (
    <Modal
      open={account !== undefined}
      title={t('accounts.moveTitle')}
      onCancel={close}
      onOk={() => moveAndDelete.mutate()}
      okText={t('accounts.moveAndDelete')}
      okButtonProps={{ disabled: !target, danger: true }}
      cancelText={t('common.cancel')}
      confirmLoading={moveAndDelete.isPending}
      maskClosable={false}
      destroyOnHidden
    >
      {account && (
        <>
          <Typography.Paragraph>{t('accounts.moveBody', { name: accountLabel(account, settings.language) })}</Typography.Paragraph>
          <AccountSelect
            value={target}
            onChange={setTarget}
            accounts={candidates}
            cashOnly={account.role === 'CashOrBank'}
            placeholder={t('accounts.moveTarget')}
            ariaLabel={t('accounts.moveTarget')}
            autoFocus
          />
          {moveAndDelete.isError && (
            <Alert
              type="error"
              showIcon
              className="form-alert"
              message={
                moveAndDelete.error instanceof ApiError && moveAndDelete.error.issues.length > 0
                  ? t(`accounts.issues.${moveAndDelete.error.issues[0]!.code}`, { defaultValue: errorMessage(moveAndDelete.error, t) })
                  : errorMessage(moveAndDelete.error, t)
              }
            />
          )}
        </>
      )}
    </Modal>
  )
}
