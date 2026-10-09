import { App as AntApp, ConfigProvider } from 'antd'
import arEG from 'antd/locale/ar_EG'
import enUS from 'antd/locale/en_US'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useEffect, useState, type ReactNode } from 'react'
import { getGetSessionQueryKey } from '../api/generated/baba'
import { sessionChangedEvent } from '../api/http'
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

  useEffect(() => {
    const reread = () => void queryClient.invalidateQueries({ queryKey: getGetSessionQueryKey() })
    window.addEventListener(sessionChangedEvent, reread)
    return () => window.removeEventListener(sessionChangedEvent, reread)
  }, [queryClient])

  return (
    <QueryClientProvider client={queryClient}>
      <SettingsProvider>
        <ThemedApp>{children}</ThemedApp>
      </SettingsProvider>
    </QueryClientProvider>
  )
}
