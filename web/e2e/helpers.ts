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

/** Walks the new-company wizard to the end for Kuwait (no tax numbers needed). */
export async function createKuwaitCompany(page: Page, file: string, labels: Record<string, string>) {
  await page.getByRole('button', { name: labels.newCompany! }).click()
  await page.getByLabel(labels.nameEn!).fill('Al Noor Trading')
  await page.getByLabel(labels.nameAr!).fill('شركة النور للتجارة')
  await chooseOption(page, labels.country!, labels.kuwait!)
  await expect(page.getByTitle('KWD', { exact: false }).first()).toBeVisible() // currency defaults to the dinar
  await page.getByRole('button', { name: labels.next! }).click()
  await page.getByRole('button', { name: labels.next! }).click() // taxes: none for this country
  await page.getByRole('button', { name: labels.next! }).click() // address
  await page.getByRole('button', { name: labels.next! }).click() // chart
  await page.getByRole('button', { name: labels.next! }).click() // modules
  await page.getByLabel(labels.password!, { exact: true }).fill(password)
  await page.getByLabel(labels.confirm!).fill(password)
  await page.getByPlaceholder('C:\\Books\\My Company.baba').fill(file)
}
