import { applyDigitStyle, formatAmount, formatDate, monthName } from './format'

describe('formatAmount', () => {
  it('uses the currency minor units: 2 for most currencies, 3 for dinars', () => {
    expect(formatAmount(1234.5, 2, 'western')).toBe('1,234.50')
    expect(formatAmount(1234.5, 3, 'western')).toBe('1,234.500')
    expect(formatAmount(0, 3, 'western')).toBe('0.000')
  })

  it('rounds to the minor units', () => {
    expect(formatAmount(1.2346, 3, 'western')).toBe('1.235')
    expect(formatAmount(10.005, 2, 'western')).toBe('10.01')
  })

  it('puts thousands separators in large numbers', () => {
    expect(formatAmount(1234567.891, 3, 'western')).toBe('1,234,567.891')
  })

  it('shows negatives with a minus sign, so colour is never the only signal', () => {
    expect(formatAmount(-1250, 3, 'western')).toBe('-1,250.000')
  })

  it('can use Arabic-Indic digits with the Arabic decimal and thousands marks', () => {
    expect(formatAmount(1234.5, 3, 'arabic-indic')).toBe('١٬٢٣٤٫٥٠٠')
    expect(formatAmount(-5, 2, 'arabic-indic')).toBe('-٥٫٠٠')
  })
})

describe('applyDigitStyle', () => {
  it('leaves text alone for Western digits', () => {
    expect(applyDigitStyle('INV-2026-0001', 'western')).toBe('INV-2026-0001')
  })

  it('converts every digit for Arabic-Indic', () => {
    expect(applyDigitStyle('0123456789', 'arabic-indic')).toBe('٠١٢٣٤٥٦٧٨٩')
  })
})

describe('formatDate', () => {
  it('is day first with two digits', () => {
    expect(formatDate(new Date(2026, 9, 6), 'western')).toBe('06/10/2026')
    expect(formatDate(new Date(2026, 0, 31), 'western')).toBe('31/01/2026')
  })

  it('follows the digit style', () => {
    expect(formatDate(new Date(2026, 9, 6), 'arabic-indic')).toBe('٠٦/١٠/٢٠٢٦')
  })

  it('accepts an ISO string', () => {
    expect(formatDate('2026-10-06T12:00:00Z', 'western')).toMatch(/^\d{2}\/\d{2}\/2026$/)
  })
})

describe('monthName', () => {
  it('names the month in the interface language', () => {
    expect(monthName(1, 'en')).toBe('January')
    expect(monthName(12, 'en')).toBe('December')
    expect(monthName(1, 'ar')).toBe('يناير')
  })
})
