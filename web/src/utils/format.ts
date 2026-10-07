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

const pad = (n: number) => String(n).padStart(2, '0')

/** A date as the API sends it (2026-10-06) read as that calendar day on this computer, never shifted by a time zone. */
export const parseIsoDate = (value: string): Date => {
  const [year = 0, month = 1, day = 1] = value.slice(0, 10).split('-').map(Number)
  return new Date(year, month - 1, day)
}

/** A date as the API wants it (2026-10-06). */
export const toIsoDate = (date: Date): string => `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`

const toDate = (value: Date | string): Date => (typeof value === 'string' ? parseIsoDate(value) : value)

/**
 * The Hijri (Umm al-Qura, the civil Hijri calendar used in the Gulf) date for a Gregorian one, as 22/04/1448. Display only: Baba never stores
 * or asks for Hijri dates (brief section 6).
 */
export const formatHijri = (value: Date | string, digits: DigitStyle): string => {
  const parts = new Intl.DateTimeFormat('en-US-u-ca-islamic-umalqura-nu-latn', {
    day: 'numeric',
    month: 'numeric',
    year: 'numeric',
  }).formatToParts(toDate(value))
  const part = (type: string) => Number(parts.find((p) => p.type === type)?.value ?? 0)
  return applyDigitStyle(`${pad(part('day'))}/${pad(part('month'))}/${part('year')}`, digits)
}

/** A short date such as 06/10/2026, always day first, in the user's digit style; with the Hijri date after it when that is switched on. */
export const formatDate = (value: Date | string, digits: DigitStyle, hijri = false): string => {
  const date = toDate(value)
  const gregorian = applyDigitStyle(`${pad(date.getDate())}/${pad(date.getMonth() + 1)}/${date.getFullYear()}`, digits)
  return hijri ? `${gregorian} (${formatHijri(date, digits)} هـ)` : gregorian
}

/** Month names in the interface language (1 = January). */
export const monthName = (month: number, language: string): string =>
  new Intl.DateTimeFormat(language, { month: 'long' }).format(new Date(2026, month - 1, 1))
