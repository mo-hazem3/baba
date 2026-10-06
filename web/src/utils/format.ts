import type { DigitStyle } from '../settings/settings'

const arabicIndicDigits = ['٠', '١', '٢', '٣', '٤', '٥', '٦', '٧', '٨', '٩']

/** Replaces 0-9 with Arabic-Indic digits (and the decimal and thousands marks) when the user prefers them. */
export const applyDigitStyle = (text: string, digits: DigitStyle): string =>
  digits === 'arabic-indic'
    ? text.replace(/\d/g, (d) => arabicIndicDigits[Number(d)] ?? d).replace(/\./g, '٫').replace(/,/g, '٬')
    : text

/**
 * Formats an amount with thousands separators and the currency's number of decimals (2 for most currencies, 3 for dinars).
 * Negative amounts get a minus sign as well as red styling in the UI, so colour is never the only signal.
 */
export const formatAmount = (value: number, minorUnits: number, digits: DigitStyle): string => {
  const formatted = new Intl.NumberFormat('en-US', {
    minimumFractionDigits: minorUnits,
    maximumFractionDigits: minorUnits,
  }).format(Math.abs(value))
  return applyDigitStyle(`${value < 0 ? '-' : ''}${formatted}`, digits)
}

/** A short date such as 06/10/2026, always day first, in the user's digit style. */
export const formatDate = (value: Date | string, digits: DigitStyle): string => {
  const date = typeof value === 'string' ? new Date(value) : value
  const pad = (n: number) => String(n).padStart(2, '0')
  return applyDigitStyle(`${pad(date.getDate())}/${pad(date.getMonth() + 1)}/${date.getFullYear()}`, digits)
}

/** Month names in the interface language (1 = January). */
export const monthName = (month: number, language: string): string =>
  new Intl.DateTimeFormat(language, { month: 'long' }).format(new Date(2026, month - 1, 1))
