import { Alert, Button, Card, Form, Input, Typography } from 'antd'
import { useMutation } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { recoverAdministrator, signIn } from '../../api/generated/baba'
import type { CompanyInfo } from '../../api/generated/model'
import { useSettings } from '../../settings/SettingsContext'
import { minPasswordLength, securityMessage } from './messages'
import { useSessionUpdate } from './sessionUpdate'

/**
 * Shown after the company file is opened when the company has user accounts: who are you? A forgotten administrator password is
 * recovered with the company file's own password, which only the owner knows.
 */
export function SignInPage({ company }: { company: CompanyInfo }) {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const [recovering, setRecovering] = useState(false)
  const update = useSessionUpdate()
  const name = settings.language === 'ar' ? company.nameAr : company.nameEn

  const signingIn = useMutation({
    mutationFn: (values: { userName: string; password: string }) => signIn(values),
    onSuccess: update,
  })
  const recover = useMutation({
    mutationFn: (values: { filePassword: string; userName: string; displayName: string; newPassword: string }) => recoverAdministrator(values),
    onSuccess: update,
  })

  return (
    <div className="sign-in-page">
      {!recovering ? (
        <Card title={t('security.signIn.title', { name })}>
          <Typography.Paragraph>{t('security.signIn.intro')}</Typography.Paragraph>
          <Form layout="vertical" requiredMark={false} onFinish={(values) => signingIn.mutate(values)}>
            {signingIn.isError && <Alert type="error" showIcon message={securityMessage(signingIn.error, t)} className="form-alert" />}
            <Form.Item name="userName" label={t('security.userName')} rules={[{ required: true, message: t('security.signIn.userRequired') }]}>
              <Input autoFocus autoComplete="username" dir="ltr" />
            </Form.Item>
            <Form.Item name="password" label={t('security.password')} rules={[{ required: true, message: t('security.signIn.passwordRequired') }]}>
              <Input.Password autoComplete="current-password" dir="ltr" />
            </Form.Item>
            <div className="form-buttons">
              <Button type="primary" htmlType="submit" loading={signingIn.isPending}>
                {t('security.signIn.button')}
              </Button>
              <Button type="link" onClick={() => setRecovering(true)}>
                {t('security.signIn.forgot')}
              </Button>
            </div>
          </Form>
        </Card>
      ) : (
        <Card title={t('security.recover.title')}>
          <Typography.Paragraph>{t('security.recover.intro')}</Typography.Paragraph>
          <Form layout="vertical" requiredMark={false} onFinish={(values) => recover.mutate(values)}>
            {recover.isError && <Alert type="error" showIcon message={securityMessage(recover.error, t)} className="form-alert" />}
            <Form.Item name="filePassword" label={t('security.recover.filePassword')} extra={t('security.recover.filePasswordHelp')} rules={[{ required: true, message: t('security.recover.filePasswordRequired') }]}>
              <Input.Password autoFocus autoComplete="off" dir="ltr" />
            </Form.Item>
            <Form.Item name="userName" label={t('security.recover.userName')} extra={t('security.recover.userNameHelp')} rules={[{ required: true, message: t('security.signIn.userRequired') }]}>
              <Input autoComplete="off" dir="ltr" />
            </Form.Item>
            <Form.Item name="displayName" label={t('security.displayName')} rules={[{ required: true, message: t('security.displayNameRequired') }]}>
              <Input autoComplete="off" />
            </Form.Item>
            <Form.Item
              name="newPassword"
              label={t('security.newPassword')}
              extra={t('security.passwordHelp', { min: minPasswordLength })}
              rules={[{ required: true, min: minPasswordLength, message: t('security.issues.user.password-short', { min: minPasswordLength }) }]}
            >
              <Input.Password autoComplete="new-password" dir="ltr" />
            </Form.Item>
            <div className="form-buttons">
              <Button type="primary" htmlType="submit" loading={recover.isPending}>
                {t('security.recover.button')}
              </Button>
              <Button onClick={() => setRecovering(false)}>{t('security.recover.back')}</Button>
            </div>
          </Form>
        </Card>
      )}
    </div>
  )
}
