import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import i18n from '../i18n'
import {
  loadSettings,
  nextTextSize,
  saveSettings,
  type DigitStyle,
  type Language,
  type Settings,
  type TextSize,
} from './settings'

interface SettingsContextValue {
  settings: Settings
  setLanguage: (language: Language) => void
  setTextSize: (size: TextSize) => void
  setDigits: (digits: DigitStyle) => void
}

const SettingsContext = createContext<SettingsContextValue | null>(null)

export function SettingsProvider({ children }: { children: ReactNode }) {
  const [settings, setSettings] = useState<Settings>(loadSettings)

  // Language and direction apply to the whole page: the layout mirrors through CSS logical properties.
  useEffect(() => {
    const root = document.documentElement
    root.lang = settings.language
    root.dir = settings.language === 'ar' ? 'rtl' : 'ltr'
    root.dataset.textSize = settings.textSize
    void i18n.changeLanguage(settings.language)
    saveSettings(settings)
  }, [settings])

  const setTextSize = useCallback((textSize: TextSize) => setSettings((s) => ({ ...s, textSize })), [])

  // Ctrl + and Ctrl - change the text size, Ctrl 0 resets it (brief section 7.2).
  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (!event.ctrlKey || event.altKey) return
      if (event.key === '+' || event.key === '=') {
        event.preventDefault()
        setSettings((s) => ({ ...s, textSize: nextTextSize(s.textSize, 1) }))
      } else if (event.key === '-') {
        event.preventDefault()
        setSettings((s) => ({ ...s, textSize: nextTextSize(s.textSize, -1) }))
      } else if (event.key === '0') {
        event.preventDefault()
        setTextSize('normal')
      }
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [setTextSize])

  const value = useMemo<SettingsContextValue>(
    () => ({
      settings,
      setLanguage: (language) => setSettings((s) => ({ ...s, language })),
      setTextSize,
      setDigits: (digits) => setSettings((s) => ({ ...s, digits })),
    }),
    [settings, setTextSize],
  )

  return <SettingsContext.Provider value={value}>{children}</SettingsContext.Provider>
}

export function useSettings(): SettingsContextValue {
  const context = useContext(SettingsContext)
  if (!context) throw new Error('useSettings must be used inside SettingsProvider')
  return context
}
