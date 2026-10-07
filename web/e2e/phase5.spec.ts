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
  for (const file of recent.filter((r) => r.path.includes('p5-')))
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

const modules = ['bank-cash', 'customers-suppliers', 'sales', 'purchases', 'inventory']

async function pick(page: Page, label: string, search: string) {
  const box = page.getByLabel(label, { exact: true })
  await box.click()
  await box.fill(search)
  await page.locator('.ant-select-dropdown:visible .ant-select-item-option', { hasText: search }).first().click()
}

const menu = (page: Page, name: string) => page.getByRole('menuitem', { name, exact: true })

test('English: stock in, a sale at cost, the low-stock warning, a count, and the valuation that agrees with the books', async ({ page, request }) => {
  await page.goto('/')
  await setLanguage(page, 'en')
  await createKuwaitCompany(page, companyFile('p5-stock'), wizardEn)
  await page.getByRole('button', { name: wizardEn.create }).click()
  await expect(page.getByRole('heading', { name: 'Summary' })).toBeVisible()

  await request.post('/api/company/modules', { data: { modules } })
  await request.post('/api/parties', { data: { kind: 'Customer', code: 'C100', nameAr: '', nameEn: 'Gulf Traders', phone: null, email: null, address: null, taxNumber: null, creditLimit: 0, paymentTermsDays: 30, notes: null, priceListId: null } })
  await page.reload()

  // The inventory module adds Stock and Warehouses; a main warehouse already exists.
  await menu(page, 'Warehouses').click()
  await expect(page.getByRole('row', { name: /MAIN/ })).toContainText('Main warehouse')
  await page.screenshot({ path: 'test-results/en-warehouses.png' })

  // A product that is a stock item, with a reorder level.
  await menu(page, 'Products and prices').click()
  await page.getByRole('button', { name: 'New product' }).click()
  await page.getByLabel('Name (English)').fill('Widget')
  await page.getByLabel('Sale price').fill('50')
  await pick(page, 'Revenue account', '511')
  await page.getByRole('switch', { name: 'Stock item' }).click()
  await page.getByLabel('Reorder level').fill('5')
  await page.screenshot({ path: 'test-results/en-product-stock.png' })
  await page.getByRole('dialog').getByRole('button', { name: 'Save' }).click()
  await expect(page.getByRole('row', { name: /Widget/ })).toContainText('Stock')

  // Opening stock: 10 pieces at 20 each.
  await menu(page, 'Stock').click()
  await expect(page.getByText('No stock yet')).toBeVisible()
  await page.getByRole('button', { name: '+ Opening stock' }).click()
  await pick(page, 'Product 1', 'Widget')
  await page.getByLabel('Quantity 1').fill('10')
  await page.getByLabel('Cost per unit 1').fill('20')
  await page.screenshot({ path: 'test-results/en-opening-stock.png' })
  await page.getByRole('dialog').getByRole('button', { name: 'Save' }).click()
  await expect(page.getByRole('row', { name: /Widget/ })).toContainText('200.000')
  await expect(page.getByRole('row', { name: /Widget/ })).toContainText('10')

  // Selling more than there is is refused and nothing is posted.
  await menu(page, 'Sales').click()
  await page.getByRole('button', { name: 'New invoice' }).click()
  await pick(page, 'Customer', 'Gulf')
  await pick(page, 'Product 1', 'Widget')
  await page.getByLabel('Quantity 1').fill('11')
  await page.getByRole('button', { name: 'Issue and post' }).click()
  await expect(page.getByText('There is not enough stock for this quantity.').first()).toBeVisible()
  await page.screenshot({ path: 'test-results/en-insufficient-stock.png' })

  // Selling 8 works: cost of sales is 8 x 20, and 2 are left, which is below the reorder level.
  await page.getByLabel('Quantity 1').fill('8')
  await page.getByRole('button', { name: 'Issue and post' }).click()
  await expect(page.getByText(/Issued as SI-\d{4}-0001/)).toBeVisible()
  await page.goto('/reports/trial-balance')
  await expect(page.getByRole('row', { name: /^411/ })).toContainText('160.000')
  await page.goto('/')
  await expect(page.getByText('1 products are at or below their reorder level.')).toBeVisible()
  await page.screenshot({ path: 'test-results/en-summary-low-stock.png' })
  await page.getByRole('link', { name: 'See what to reorder' }).click()
  await expect(page.getByRole('row', { name: /Widget/ })).toContainText('Widget')

  // A stock count finds 1 where 2 are recorded: the missing one is written off at its cost.
  await menu(page, 'Stock').click()
  await page.getByRole('button', { name: '+ Count or adjust stock' }).click()
  await page.getByRole('button', { name: 'Load the stock of this warehouse' }).click()
  await page.getByLabel('Counted 1').fill('1')
  await page.screenshot({ path: 'test-results/en-stock-count.png' })
  await page.getByRole('dialog').getByRole('button', { name: 'Save' }).click()
  await expect(page.getByRole('dialog')).toBeHidden()
  await expect(page.locator('.ant-table-summary')).toContainText('20.000') // 40.000 before the count

  // The valuation report agrees with the stock account of the ledger.
  await page.goto('/reports/stock-valuation')
  await expect(page.getByRole('row', { name: /Widget/ })).toContainText('20.000')
  await expect(page.locator('.check-ok').first()).toBeVisible()
  await expectNoHorizontalScroll(page)
  await page.screenshot({ path: 'test-results/en-stock-valuation.png' })
  await page.goto('/reports/stock-movements')
  await expect(page.getByRole('row', { name: /Sale/ })).toBeVisible()
})

test('Arabic: the stock pages read right-to-left', async ({ page, request }) => {
  await page.goto('/')
  await setLanguage(page, 'ar')
  await createKuwaitCompany(page, companyFile('p5-stock-ar'), wizardAr)
  await page.getByRole('button', { name: wizardAr.create }).click()
  await expect(page.getByRole('heading', { name: 'الملخص' })).toBeVisible()
  await request.post('/api/company/modules', { data: { modules } })
  await page.reload()

  await menu(page, 'المخزون').click()
  await expect(page.getByRole('heading', { name: 'المخزون' })).toBeVisible()
  await expect(page.getByText('لا يوجد مخزون بعد')).toBeVisible()
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl')
  await expectNoHorizontalScroll(page)
  await page.screenshot({ path: 'test-results/ar-stock.png' })

  await page.getByRole('button', { name: '+ تحويل مخزون' }).click()
  await expect(page.getByRole('dialog')).toContainText('تحويل مخزون')
  await page.screenshot({ path: 'test-results/ar-stock-transfer.png' })
  await page.getByRole('dialog').getByRole('button', { name: 'إلغاء' }).click()

  await menu(page, 'المستودعات').click()
  await expect(page.getByRole('row', { name: /MAIN/ })).toContainText('المستودع الرئيسي')
  await expectNoHorizontalScroll(page)
  await page.screenshot({ path: 'test-results/ar-warehouses.png' })
})
