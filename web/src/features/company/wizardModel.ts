import type { CountryDto, CreateCompanyRequest } from '../../api/generated/model'

/** The value of the chart choice that means "start with no accounts". */
export const blankChart = '__blank'

export const minimumPasswordLength = 6

export const stepKeys = ['company', 'taxes', 'address', 'chart', 'modules', 'file'] as const
export type StepKey = (typeof stepKeys)[number]

export interface WizardValues {
  nameAr: string
  nameEn: string
  countryCode: string
  baseCurrencyCode: string
  fiscalYearStartMonth: number
  firstFiscalYear: number
  taxNumbers: Record<string, string>
  address: string
  chartTemplateKey: string
  enabledModules: string[]
  password: string
  passwordConfirm: string
  filePath: string
}

export const initialValues = (year = new Date().getFullYear()): Partial<WizardValues> => ({
  nameAr: '',
  nameEn: '',
  fiscalYearStartMonth: 1,
  firstFiscalYear: year,
  taxNumbers: {},
  address: '',
  chartTemplateKey: blankChart,
  enabledModules: ['bank-cash', 'customers-suppliers'],
})

/** The defaults a country brings: its currency, fiscal year start, first chart and no tax numbers yet. */
export const countryDefaults = (country: CountryDto): Partial<WizardValues> => ({
  baseCurrencyCode: country.currency.code,
  fiscalYearStartMonth: country.defaultFiscalYearStartMonth,
  chartTemplateKey: country.chartsOfAccounts[0]?.key ?? blankChart,
  taxNumbers: {},
})

/** Which wizard step a server field problem (for example `taxNumbers.vat-number`) belongs to. */
export const stepOfField = (field: string): StepKey => {
  if (field.startsWith('taxNumbers')) return 'taxes'
  if (field === 'chartTemplateKey') return 'chart'
  if (field === 'modules') return 'modules'
  if (field === 'password' || field === 'path') return 'file'
  return 'company'
}

/** The form field a server field problem is shown on. */
export const formFieldOf = (field: string): string[] => {
  if (field.startsWith('taxNumbers.')) return ['taxNumbers', field.slice('taxNumbers.'.length)]
  switch (field) {
    case 'name':
      return ['nameEn']
    case 'country':
      return ['countryCode']
    case 'currency':
      return ['baseCurrencyCode']
    case 'modules':
      return ['enabledModules']
    case 'chartTemplateKey':
      return ['chartTemplateKey']
    case 'path':
      return ['filePath']
    default:
      return [field]
  }
}

export const toRequest = (values: WizardValues): CreateCompanyRequest => ({
  path: values.filePath,
  password: values.password,
  company: {
    nameAr: values.nameAr ?? '',
    nameEn: values.nameEn ?? '',
    countryCode: values.countryCode,
    baseCurrencyCode: values.baseCurrencyCode,
    fiscalYearStartMonth: values.fiscalYearStartMonth,
    firstFiscalYear: values.firstFiscalYear,
    taxNumbers: Object.fromEntries(Object.entries(values.taxNumbers ?? {}).filter(([, v]) => v?.trim())),
    address: values.address?.trim() ? values.address.trim() : null,
    chartTemplateKey: values.chartTemplateKey === blankChart ? null : values.chartTemplateKey,
    enabledModules: values.enabledModules ?? [],
  },
})

/** A safe file name for the save dialog, such as "Al Noor Trading.baba". */
export const suggestedFileName = (nameEn?: string, nameAr?: string): string => {
  const base = (nameEn?.trim() || nameAr?.trim() || 'Company').replace(/[\\/:*?"<>|]/g, '').trim()
  return `${base || 'Company'}.baba`
}
