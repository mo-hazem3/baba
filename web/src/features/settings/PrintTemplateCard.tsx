import { App, Button, Card, Form, Input, Radio, Switch } from 'antd'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { savePrintSettings } from '../../api/generated/baba'
import type { PrintLayout, PrintSettingsInput } from '../../api/generated/model'
import { ApiError } from '../../api/http'
import { refreshBooks, usePrintSettings } from '../../api/hooks'
import { errorMessage } from '../../layout/errors'

/** The print template (brief section 10.1): what shows on voucher printouts and reports, and in which language. Saved in the company file. */
export function PrintTemplateCard() {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const settings = usePrintSettings()
  const [form] = Form.useForm<PrintSettingsInput>()

  useEffect(() => {
    if (settings.data) form.setFieldsValue(settings.data)
  }, [settings.data, form])

  const save = useMutation({
    mutationFn: (values: PrintSettingsInput) => savePrintSettings(values),
    onSuccess: async () => {
      await refreshBooks(queryClient)
      void message.success(t('printing.saved'))
    },
    onError: (error) => {
      if (error instanceof ApiError && error.issues.length > 0) {
        form.setFields(error.issues.map((i) => ({ name: i.field as keyof PrintSettingsInput, errors: [t(`printing.issues.${i.code}`, { defaultValue: i.code })] })))
      } else {
        void message.error(errorMessage(error, t))
      }
    },
  })

  const toggles: { name: keyof PrintSettingsInput; label: string }[] = [
    { name: 'showCompanyName', label: t('printing.showCompanyName') },
    { name: 'showLogo', label: t('printing.showLogo') },
    { name: 'showStamp', label: t('printing.showStamp') },
    { name: 'showSignatures', label: t('printing.showSignatures') },
    { name: 'showAmountInWords', label: t('printing.showAmountInWords') },
    { name: 'arabicIndicDigits', label: t('printing.arabicIndicDigits') },
  ]

  return (
    <Card title={t('printing.title')} className="settings-card" loading={settings.isPending}>
      <Form form={form} layout="vertical" onFinish={(values) => save.mutate(values)} className="settings-form">
        <p>{t('printing.intro')}</p>
        <div className="switch-grid">
          {toggles.map((toggle) => (
            <Form.Item key={toggle.name} name={toggle.name} valuePropName="checked" label={toggle.label}>
              <Switch />
            </Form.Item>
          ))}
        </div>
        <Form.Item name="defaultLayout" label={t('printing.defaultLayout')} extra={t('printing.defaultLayoutHelp')}>
          <Radio.Group
            options={(['Both', 'Arabic', 'English'] as PrintLayout[]).map((value) => ({ value, label: t(`export.${value.toLowerCase()}`) }))}
          />
        </Form.Item>
        <Form.Item name="headerTextEn" label={t('printing.headerEn')} extra={t('printing.headerHelp')}>
          <Input.TextArea rows={2} maxLength={500} dir="ltr" />
        </Form.Item>
        <Form.Item name="headerTextAr" label={t('printing.headerAr')}>
          <Input.TextArea rows={2} maxLength={500} dir="rtl" />
        </Form.Item>
        <Form.Item name="footerTextEn" label={t('printing.footerEn')}>
          <Input.TextArea rows={2} maxLength={500} dir="ltr" />
        </Form.Item>
        <Form.Item name="footerTextAr" label={t('printing.footerAr')}>
          <Input.TextArea rows={2} maxLength={500} dir="rtl" />
        </Form.Item>
        <Button type="primary" htmlType="submit" loading={save.isPending}>
          {t('common.save')}
        </Button>
      </Form>
    </Card>
  )
}
