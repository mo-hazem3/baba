import { cellText, drillPath, isReportKey, matchPreset, presetRange, reportInputs, reportKeys, voucherPath } from './reportModel'

const today = new Date(2026, 9, 6) // 6 October 2026

describe('presetRange', () => {
  it('gives this month and last month', () => {
    expect(presetRange('thisMonth', today, 1)).toEqual({ from: '2026-10-01', to: '2026-10-31' })
    expect(presetRange('lastMonth', today, 1)).toEqual({ from: '2026-09-01', to: '2026-09-30' })
    expect(presetRange('lastMonth', new Date(2026, 0, 15), 1)).toEqual({ from: '2025-12-01', to: '2025-12-31' }) // across the new year
    expect(presetRange('thisMonth', new Date(2028, 1, 10), 1)).toEqual({ from: '2028-02-01', to: '2028-02-29' }) // a leap year
  })

  it('gives the fiscal year that contains today, whatever month it starts in', () => {
    expect(presetRange('thisYear', today, 1)).toEqual({ from: '2026-01-01', to: '2026-12-31' })
    expect(presetRange('lastYear', today, 1)).toEqual({ from: '2025-01-01', to: '2025-12-31' })
    // A fiscal year starting in April: October 2026 is in the year that began in April 2026.
    expect(presetRange('thisYear', today, 4)).toEqual({ from: '2026-04-01', to: '2027-03-31' })
    // ... and February 2026 is still in the year that began in April 2025.
    expect(presetRange('thisYear', new Date(2026, 1, 10), 4)).toEqual({ from: '2025-04-01', to: '2026-03-31' })
    expect(presetRange('lastYear', today, 4)).toEqual({ from: '2025-04-01', to: '2026-03-31' })
  })

  it('"all" has no dates, so a report shows everything', () => expect(presetRange('all', today, 1)).toEqual({}))
})

describe('matchPreset', () => {
  it('recognises a range that equals a preset', () => {
    expect(matchPreset({ from: '2026-10-01', to: '2026-10-31' }, today, 1)).toBe('thisMonth')
    expect(matchPreset({ from: '2026-01-01', to: '2026-12-31' }, today, 1)).toBe('thisYear')
    expect(matchPreset({}, today, 1)).toBe('all')
    expect(matchPreset({ from: '2026-10-03', to: '2026-10-31' }, today, 1)).toBeUndefined()
  })
})

describe('cellText', () => {
  it('uses the Arabic variant in Arabic, English otherwise, and falls back to whichever exists', () => {
    const cell = { text: 'Rent', textAr: 'الإيجار' }

    expect(cellText(cell, 'en')).toBe('Rent')
    expect(cellText(cell, 'ar')).toBe('الإيجار')
    expect(cellText({ text: '111' }, 'ar')).toBe('111')
    expect(cellText({ textAr: 'فقط' }, 'en')).toBe('فقط')
    expect(cellText(undefined, 'en')).toBe('')
  })
})

describe('drill-down', () => {
  it('goes from an account row to its statement for the same dates', () => {
    expect(drillPath({ kind: 'account', id: 'abc', from: '2026-10-01', to: '2026-10-31' }))
      .toBe('/reports/statement-of-account?accountId=abc&from=2026-10-01&to=2026-10-31')
    expect(drillPath({ kind: 'account', id: 'abc' })).toBe('/reports/statement-of-account?accountId=abc')
  })

  it('goes from a voucher row to the voucher', () => {
    expect(drillPath({ kind: 'voucher', id: 'v1' })).toBe('/vouchers/open/v1')
    expect(voucherPath('Payment', 'v1')).toBe('/vouchers/payment/v1')
    expect(voucherPath('Journal', 'v2')).toBe('/vouchers/journal/v2')
  })
})

describe('the reports', () => {
  it('each say which inputs they need', () => {
    expect(reportKeys).toHaveLength(6)
    expect(reportInputs['trial-balance']).toEqual({ range: true, asOf: false, account: false, comparison: false })
    expect(reportInputs['balance-sheet'].asOf).toBe(true) // a balance sheet is "as of" a date, not for a range
    expect(reportInputs['profit-and-loss'].comparison).toBe(true)
    expect(reportInputs['statement-of-account'].account).toBe(true)
  })

  it('are recognised by their address', () => {
    expect(isReportKey('journal')).toBe(true)
    expect(isReportKey('payroll')).toBe(false)
    expect(isReportKey(undefined)).toBe(false)
  })
})
