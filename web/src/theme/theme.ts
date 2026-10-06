import type { ThemeConfig } from 'antd'
import { baseFontSize, type Language, type TextSize } from '../settings/settings'

/**
 * The whole look of the app, set once (brief section 7.1): classic and calm, one blue accent,
 * red only for destructive actions and negative numbers, small corners, visible borders, no animation.
 * Every pair used for text meets WCAG 2.2 AA contrast (checked by a test).
 */
export const colors = {
  primary: '#12356b',
  danger: '#b00020',
  text: '#1a1a1a',
  textSecondary: '#444b57',
  surface: '#ffffff',
  page: '#f4f6f9',
  border: '#7b8597',
  tableHeader: '#eef2f8',
} as const

const fontFamily = (language: Language) =>
  language === 'ar'
    ? '"Noto Sans Arabic", "Segoe UI", Tahoma, Arial, sans-serif'
    : '"Segoe UI", "Noto Sans Arabic", Tahoma, Arial, sans-serif'

export const buildTheme = (language: Language, textSize: TextSize): ThemeConfig => {
  const fontSize = baseFontSize[language][textSize]

  return {
    token: {
      colorPrimary: colors.primary,
      colorError: colors.danger,
      colorText: colors.text,
      colorTextSecondary: colors.textSecondary,
      colorTextDescription: colors.textSecondary,
      colorTextPlaceholder: '#5b6372',
      colorBgLayout: colors.page,
      colorBgContainer: colors.surface,
      colorBorder: colors.border,
      colorLink: colors.primary,
      borderRadius: 3,
      borderRadiusLG: 4,
      borderRadiusSM: 2,
      fontSize,
      fontFamily: fontFamily(language),
      lineHeight: 1.6,
      controlHeight: Math.round(fontSize * 2.3),
      motion: false, // no animations or transitions anywhere
      boxShadow: 'none',
      boxShadowSecondary: '0 2px 8px rgba(0, 0, 0, 0.18)', // dropdowns and dialogs still need to stand out
    },
    components: {
      Layout: {
        headerBg: colors.primary,
        headerColor: '#ffffff',
        headerHeight: Math.round(fontSize * 3.4),
        headerPadding: '0 16px',
        siderBg: colors.surface,
        bodyBg: colors.page,
      },
      Menu: {
        itemSelectedBg: colors.tableHeader,
        itemSelectedColor: colors.primary,
        itemHeight: Math.round(fontSize * 2.6),
      },
      Table: {
        headerBg: colors.tableHeader,
        headerColor: colors.text,
        borderColor: colors.border,
        cellPaddingBlock: Math.round(fontSize * 0.7), // rows are never cramped
      },
      Button: {
        primaryShadow: 'none',
        defaultShadow: 'none',
        dangerShadow: 'none',
        fontWeight: 600,
      },
      Card: { headerFontSize: fontSize + 2 },
    },
  }
}
