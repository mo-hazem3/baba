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

// ---------------------------------------------------------------- Sales and purchase documents

test('English: a product, a customer, an invoice, and a credit note that takes it back', async ({ page, request }) => {
  await page.goto('/')
  await setLanguage(page, 'en')
  await createKuwaitCompany(page, companyFile('p3-sales'), wizardEn)
  await page.getByRole('button', { name: wizardEn.create }).click()
  await expect(page.getByRole('heading', { name: 'Summary' })).toBeVisible()

  await request.post('/api/parties', { data: { kind: 'Customer', code: 'C100', nameAr: '', nameEn: 'Gulf Traders', phone: null, email: null, address: null, taxNumber: null, creditLimit: 0, paymentTermsDays: 30, notes: null, priceListId: null } })

  // A product with its own price and revenue account.
  await menu(page, 'Products and prices').click()
  await expect(page.getByText('No products yet')).toBeVisible()
  await page.getByRole('button', { name: 'New product' }).click()
  await page.getByLabel('Name (English)').fill('Consulting hour')
  await page.getByLabel('Sale price').fill('50')
  await pick(page, 'Revenue account', '511')
  await page.getByRole('dialog').getByRole('button', { name: 'Save' }).click()
  await expect(page.getByRole('row', { name: /Consulting hour/ })).toContainText('50.000')
  await page.screenshot({ path: 'test-results/en-products.png' })

  // An invoice: choosing the product fills its price and the quantity is typed.
  await menu(page, 'Sales').click()
  await expect(page.getByText('No invoices yet')).toBeVisible()
  await page.getByRole('button', { name: 'New invoice' }).click()
  await pick(page, 'Customer', 'Gulf')
  await pick(page, 'Product 1', 'Consulting')
  await expect(page.getByLabel('Price 1')).toHaveValue('50.000')
  await expect(page.getByLabel('Description 1')).toHaveValue('Consulting hour')
  await page.getByLabel('Quantity 1').fill('4')
  await expect(page.locator('.doc-totals')).toContainText('200.000')
  await expectNoHorizontalScroll(page)
  await page.screenshot({ path: 'test-results/en-invoice-form.png' })
  await page.getByRole('button', { name: 'Issue and post' }).click()
  await expect(page.getByText(/Issued as SI-\d{4}-0001/)).toBeVisible()
  await expect(page.getByRole('row', { name: /SI-/ })).toContainText('200.000')

  // The books got it: the customer owes 200 and revenue is 200.
  await page.goto('/reports/trial-balance')
  await expect(page.getByRole('row', { name: /Accounts receivable/ })).toContainText('200.000')
  await expect(page.getByRole('row', { name: /^511/ })).toContainText('200.000')

  // Turn it into a credit note, issue it: everything is back to zero.
  await menu(page, 'Sales').click()
  await page.getByRole('link', { name: /SI-/ }).click()
  await expect(page.getByText('This document is posted to the books.')).toBeVisible()
  await page.getByRole('button', { name: 'Credit note' }).click()
  await expect(page.getByText(/Made a draft Credit note/)).toBeVisible()
  await page.getByRole('button', { name: 'Issue and post' }).click()
  await expect(page.getByText(/Issued as SC-\d{4}-0001/)).toBeVisible()
  await page.goto('/reports/trial-balance')
  // The sale and its reversal both show as movement (200 in, 200 out) and nothing is left at the end.
  const sold = page.getByRole('row', { name: /Product sales/ })
  await expect(sold.getByRole('cell', { name: '200.000' })).toHaveCount(2)
  await expect(page.getByRole('row', { name: /Accounts receivable/ }).getByRole('cell', { name: '200.000' })).toHaveCount(2)
})

test('Arabic: the sales page and the invoice form read right-to-left', async ({ page }) => {
  await page.goto('/')
  await setLanguage(page, 'ar')
  await createKuwaitCompany(page, companyFile('p3-sales-ar'), wizardAr)
  await page.getByRole('button', { name: wizardAr.create }).click()
  await expect(page.getByRole('heading', { name: 'الملخص' })).toBeVisible()

  await menu(page, 'المبيعات').click()
  await expect(page.getByRole('heading', { name: 'المبيعات' })).toBeVisible()
  await expect(page.getByText('لا توجد فواتير بعد')).toBeVisible()
  await expectNoHorizontalScroll(page)
  await page.screenshot({ path: 'test-results/ar-sales.png' })

  await page.getByRole('button', { name: 'فاتورة جديدة' }).click()
  await expect(page.getByRole('heading', { name: 'فاتورة جديدة' })).toBeVisible()
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl')
  await expectNoHorizontalScroll(page)
  await page.screenshot({ path: 'test-results/ar-invoice-form.png' })
})
