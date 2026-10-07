import { Button, Modal, Table } from 'antd'
import { useTranslation } from 'react-i18next'
import { shortcutList } from './useShortcuts'

/** The list of keyboard shortcuts (brief section 7.4), opened from the header menu. */
export function ShortcutsDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const { t } = useTranslation()

  return (
    <Modal
      open={open}
      title={t('shortcuts.title')}
      onCancel={onClose}
      footer={
        <Button type="primary" onClick={onClose}>
          {t('common.dismiss')}
        </Button>
      }
    >
      <p>{t('shortcuts.intro')}</p>
      <Table
        size="small"
        pagination={false}
        rowKey="action"
        dataSource={[...shortcutList]}
        columns={[
          { title: t('shortcuts.key'), dataIndex: 'keys', width: 170, render: (keys: string) => <kbd dir="ltr">{keys}</kbd> },
          { title: t('shortcuts.action'), dataIndex: 'action', render: (action: string) => t(`shortcuts.actions.${action}`) },
        ]}
      />
    </Modal>
  )
}
