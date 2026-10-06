import ar from './locales/ar.json'
import en from './locales/en.json'

type Tree = { [key: string]: string | Tree }

const flatten = (tree: Tree, prefix = ''): Record<string, string> =>
  Object.entries(tree).reduce<Record<string, string>>((all, [key, value]) => {
    const path = prefix ? `${prefix}.${key}` : key
    return typeof value === 'string' ? { ...all, [path]: value } : { ...all, ...flatten(value, path) }
  }, {})

const english = flatten(en as Tree)
const arabic = flatten(ar as Tree)
const placeholders = (text: string) => [...text.matchAll(/{{\s*(\w+)\s*}}/g)].map((m) => m[1]).sort()

describe('translations', () => {
  it('have exactly the same keys in English and Arabic', () => {
    expect(Object.keys(arabic).sort()).toEqual(Object.keys(english).sort())
  })

  it('have no empty texts', () => {
    for (const [key, text] of [...Object.entries(english), ...Object.entries(arabic)]) {
      expect(text.trim(), key).not.toBe('')
    }
  })

  it('use the same placeholders in both languages', () => {
    for (const key of Object.keys(english)) {
      expect(placeholders(arabic[key] ?? ''), key).toEqual(placeholders(english[key] ?? ''))
    }
  })

  it('write Arabic texts in Arabic script', () => {
    // Allowed to stay Latin: product names, the file name example and the language switch label.
    const allowed = new Set(['app.arabicName', 'header.switchLanguage', 'start.pathPlaceholder', 'settings.languageEnglish'])
    for (const [key, text] of Object.entries(arabic)) {
      if (!allowed.has(key)) expect(text, key).toMatch(/[؀-ۿ]/)
    }
  })

  it('explain every error the API can return', () => {
    const problems = [
      'WrongPasswordOrNotABabaFile', 'NotABabaFile', 'OpenElsewhere', 'CreatedByNewerVersion', 'FileNotFound',
      'FileAlreadyExists', 'NoCompanyOpen', 'CompanyAlreadyOpen', 'InvalidRequest', 'Validation', 'BadRequest',
      'Network', 'Unexpected',
    ]
    for (const problem of problems) expect(english[`errors.${problem}`], problem).toBeTruthy()
  })

  it('explain every field problem the API can return', () => {
    const codes = [
      'path.required', 'path.not-absolute', 'password.too-short', 'name.required', 'country.unknown', 'currency.unknown',
      'fiscal-year.month-invalid', 'fiscal-year.year-invalid', 'module.unknown', 'chart.unknown',
      'tax.required', 'tax.invalid', 'tax.unknown',
    ]
    for (const code of codes) expect(english[`validation.${code}`], code).toBeTruthy()
  })

  it('name and describe every module', () => {
    const keys = [
      'bank-cash', 'customers-suppliers', 'cost-centers', 'sales', 'purchases',
      'inventory', 'fixed-assets', 'payroll', 'expense-claims', 'budgets',
    ]
    for (const key of keys) {
      expect(english[`modules.${key}.name`], key).toBeTruthy()
      expect(english[`modules.${key}.description`], key).toBeTruthy()
    }
  })
})
