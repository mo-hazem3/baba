import { QuestionCircleOutlined } from '@ant-design/icons'
import { Button, Modal } from 'antd'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'

export type HelpTopic = 'start' | 'wizard' | 'summary' | 'settings' | 'accounts' | 'vouchers' | 'voucher' | 'reports' | 'report' | 'parties' | 'costCenters' | 'transfer' | 'bank' | 'reconcile' | 'opening' | 'yearEnd' | 'rates' | 'documents' | 'products' | 'recurring'

/** The "?" help link every screen has (brief section 7.5): a short explanation in the user's language. */
export function HelpLink({ topic }: { topic: HelpTopic }) {
  const { t } = useTranslation()
  const [open, setOpen] = useState(false)

  return (
    <>
      <Button type="link" icon={<QuestionCircleOutlined />} onClick={() => setOpen(true)}>
        {t('common.help')}
      </Button>
      <Modal
        open={open}
        title={t('common.help')}
        onCancel={() => setOpen(false)}
        footer={
          <Button type="primary" onClick={() => setOpen(false)}>
            {t('common.dismiss')}
          </Button>
        }
      >
        <p>{t(`help.${topic}`)}</p>
      </Modal>
    </>
  )
}
