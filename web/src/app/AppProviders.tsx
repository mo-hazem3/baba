import { App as AntApp, ConfigProvider } from 'antd'
import arEG from 'antd/locale/ar_EG'
import enUS from 'antd/locale/en_US'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useState, type ReactNode } from 'react'
import { SettingsProvider, useSettings } from '../settings/SettingsContext'
import { buildTheme } from '../theme/theme'

function ThemedApp({ children }: { children: ReactNode }) {
  const { settings } = useSettings()
  const rtl = settings.language === 'ar'

  return (
    <ConfigProvider
      direction={rtl ? 'rtl' : 'ltr'}
      locale={rtl ? arEG : enUS}
      theme={buildTheme(settings.language, settings.textSize)}
    >
      <AntApp>{children}</AntApp>
    </ConfigProvider>
  )
}

export function AppProviders({ children }: { children: ReactNode }) {
  const [queryClient] = useState(
    () => new QueryClient({ defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false } } }),
  )

  return (
    <QueryClientProvider client={queryClient}>
      <SettingsProvider>
        <ThemedApp>{children}</ThemedApp>
      </SettingsProvider>
    </QueryClientProvider>
  )
}
