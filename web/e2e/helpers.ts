import path from 'node:path'
import { expect, type Page } from '@playwright/test'
import { dataDir } from '../playwright.config'

export const companyFile = (name: string) => path.join(dataDir, `${name}.baba`)

export const password = 'correct-horse'

/** Picks an option of an Ant Design select by its visible label. */
export async function chooseOption(page: Page, field: string | RegExp, option: string | RegExp) {
  await page.getByLabel(field, { exact: false }).first().click()
  await page.locator('.ant-select-item-option', { hasText: option }).first().click()
}

export async function setLanguage(page: Page, language: 'en' | 'ar') {
  await page.evaluate(
    (value) => localStorage.setItem('baba.settings', JSON.stringify({ language: value, textSize: 'normal', digits: 'western' })),
    language,
  )
  await page.reload()
  await expect(page.locator('html')).toHaveAttribute('lang', language)
}

/** The page must never need sideways scrolling (brief section 7.6). */
export async function expectNoHorizontalScroll(page: Page) {
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)
  expect(overflow).toBeLessThanOrEqual(0)
}
