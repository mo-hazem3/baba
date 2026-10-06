import { contrastRatio } from './contrast'
import { buildTheme, colors } from './theme'

describe('contrast', () => {
  it('computes the WCAG ratio', () => {
    expect(contrastRatio('#000000', '#ffffff')).toBeCloseTo(21, 0)
    expect(contrastRatio('#ffffff', '#ffffff')).toBeCloseTo(1, 5)
  })
})

describe('colours meet WCAG 2.2 AA', () => {
  // Normal text needs 4.5:1. Borders and other parts of controls need 3:1.
  it.each([
    ['text on white', colors.text, colors.surface],
    ['text on the page background', colors.text, colors.page],
    ['secondary text on white', colors.textSecondary, colors.surface],
    ['secondary text on the page background', colors.textSecondary, colors.page],
    ['text on table headers', colors.text, colors.tableHeader],
    ['blue links and primary text on white', colors.primary, colors.surface],
    ['blue text on the page background', colors.primary, colors.page],
    ['white on the blue buttons and header', '#ffffff', colors.primary],
    ['red negative numbers on white', colors.danger, colors.surface],
    ['red negative numbers on the page background', colors.danger, colors.page],
    ['white on red destructive buttons', '#ffffff', colors.danger],
  ])('%s', (_name, foreground, background) => {
    expect(contrastRatio(foreground, background)).toBeGreaterThanOrEqual(4.5)
  })

  it('input and table borders are clearly visible against white', () => {
    expect(contrastRatio(colors.border, colors.surface)).toBeGreaterThanOrEqual(3)
  })
})

describe('buildTheme', () => {
  it('turns every animation off', () => {
    expect(buildTheme('en', 'normal').token?.motion).toBe(false)
  })

  it('uses small corners and one accent colour', () => {
    const token = buildTheme('en', 'normal').token
    expect(token?.borderRadius).toBeLessThanOrEqual(4)
    expect(token?.colorPrimary).toBe(colors.primary)
  })

  it('makes text and controls bigger with the text size setting', () => {
    const normal = buildTheme('en', 'normal').token
    const xlarge = buildTheme('en', 'xlarge').token
    expect(xlarge?.fontSize).toBeGreaterThan(normal?.fontSize ?? 0)
    expect(xlarge?.controlHeight).toBeGreaterThan(normal?.controlHeight ?? 0)
  })

  it('puts the Arabic font first for Arabic', () => {
    expect(String(buildTheme('ar', 'normal').token?.fontFamily)).toMatch(/^"Noto Sans Arabic"/)
    expect(String(buildTheme('en', 'normal').token?.fontFamily)).toMatch(/^"Segoe UI"/)
  })
})
