import { Alert, Button, Form, Input, Modal, Typography } from 'antd'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router'
import { openCompany } from '../../api/generated/baba'
import { refreshCompany } from '../../api/hooks'
import { errorMessage } from '../../layout/errors'

export interface OpenTarget {
  /** The file to open, or null to ask for the location (used when the host has no file dialog). */
  path: string | null
  name: string
}

interface Values {
  path?: string
  password: string
}

/** Asks for the file password and opens the company. Wrong passwords are explained next to the field. */
export function OpenCompanyModal({ target, onClose }: { target: OpenTarget | null; onClose: () => void }) {
  const { t } = useTranslation()
  const [form] = Form.useForm<Values>()
  const queryClient = useQueryClient()
  const navigate = useNavigate()

  const open = useMutation({
    mutationFn: (values: Values) => openCompany({ path: target?.path ?? values.path ?? '', password: values.password }),
    onSuccess: async () => {
      await refreshCompany(queryClient)
      onClose()
      navigate('/')
    },
  })

  const handleClose = () => {
    if (open.isPending) return
    open.reset()
    form.resetFields()
    onClose()
  }

  return (
    <Modal
      open={target !== null}
      title={target?.path ? t('openCompany.title', { name: target.name }) : t('start.openFile')}
      onCancel={handleClose}
      destroyOnHidden
      footer={null}
      maskClosable={false}
    >
      <Form form={form} layout="vertical" onFinish={(values) => open.mutate(values)} requiredMark={false}>
        {target?.path === null && (
          <Form.Item
            name="path"
            label={t('start.pathLabel')}
            extra={t('start.pathHelp')}
            rules={[{ required: true, message: t('validation.path.required') }]}
          >
            <Input placeholder={t('start.pathPlaceholder')} dir="ltr" autoFocus />
          </Form.Item>
        )}
        {target?.path && (
          <Typography.Paragraph type="secondary" className="path">
            <bdi dir="ltr">{target.path}</bdi>
          </Typography.Paragraph>
        )}
        <Form.Item
          name="password"
          label={t('openCompany.password')}
          extra={t('openCompany.passwordHelp')}
          rules={[{ required: true, message: t('wizard.fieldRequired') }]}
        >
          <Input.Password autoFocus={target?.path !== null} autoComplete="off" />
        </Form.Item>

        {open.isError && <Alert type="error" showIcon message={errorMessage(open.error, t)} className="form-alert" />}
        {open.isPending && <Alert type="info" showIcon message={t('openCompany.opening')} className="form-alert" />}

        <div className="form-buttons">
          <Button type="primary" htmlType="submit" loading={open.isPending}>
            {t('openCompany.submit')}
          </Button>
          <Button onClick={handleClose} disabled={open.isPending}>
            {t('common.cancel')}
          </Button>
        </div>
      </Form>
    </Modal>
  )
}
