// WCAG 2.x contrast ratio between two #rrggbb colours.

const channel = (value: number): number => {
  const c = value / 255
  return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4
}

const luminance = (hex: string): number => {
  const n = parseInt(hex.replace('#', ''), 16)
  return 0.2126 * channel((n >> 16) & 255) + 0.7152 * channel((n >> 8) & 255) + 0.0722 * channel(n & 255)
}

export const contrastRatio = (foreground: string, background: string): number => {
  const [a, b] = [luminance(foreground), luminance(background)].sort((x, y) => y - x)
  return (a! + 0.05) / (b! + 0.05)
}
