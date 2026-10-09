import { Alert, Button, Card, Form, Input, Modal, Typography } from 'antd'
import { useMutation } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { changeOwnPassword } from '../../api/generated/baba'
import { minPasswordLength, securityMessage } from './messages'
import { useSessionUpdate } from './sessionUpdate'

interface Values {
  oldPassword: string
  newPassword: string
  confirm: string
}

function ChangePasswordForm({ intro, onDone, onCancel }: { intro: string; onDone?: () => void; onCancel?: () => void }) {
  const { t } = useTranslation()
  const update = useSessionUpdate()
  const change = useMutation({
    mutationFn: (values: Values) => changeOwnPassword({ oldPassword: values.oldPassword, newPassword: values.newPassword }),
    onSuccess: async (response) => {
      await update(response)
      onDone?.()
    },
  })

  return (
    <Form layout="vertical" requiredMark={false} onFinish={(values) => change.mutate(values)}>
      <Typography.Paragraph>{intro}</Typography.Paragraph>
      {change.isError && <Alert type="error" showIcon message={securityMessage(change.error, t)} className="form-alert" />}
      <Form.Item name="oldPassword" label={t('security.oldPassword')} rules={[{ required: true, message: t('security.passwordRequired') }]}>
        <Input.Password autoFocus autoComplete="current-password" dir="ltr" />
      </Form.Item>
      <Form.Item
        name="newPassword"
        label={t('security.newPassword')}
        extra={t('security.passwordHelp', { min: minPasswordLength })}
        rules={[{ required: true, min: minPasswordLength, message: t('security.issues.user.password-short', { min: minPasswordLength }) }]}
      >
        <Input.Password autoComplete="new-password" dir="ltr" />
      </Form.Item>
      <Form.Item
        name="confirm"
        label={t('security.confirmPassword')}
        dependencies={['newPassword']}
        rules={[
          { required: true, message: t('security.passwordRequired') },
          ({ getFieldValue }) => ({
            validator: (_, value) => (!value || getFieldValue('newPassword') === value ? Promise.resolve() : Promise.reject(new Error(t('security.passwordsDiffer')))),
          }),
        ]}
      >
        <Input.Password autoComplete="new-password" dir="ltr" />
      </Form.Item>
      <div className="form-buttons">
        <Button type="primary" htmlType="submit" loading={change.isPending}>
          {t('security.changePassword.button')}
        </Button>
        {onCancel && <Button onClick={onCancel}>{t('common.cancel')}</Button>}
      </div>
    </Form>
  )
}

/** Shown to a new user (or after an administrator reset their password) before anything else: they choose their own. */
export function ChangePasswordPage() {
  const { t } = useTranslation()
  return (
    <div className="sign-in-page">
      <Card title={t('security.changePassword.requiredTitle')}>
        <ChangePasswordForm intro={t('security.changePassword.requiredIntro')} />
      </Card>
    </div>
  )
}

/** "Change my password" from the menu. */
export function ChangePasswordModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const { t } = useTranslation()
  return (
    <Modal open={open} title={t('security.changePassword.title')} onCancel={onClose} footer={null} destroyOnHidden maskClosable={false}>
      <ChangePasswordForm intro={t('security.changePassword.intro')} onDone={onClose} onCancel={onClose} />
    </Modal>
  )
}
