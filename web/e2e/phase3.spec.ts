import { expect, test, type Page } from '@playwright/test'
import { companyFile, createKuwaitCompany, expectNoHorizontalScroll, setLanguage } from './helpers'

// The server is shared by all tests, so every test starts with no company open.
test.beforeEach(async ({ request }) => {
  await request.post('/api/company/close')
})

// The start-screen tests expect an empty recent list, so these companies are taken off it again.
test.afterEach(async ({ request }) => {
  await request.post('/api/company/close')
  const recent = (await (await request.get('/api/recent-files')).json()) as { path: string }[]
  for (const file of recent.filter((r) => r.path.includes('p3-')))
    await request.post('/api/recent-files/remove', { data: { path: file.path } })
})

const wizardEn = {
  newCompany: 'New company',
  nameEn: 'Company name (English)',
  nameAr: 'Company name (Arabic)',
  country: 'Country',
  kuwait: 'Kuwait',
  next: 'Next',
  password: 'File password',
  confirm: 'Confirm password',
  create: 'Create company',
}

const wizardAr = { ...wizardEn, newCompany: 'شركة جديدة', nameEn: 'اسم الشركة (بالإنجليزية)', nameAr: 'اسم الشركة (بالعربية)', country: 'الدولة', kuwait: 'الكويت', next: 'التالي', password: 'كلمة مرور الملف', confirm: 'تأكيد كلمة المرور', create: 'إنشاء الشركة' }

async function pick(page: Page, label: string, search: string) {
  const box = page.getByLabel(label, { exact: true })
  await box.click()
  await box.fill(search)
  await page.locator('.ant-select-dropdown:visible .ant-select-item-option', { hasText: search }).first().click()
}

const menu = (page: Page, name: string) => page.getByRole('menuitem', { name, exact: true })

test('English: exchange rates and a receipt in dollars', async ({ page }) => {
  await page.goto('/')
  await setLanguage(page, 'en')
  await createKuwaitCompany(page, companyFile('p3-rates'), wizardEn)
  await page.getByRole('button', { name: wizardEn.create }).click()
  await expect(page.getByRole('heading', { name: 'Summary' })).toBeVisible()

  // Add a rate: 1 US dollar = 0.3 dinars from today.
  await menu(page, 'Exchange rates').click()
  await expect(page.getByText('No exchange rates yet')).toBeVisible()
  await pick(page, 'Currency', 'USD')
  await page.getByLabel('Worth in KWD').fill('0.3')
  await page.getByRole('button', { name: 'Save' }).click()
  await expect(page.getByText('The rate was saved.')).toBeVisible()
  await expect(page.getByRole('row', { name: /USD/ })).toContainText('0.3')
  await page.screenshot({ path: 'test-results/en-rates.png' })

  // A receipt of 100 dollars: the rate is filled in from the table, the dinar total follows.
  await menu(page, 'Receipts').click()
  await page.getByRole('button', { name: 'New receipt' }).click()
  await pick(page, 'Received into', '112')
  await pick(page, 'Account 1', '511')
  await page.getByLabel('Amount 1').fill('100')
  await pick(page, 'Currency', 'USD')
  await expect(page.getByLabel(/Rate: 1 USD/)).toHaveValue('0.300000')
  await expectNoHorizontalScroll(page)
  await page.screenshot({ path: 'test-results/en-receipt-usd.png' })
  await page.getByRole('button', { name: 'Save and post' }).click()
  await expect(page.getByText(/Posted as RV-\d{4}-0001/)).toBeVisible()

  // The list says what the voucher was written in; the books are in dinars.
  await expect(page.getByRole('row', { name: /RV-/ })).toContainText('100.00')
  await expect(page.getByRole('row', { name: /RV-/ })).toContainText('USD')
  await page.goto('/reports/trial-balance')
  await expect(page.getByRole('row', { name: /511/ })).toContainText('30.000')
  await expect(page.locator('.check-ok').first()).toBeVisible()
})

test('Arabic: the exchange rates page reads right-to-left', async ({ page }) => {
  await page.goto('/')
  await setLanguage(page, 'ar')
  await createKuwaitCompany(page, companyFile('p3-rates-ar'), wizardAr)
  await page.getByRole('button', { name: wizardAr.create }).click()
  await expect(page.getByRole('heading', { name: 'الملخص' })).toBeVisible()

  await menu(page, 'أسعار الصرف').click()
  await expect(page.getByRole('heading', { name: 'أسعار الصرف' })).toBeVisible()
  await expect(page.getByText('لا توجد أسعار صرف بعد')).toBeVisible()
  await expectNoHorizontalScroll(page)
  await page.screenshot({ path: 'test-results/ar-rates.png' })
})
