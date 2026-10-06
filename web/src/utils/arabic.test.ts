import { matchesSearch, normalizeArabic } from './arabic'

describe('normalizeArabic', () => {
  it('ignores diacritics and tatweel', () => {
    expect(normalizeArabic('مُحَمَّد')).toBe('محمد')
    expect(normalizeArabic('كتــاب')).toBe('كتاب')
  })

  it('treats the alef variants as one letter', () => {
    expect(normalizeArabic('أحمد')).toBe('احمد')
    expect(normalizeArabic('إبراهيم')).toBe('ابراهيم')
    expect(normalizeArabic('آمنة')).toBe('امنه')
  })

  it('treats teh marbuta / heh and alef maksura / yeh as the same', () => {
    expect(normalizeArabic('مدرسة')).toBe(normalizeArabic('مدرسه'))
    expect(normalizeArabic('على')).toBe(normalizeArabic('علي'))
  })

  it('turns Arabic-Indic and Persian digits into Western digits', () => {
    expect(normalizeArabic('١٢٣')).toBe('123')
    expect(normalizeArabic('۴۵۶')).toBe('456')
  })

  it('lowercases Latin text', () => {
    expect(normalizeArabic('Al Noor')).toBe('al noor')
  })
})

describe('matchesSearch', () => {
  it('finds a name typed without diacritics or with another alef', () => {
    expect(matchesSearch('احمد', 'أحمد الصباح')).toBe(true)
    expect(matchesSearch('مدرسه', 'المدرسة الأهلية')).toBe(true)
  })

  it('needs every word to match somewhere', () => {
    expect(matchesSearch('احمد صباح', 'أحمد الصباح')).toBe(true)
    expect(matchesSearch('احمد خالد', 'أحمد الصباح')).toBe(false)
  })

  it('matches either name and the code', () => {
    expect(matchesSearch('noor', 'النور', 'Al Noor', '1010')).toBe(true)
    expect(matchesSearch('النور', 'النور', 'Al Noor', '1010')).toBe(true)
    expect(matchesSearch('1010', 'النور', 'Al Noor', '1010')).toBe(true)
    expect(matchesSearch('١٠١٠', 'النور', 'Al Noor', '1010')).toBe(true)
  })

  it('matches everything when nothing is typed', () => {
    expect(matchesSearch('   ', 'anything')).toBe(true)
  })
})
