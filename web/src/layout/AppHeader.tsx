import { DownOutlined } from '@ant-design/icons'
import { App, Button, Dropdown, Select } from 'antd'
import { useQueryClient, useMutation } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router'
import { refreshCompany } from '../api/hooks'
import { closeCompany } from '../api/generated/baba'
import type { CompanyInfo } from '../api/generated/model'
import { useSettings } from '../settings/SettingsContext'
import { textSizes } from '../settings/settings'
import { errorMessage } from './errors'
import { ShortcutsDialog } from './ShortcutsDialog'

/** The blue bar on every screen: name, language, text size and the menu. All controls have text labels (no icon-only buttons). */
export function AppHeader({ company }: { company?: CompanyInfo | null }) {
  const { t, i18n } = useTranslation()
  const { settings, setLanguage, setTextSize } = useSettings()
  const { modal, message } = App.useApp()
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const [shortcutsOpen, setShortcutsOpen] = useState(false)

  const close = useMutation({
    mutationFn: () => closeCompany(),
    onSuccess: async () => {
      await refreshCompany(queryClient)
      navigate('/')
    },
    onError: (error) => void message.error(errorMessage(error, t)),
  })

  const confirmClose = () => {
    if (!company) return
    modal.confirm({
      title: t('header.closeCompanyTitle', { name: i18n.language === 'ar' ? company.nameAr : company.nameEn }),
      content: t('header.closeCompanyBody'),
      okText: t('header.closeCompany'),
      cancelText: t('common.cancel'),
      onOk: () => close.mutateAsync(),
    })
  }

  const menuItems = company
    ? [
        { key: 'settings', label: t('nav.settings'), onClick: () => navigate('/settings') },
        { key: 'shortcuts', label: t('nav.shortcuts'), onClick: () => setShortcutsOpen(true) },
        { type: 'divider' as const },
        { key: 'close', label: t('header.closeCompany'), onClick: confirmClose },
      ]
    : []

  return (
    <div className="app-header">
      <span className="brand" lang={settings.language}>
        <img src="/logo.svg" alt="" width={32} height={32} className="brand-logo" />
        {t('app.name')}
      </span>
      {company && (
        <span className="company-name">{settings.language === 'ar' ? company.nameAr : company.nameEn}</span>
      )}

      <span className="header-spacer" />

      <Button onClick={() => setLanguage(settings.language === 'ar' ? 'en' : 'ar')} lang={settings.language === 'ar' ? 'en' : 'ar'}>
        {t('header.switchLanguage')}
      </Button>

      <Select
        value={settings.textSize}
        onChange={setTextSize}
        aria-label={t('header.textSize')}
        className="text-size-select"
        options={textSizes.map((size) => ({ value: size, label: `${t('header.textSize')}: ${t(`textSize.${size}`)}` }))}
      />

      {company && (
        <Dropdown menu={{ items: menuItems }} trigger={['click']}>
          <Button>
            {t('header.menu')} <DownOutlined />
          </Button>
        </Dropdown>
      )}
      <ShortcutsDialog open={shortcutsOpen} onClose={() => setShortcutsOpen(false)} />
    </div>
  )
}
