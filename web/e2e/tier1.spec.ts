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
  for (const file of recent.filter((r) => r.path.includes('tier1-')))
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

const wizardAr = {
  newCompany: 'شركة جديدة',
  nameEn: 'اسم الشركة (بالإنجليزية)',
  nameAr: 'اسم الشركة (بالعربية)',
  country: 'الدولة',
  kuwait: 'الكويت',
  next: 'التالي',
  password: 'كلمة مرور الملف',
  confirm: 'تأكيد كلمة المرور',
  create: 'إنشاء الشركة',
}

async function pick(page: Page, label: string, search: string) {
  const box = page.getByLabel(label, { exact: true })
  await box.click()
  await box.fill(search)
  await page.locator('.ant-select-dropdown:visible .ant-select-item-option', { hasText: search }).first().click()
}

const menu = (page: Page, name: string) => page.getByRole('menuitem', { name, exact: true })

test('English: customers, aging, statements and cost centers', async ({ page }) => {
  await page.goto('/')
  await setLanguage(page, 'en')
  await createKuwaitCompany(page, companyFile('tier1-en'), wizardEn)
  await page.getByRole('button', { name: wizardEn.create }).click()
  await expect(page.getByRole('heading', { name: 'Summary' })).toBeVisible()

  // Customers and suppliers are on by default; cost centers are not, until switched on in Settings.
  await expect(menu(page, 'Customers')).toBeVisible()
  await expect(menu(page, 'Suppliers')).toBeVisible()
  await expect(menu(page, 'Cost centers')).toHaveCount(0)
  await menu(page, 'Settings').click()
  await page.getByRole('checkbox', { name: /Cost centers/ }).click()
  await expect(menu(page, 'Cost centers')).toBeVisible()
  await page.screenshot({ path: 'test-results/en-settings-modules.png', fullPage: true })

  // A cost center and a customer.
  await menu(page, 'Cost centers').click()
  await expect(page.getByText('No cost centers yet')).toBeVisible()
  await page.getByRole('button', { name: 'New cost center' }).click()
  await page.getByLabel('Code', { exact: true }).fill('N')
  await page.getByLabel('Name (English)').fill('North branch')
  await page.getByRole('button', { name: 'Save' }).click()
  await expect(page.getByRole('cell', { name: 'North branch' })).toBeVisible()

  await menu(page, 'Customers').click()
  await expect(page.getByText('No customers yet')).toBeVisible()
  await page.getByRole('button', { name: 'New customer' }).click()
  await expect(page.getByLabel('Code', { exact: true })).toHaveValue('C001') // the next free code is suggested
  await page.getByLabel('Name (English)').fill('Gulf Traders')
  await page.getByRole('button', { name: 'Save' }).click()
  await expect(page.getByRole('cell', { name: 'Gulf Traders' })).toBeVisible()
  await page.screenshot({ path: 'test-results/en-customers.png' })

  // A sale on credit: the line on receivables must name the customer, and a revenue line can carry the cost center.
  await menu(page, 'Journal vouchers').click()
  await page.getByRole('button', { name: 'New journal voucher' }).click()
  await pick(page, 'Account 1', '113')
  await page.getByLabel('Debit 1').fill('500')
  await pick(page, 'Account 2', '511')
  await page.getByLabel('Credit 2').fill('500')
  await pick(page, 'Cost center 2', 'North')
  await page.getByRole('button', { name: 'Save and post' }).click()
  await expect(page.getByText('Line 1: choose the customer or supplier this is for.').first()).toBeVisible()
  await page.screenshot({ path: 'test-results/en-journal-party-required.png' })

  await pick(page, 'Customer / supplier 1', 'Gulf')
  await expectNoHorizontalScroll(page)
  await page.screenshot({ path: 'test-results/en-journal-party.png' })
  await page.getByRole('button', { name: 'Save and post' }).click()
  await expect(page.getByText(/Posted as JV-\d{4}-0001/)).toBeVisible()

  // The customer owes 500 and the balance shows on the list.
  await menu(page, 'Customers').click()
  await expect(page.getByRole('row', { name: /Gulf Traders/ })).toContainText('500.000')

  // Aging: everything is within its 30 days, so it is not due yet. Clicking the row opens the statement.
  await menu(page, 'Reports').click()
  await expect(page.getByRole('button', { name: 'Open Cost centers' })).toBeVisible()
  await page.getByRole('button', { name: 'Open Customers aging' }).click()
  await expect(page.getByRole('heading', { name: 'Customers aging' })).toBeVisible()
  const agingRow = page.getByRole('row', { name: /Gulf Traders/ })
  await expect(agingRow).toContainText('500.000')
  await expect(page.locator('.check-ok')).toBeVisible()
  await expectNoHorizontalScroll(page)
  await page.screenshot({ path: 'test-results/en-aging.png' })

  await agingRow.getByRole('link', { name: 'C001' }).click()
  await expect(page).toHaveURL(/\/reports\/party-statement\?partyId=/)
  await expect(page.getByRole('row', { name: /JV-/ })).toContainText('500.000')
  await page.screenshot({ path: 'test-results/en-party-statement.png' })

  // Cost centers: the north branch earned 500, and its own profit and loss agrees.
  await page.goto('/reports/cost-centers')
  await expect(page.getByRole('row', { name: /North branch/ })).toContainText('500.000')
  await page.screenshot({ path: 'test-results/en-cost-centers-report.png' })
  await page.getByRole('row', { name: /North branch/ }).getByRole('link', { name: 'N', exact: true }).click()
  await expect(page).toHaveURL(/profit-and-loss\?costCenterId=/)
  await expect(page.getByRole('heading', { name: /Profit and loss: N North branch/ })).toBeVisible()
  await expect(page.getByText('500.000').first()).toBeVisible()

  // A customer that has been used cannot be deleted.
  await menu(page, 'Customers').click()
  await page.getByRole('button', { name: 'Delete C001' }).click()
  await page.getByRole('button', { name: 'Delete', exact: true }).last().click()
  await expect(page.getByText('This one has entries, so it cannot be deleted. Switch it off instead.')).toBeVisible()
})

test('Arabic: customers, suppliers and the aging report read right-to-left', async ({ page }) => {
  await page.goto('/')
  await setLanguage(page, 'ar')
  await createKuwaitCompany(page, companyFile('tier1-ar'), wizardAr)
  await page.getByRole('button', { name: wizardAr.create }).click()
  await expect(page.getByRole('heading', { name: 'الملخص' })).toBeVisible()

  await menu(page, 'العملاء').click()
  await expect(page.getByRole('heading', { name: 'العملاء' })).toBeVisible()
  await page.getByRole('button', { name: 'عميل جديد' }).click()
  await page.getByLabel('الاسم (بالعربية)').fill('شركة الخليج للتجارة')
  await page.getByRole('button', { name: 'حفظ' }).click()
  await expect(page.getByRole('cell', { name: 'شركة الخليج للتجارة' })).toBeVisible()
  await expectNoHorizontalScroll(page)
  await page.screenshot({ path: 'test-results/ar-customers.png' })

  await menu(page, 'الموردون').click()
  await expect(page.getByText('لا يوجد موردون بعد')).toBeVisible()

  await menu(page, 'التقارير').click()
  await page.getByRole('button', { name: 'فتح أعمار ديون العملاء' }).click()
  await expect(page.getByRole('heading', { name: 'أعمار ديون العملاء' })).toBeVisible()
  await expectNoHorizontalScroll(page)
  await page.screenshot({ path: 'test-results/ar-aging.png' })
})
