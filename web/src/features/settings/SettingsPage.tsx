import { App, Button, Card, Form, Radio, Typography } from 'antd'
import { useMutation } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { printTestPage } from '../../api/generated/baba'
import type { PrintLayout } from '../../api/generated/model'
import { asBlob } from '../../api/http'
import { errorMessage } from '../../layout/errors'
import { PageHeader } from '../../layout/PageHeader'
import { useSettings } from '../../settings/SettingsContext'
import { textSizes } from '../../settings/settings'
import { formatAmount, formatDate } from '../../utils/format'

/** Language, text size and number style (brief sections 6 and 7.2). Saved on this computer. */
export function SettingsPage() {
  const { t } = useTranslation()
  const { settings, setLanguage, setTextSize, setDigits } = useSettings()
  const { message } = App.useApp()

  // Makes a sample invoice PDF and opens it in the viewer, where it can be checked, printed or saved.
  const print = useMutation({
    mutationFn: (layout: PrintLayout) => printTestPage({ layout }),
    onSuccess: (response) => {
      if (response.status !== 200) {
        void message.info(t('settings.printing.unavailable'))
        return
      }
      const url = URL.createObjectURL(asBlob(response.data))
      window.open(url, '_blank')
      setTimeout(() => URL.revokeObjectURL(url), 5 * 60_000)
    },
    onError: (error) => void message.error(errorMessage(error, t)),
  })

  return (
    <div>
      <PageHeader
        title={t('settings.title')}
        help="settings"
        crumbs={[{ label: t('breadcrumb.home'), to: '/' }, { label: t('settings.title') }]}
      />
      <Card>
        <Form layout="vertical" className="settings-form">
          <Form.Item label={t('settings.language')}>
            <Radio.Group
              value={settings.language}
              onChange={(e) => setLanguage(e.target.value as 'en' | 'ar')}
              options={[
                { value: 'en', label: <span lang="en">{t('settings.languageEnglish')}</span> },
                { value: 'ar', label: <span lang="ar">{t('settings.languageArabic')}</span> },
              ]}
            />
          </Form.Item>
          <Form.Item label={t('settings.textSize')} extra={t('settings.textSizeHelp')}>
            <Radio.Group
              value={settings.textSize}
              onChange={(e) => setTextSize(e.target.value as (typeof textSizes)[number])}
              options={textSizes.map((size) => ({ value: size, label: t(`textSize.${size}`) }))}
            />
          </Form.Item>
          <Form.Item label={t('settings.digits')}>
            <Radio.Group
              value={settings.digits}
              onChange={(e) => setDigits(e.target.value as 'western' | 'arabic-indic')}
              options={[
                { value: 'western', label: t('digits.western') },
                { value: 'arabic-indic', label: t('digits.arabicIndic') },
              ]}
            />
          </Form.Item>
          <Form.Item label={t('settings.preview')}>
            <p className="numbers preview">
              {formatAmount(1234567.891, 3, settings.digits)} &nbsp;|&nbsp;
              <span className="negative"> {formatAmount(-1250, 3, settings.digits)}</span> &nbsp;|&nbsp;
              {formatDate(new Date(2026, 9, 6), settings.digits)}
            </p>
          </Form.Item>
        </Form>
      </Card>

      <Card title={t('settings.printing.title')} className="settings-card">
        <Typography.Paragraph>{t('settings.printing.body')}</Typography.Paragraph>
        <div className="form-buttons">
          {(['Arabic', 'English', 'Both'] as const).map((layout) => (
            <Button
              key={layout}
              onClick={() => print.mutate(layout)}
              loading={print.isPending && print.variables === layout}
              disabled={print.isPending}
            >
              {t(`settings.printing.${layout}`)}
            </Button>
          ))}
        </div>
      </Card>
    </div>
  )
}
