import type { CountryDto } from '../../api/generated/model'
import {
  blankChart,
  countryDefaults,
  formFieldOf,
  initialValues,
  stepOfField,
  suggestedFileName,
  toRequest,
  type WizardValues,
} from './wizardModel'

const values = (change: Partial<WizardValues> = {}): WizardValues => ({
  nameAr: 'شركة النور',
  nameEn: 'Al Noor',
  countryCode: 'XX',
  baseCurrencyCode: 'USD',
  fiscalYearStartMonth: 1,
  firstFiscalYear: 2026,
  taxNumbers: {},
  address: '',
  chartTemplateKey: 'default',
  enabledModules: ['sales'],
  password: 'secret-pass',
  passwordConfirm: 'secret-pass',
  filePath: 'C:\\Books\\Noor.baba',
  ...change,
})

describe('toRequest', () => {
  it('builds the API request', () => {
    expect(toRequest(values())).toEqual({
      path: 'C:\\Books\\Noor.baba',
      password: 'secret-pass',
      company: {
        nameAr: 'شركة النور',
        nameEn: 'Al Noor',
        countryCode: 'XX',
        baseCurrencyCode: 'USD',
        fiscalYearStartMonth: 1,
        firstFiscalYear: 2026,
        taxNumbers: {},
        address: null,
        chartTemplateKey: 'default',
        enabledModules: ['sales'],
      },
    })
  })

  it('sends no chart for the blank choice, and an empty address as null', () => {
    const request = toRequest(values({ chartTemplateKey: blankChart, address: '   ' }))

    expect(request.company.chartTemplateKey).toBeNull()
    expect(request.company.address).toBeNull()
  })

  it('trims the address and leaves out empty tax numbers', () => {
    const request = toRequest(values({ address: '  Block 5  ', taxNumbers: { 'vat-number': '300123456789003', cr: '  ' } }))

    expect(request.company.address).toBe('Block 5')
    expect(request.company.taxNumbers).toEqual({ 'vat-number': '300123456789003' })
  })

  it('copes with a name left empty', () => {
    expect(toRequest(values({ nameAr: undefined as unknown as string })).company.nameAr).toBe('')
  })
})

describe('server problems', () => {
  it.each([
    ['name', 'company', ['nameEn']],
    ['country', 'company', ['countryCode']],
    ['currency', 'company', ['baseCurrencyCode']],
    ['fiscalYearStartMonth', 'company', ['fiscalYearStartMonth']],
    ['firstFiscalYear', 'company', ['firstFiscalYear']],
    ['taxNumbers.vat-number', 'taxes', ['taxNumbers', 'vat-number']],
    ['chartTemplateKey', 'chart', ['chartTemplateKey']],
    ['modules', 'modules', ['enabledModules']],
    ['password', 'file', ['password']],
    ['path', 'file', ['filePath']],
  ])('%s belongs to the %s step', (field, step, formField) => {
    expect(stepOfField(field)).toBe(step)
    expect(formFieldOf(field)).toEqual(formField)
  })
})

describe('defaults', () => {
  const country = {
    code: 'XX',
    currency: { code: 'USD' },
    defaultFiscalYearStartMonth: 4,
    chartsOfAccounts: [{ key: 'default' }],
  } as unknown as CountryDto

  it('a country brings its currency, fiscal start and first chart', () => {
    expect(countryDefaults(country)).toEqual({
      baseCurrencyCode: 'USD',
      fiscalYearStartMonth: 4,
      chartTemplateKey: 'default',
      taxNumbers: {},
    })
  })

  it('a country with no chart template starts blank', () => {
    expect(countryDefaults({ ...country, chartsOfAccounts: [] }).chartTemplateKey).toBe(blankChart)
  })

  it('the wizard starts in the current year with the usual modules on', () => {
    const initial = initialValues(2031)

    expect(initial.firstFiscalYear).toBe(2031)
    expect(initial.enabledModules).toEqual(['bank-cash', 'customers-suppliers'])
  })
})

describe('suggestedFileName', () => {
  it('uses the English name, else the Arabic one, else "Company"', () => {
    expect(suggestedFileName('Al Noor', 'النور')).toBe('Al Noor.baba')
    expect(suggestedFileName('', 'شركة النور')).toBe('شركة النور.baba')
    expect(suggestedFileName(undefined, undefined)).toBe('Company.baba')
  })

  it('removes characters Windows does not allow in file names', () => {
    expect(suggestedFileName('A/B: "C"?*<>|\\D')).toBe('AB CD.baba')
    expect(suggestedFileName('///')).toBe('Company.baba')
  })
})
