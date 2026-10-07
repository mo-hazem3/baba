export type Language = 'en' | 'ar'
export type TextSize = 'normal' | 'large' | 'xlarge'
export type DigitStyle = 'western' | 'arabic-indic'

export interface Settings {
  language: Language
  textSize: TextSize
  digits: DigitStyle
  /** Show the Hijri (Umm al-Qura) date next to Gregorian dates. Display only, off by default (brief section 6). */
  hijri: boolean
}

export const textSizes: readonly TextSize[] = ['normal', 'large', 'xlarge']

const storageKey = 'baba.settings'

/** Arabic reads slightly larger than Latin at the same size, so it gets one extra pixel (brief section 7.2). */
export const baseFontSize: Record<Language, Record<TextSize, number>> = {
  en: { normal: 15, large: 17, xlarge: 20 },
  ar: { normal: 16, large: 18, xlarge: 21 },
}

export const defaultSettings = (preferredLanguage: string = navigator.language): Settings => ({
  language: preferredLanguage.toLowerCase().startsWith('ar') ? 'ar' : 'en',
  textSize: 'normal',
  digits: 'western',
  hijri: false,
})

/** Browser storage can be missing or blocked, so every access is guarded and the app works without it. */
export const loadSettings = (): Settings => {
  const defaults = defaultSettings()
  try {
    const stored: unknown = JSON.parse(localStorage.getItem(storageKey) ?? 'null')
    if (typeof stored !== 'object' || stored === null) return defaults
    const s = stored as Partial<Settings>
    return {
      language: s.language === 'ar' || s.language === 'en' ? s.language : defaults.language,
      textSize: s.textSize && textSizes.includes(s.textSize) ? s.textSize : defaults.textSize,
      digits: s.digits === 'arabic-indic' || s.digits === 'western' ? s.digits : defaults.digits,
      hijri: s.hijri === true,
    }
  } catch {
    return defaults
  }
}

export const saveSettings = (settings: Settings): void => {
  try {
    localStorage.setItem(storageKey, JSON.stringify(settings))
  } catch {
    // Not remembered between runs; that is fine.
  }
}

export const nextTextSize = (current: TextSize, direction: 1 | -1): TextSize => {
  const index = textSizes.indexOf(current) + direction
  return textSizes[Math.min(Math.max(index, 0), textSizes.length - 1)] ?? current
}
