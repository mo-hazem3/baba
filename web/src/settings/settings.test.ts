import { baseFontSize, defaultSettings, loadSettings, nextTextSize, saveSettings } from './settings'

afterEach(() => localStorage.clear())

describe('defaultSettings', () => {
  it('picks Arabic for an Arabic browser and English otherwise', () => {
    expect(defaultSettings('ar-KW').language).toBe('ar')
    expect(defaultSettings('AR').language).toBe('ar')
    expect(defaultSettings('en-US').language).toBe('en')
    expect(defaultSettings('fr-FR').language).toBe('en')
  })

  it('starts with normal text and Western digits', () => {
    expect(defaultSettings('en')).toMatchObject({ textSize: 'normal', digits: 'western', hijri: false })
  })
})

describe('saved settings', () => {
  it('are remembered', () => {
    saveSettings({ language: 'ar', textSize: 'xlarge', digits: 'arabic-indic', hijri: true })

    expect(loadSettings()).toEqual({ language: 'ar', textSize: 'xlarge', digits: 'arabic-indic', hijri: true })
  })

  it('fall back to defaults when nothing or garbage is stored', () => {
    expect(loadSettings().textSize).toBe('normal')

    localStorage.setItem('baba.settings', 'not json')
    expect(loadSettings().textSize).toBe('normal')

    localStorage.setItem('baba.settings', JSON.stringify({ language: 'xx', textSize: 'huge', digits: 5 }))
    expect(loadSettings()).toMatchObject({ textSize: 'normal', digits: 'western' })
  })

  it('still work when browser storage is blocked', () => {
    const blocked = vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked')
    })
    const failing = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked')
    })

    expect(loadSettings().textSize).toBe('normal')
    expect(() => saveSettings(defaultSettings('en'))).not.toThrow()

    blocked.mockRestore()
    failing.mockRestore()
  })
})

describe('nextTextSize', () => {
  it('steps up and down but never past the ends', () => {
    expect(nextTextSize('normal', 1)).toBe('large')
    expect(nextTextSize('large', 1)).toBe('xlarge')
    expect(nextTextSize('xlarge', 1)).toBe('xlarge')
    expect(nextTextSize('xlarge', -1)).toBe('large')
    expect(nextTextSize('normal', -1)).toBe('normal')
  })
})

describe('font sizes', () => {
  it('follow the brief: 15-16px English, slightly larger Arabic, and grow with the setting', () => {
    expect(baseFontSize.en.normal).toBeGreaterThanOrEqual(15)
    expect(baseFontSize.ar.normal).toBeGreaterThan(baseFontSize.en.normal)
    for (const language of ['en', 'ar'] as const) {
      const { normal, large, xlarge } = baseFontSize[language]
      expect(normal).toBeLessThan(large)
      expect(large).toBeLessThan(xlarge)
    }
  })
})
