import { Alert, Form, Input, Modal, Typography } from 'antd'
import { useMutation } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { turnOnAccounts } from '../../api/generated/baba'
import { minPasswordLength, securityMessage } from './messages'
import { useSessionUpdate } from './sessionUpdate'

interface Values {
  userName: string
  displayName: string
  password: string
  confirm: string
}

/**
 * Switches user accounts on for the company by making the first person: an administrator, who is signed in at once. From then on
 * everyone signs in with their own name and password after the company file is opened.
 */
export function TurnOnAccountsModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const { t } = useTranslation()
  const [form] = Form.useForm<Values>()
  const update = useSessionUpdate()
  const turnOn = useMutation({
    mutationFn: (values: Values) => turnOnAccounts({ userName: values.userName, displayName: values.displayName, password: values.password }),
    onSuccess: async (response) => {
      await update(response)
      onClose()
    },
  })

  return (
    <Modal
      open={open}
      title={t('security.turnOn.title')}
      onCancel={onClose}
      onOk={() => form.submit()}
      okText={t('security.turnOn.button')}
      cancelText={t('common.cancel')}
      confirmLoading={turnOn.isPending}
      maskClosable={false}
      destroyOnHidden
    >
      <Typography.Paragraph>{t('security.turnOn.intro')}</Typography.Paragraph>
      <Alert type="warning" showIcon message={t('security.turnOn.warning')} className="form-alert" />
      <Form form={form} layout="vertical" requiredMark={false} onFinish={(values) => turnOn.mutate(values)}>
        {turnOn.isError && <Alert type="error" showIcon message={securityMessage(turnOn.error, t)} className="form-alert" />}
        <Form.Item name="displayName" label={t('security.displayName')} rules={[{ required: true, message: t('security.displayNameRequired') }]}>
          <Input autoFocus autoComplete="off" />
        </Form.Item>
        <Form.Item name="userName" label={t('security.userName')} extra={t('security.userNameHelp')} rules={[{ required: true, message: t('security.signIn.userRequired') }]}>
          <Input autoComplete="off" dir="ltr" />
        </Form.Item>
        <Form.Item
          name="password"
          label={t('security.password')}
          extra={t('security.passwordHelp', { min: minPasswordLength })}
          rules={[{ required: true, min: minPasswordLength, message: t('security.issues.user.password-short', { min: minPasswordLength }) }]}
        >
          <Input.Password autoComplete="new-password" dir="ltr" />
        </Form.Item>
        <Form.Item
          name="confirm"
          label={t('security.confirmPassword')}
          dependencies={['password']}
          rules={[
            { required: true, message: t('security.passwordRequired') },
            ({ getFieldValue }) => ({
              validator: (_, value) => (!value || getFieldValue('password') === value ? Promise.resolve() : Promise.reject(new Error(t('security.passwordsDiffer')))),
            }),
          ]}
        >
          <Input.Password autoComplete="new-password" dir="ltr" />
        </Form.Item>
      </Form>
    </Modal>
  )
}
