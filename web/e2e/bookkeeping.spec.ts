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
  for (const file of recent.filter((r) => r.path.includes('bookkeeping-')))
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

/** Picks an account in an account box by typing part of its code or name, then choosing it from the list. */
async function pickAccount(page: Page, label: string, search: string) {
  const box = page.getByLabel(label, { exact: true })
  await box.click()
  await box.fill(search)
  await page.locator('.ant-select-dropdown:visible .ant-select-item-option', { hasText: search }).first().click()
}

async function openFromMenu(page: Page, item: string) {
  await page.getByRole('menuitem', { name: item, exact: true }).click()
}

test('English: a month of bookkeeping gives a correct trial balance, profit and drill-down, and a locked month refuses changes', async ({ page }) => {
  await page.goto('/')
  await setLanguage(page, 'en')
  await createKuwaitCompany(page, companyFile('bookkeeping-en'), wizardEn)
  await page.getByRole('button', { name: wizardEn.create }).click()
  await expect(page.getByRole('heading', { name: 'Summary' })).toBeVisible()
  await expectNoHorizontalScroll(page)

  // The chart of accounts is there from the start, and can be searched.
  await openFromMenu(page, 'Chart of accounts')
  await expect(page.getByRole('heading', { name: 'Chart of accounts' })).toBeVisible()
  await page.getByPlaceholder('Search by code or name').fill('Rent')
  await expect(page.getByText('Rent', { exact: true }).first()).toBeVisible()
  await page.screenshot({ path: 'test-results/en-chart.png' })

  // A receipt of 1,000: Cash on hand against Product sales.
  await openFromMenu(page, 'Receipts')
  await expect(page.getByText('No receipts yet')).toBeVisible()
  await page.getByRole('button', { name: 'New receipt' }).click()
  await pickAccount(page, 'Received into', '111')
  await pickAccount(page, 'Account 1', '511')
  await page.getByLabel('Amount 1').fill('1000')
  await expectNoHorizontalScroll(page)
  await page.screenshot({ path: 'test-results/en-receipt-form.png' })
  await page.getByRole('button', { name: 'Save and post' }).click()
  await expect(page.getByText(/Posted as RV-\d{4}-0001/)).toBeVisible()
  await expect(page.getByRole('link', { name: /RV-\d{4}-0001/ })).toBeVisible()

  // A payment of 300: Rent from Cash on hand.
  await openFromMenu(page, 'Payments')
  await page.getByRole('button', { name: 'New payment' }).click()
  await pickAccount(page, 'Paid from', '111')
  await pickAccount(page, 'Account 1', '422')
  await page.getByLabel('Amount 1').fill('300')
  await page.getByRole('button', { name: 'Save and post' }).click()
  await expect(page.getByText(/Posted as PV-\d{4}-0001/)).toBeVisible()

  // A journal voucher of 500 that must balance: the Save button refuses until it does.
  await openFromMenu(page, 'Journal vouchers')
  await page.getByRole('button', { name: 'New journal voucher' }).click()
  await pickAccount(page, 'Account 1', '112')
  await page.getByLabel('Debit 1').fill('500')
  await pickAccount(page, 'Account 2', '31')
  await page.getByLabel('Credit 2').fill('400')
  await expect(page.getByText(/Difference/)).toBeVisible()
  await page.getByRole('button', { name: 'Save and post' }).click()
  await expect(page.getByText('The voucher cannot be saved yet')).toBeVisible()
  await expect(page.getByText('Debits and credits are not equal.').first()).toBeVisible()
  await page.screenshot({ path: 'test-results/en-journal-unbalanced.png' })
  await page.getByLabel('Credit 2').fill('500')
  await expect(page.getByText('Balanced')).toBeVisible()
  await page.getByRole('button', { name: 'Save and post' }).click()
  await expect(page.getByText(/Posted as JV-\d{4}-0001/)).toBeVisible()

  // The Summary shows the new balances straight away (never the ones from before): cash 700 + bank 500, profit 1,000 - 300.
  await openFromMenu(page, 'Summary')
  await expect(page.locator('.dashboard-cards')).toContainText('1,200.000')
  await expect(page.locator('.dashboard-cards')).toContainText('700.000')
  await page.screenshot({ path: 'test-results/en-summary-dashboard.png' })

  // Trial balance: debits 700 + 500 + 300 = 1,500 = credits 1,000 + 500.
  await openFromMenu(page, 'Reports')
  await page.getByRole('button', { name: 'Open Trial balance' }).click()
  await expect(page.getByRole('heading', { name: /Trial balance/ })).toBeVisible()
  await expect(page.locator('.report-total').first()).toContainText('1,500.000')
  await expect(page.locator('.check-ok').first()).toBeVisible()
  await expect(page.locator('.check-bad')).toHaveCount(0)
  await expectNoHorizontalScroll(page)
  await page.screenshot({ path: 'test-results/en-trial-balance.png' })

  // Drill down: the cash account's figure leads to its statement, a line of the statement leads to the voucher.
  await page.getByRole('link', { name: '111', exact: true }).click() // the account code carries the link
  await expect(page).toHaveURL(/\/reports\/statement-of-account/)
  await page.screenshot({ path: 'test-results/en-statement.png' })
  await page.getByRole('link', { name: /PV-\d{4}-0001|RV-\d{4}-0001/ }).first().click()
  await expect(page).toHaveURL(/\/vouchers\/(payment|receipt)\//)
  await expect(page.getByText('Posted', { exact: true }).first()).toBeVisible()

  // Profit and loss: 1,000 income less 300 rent = 700.
  await page.goto('/reports/profit-and-loss')
  await expect(page.getByRole('heading', { name: /Profit and loss/ })).toBeVisible()
  await expect(page.getByText('700.000').first()).toBeVisible()
  await page.screenshot({ path: 'test-results/en-profit-and-loss.png' })

  // Balance sheet balances too.
  await page.goto('/reports/balance-sheet')
  await expect(page.getByRole('heading', { name: /Balance sheet/ })).toBeVisible()
  await expect(page.locator('.check-bad')).toHaveCount(0)
  await expect(page.locator('.check-ok').first()).toBeVisible()
  await page.screenshot({ path: 'test-results/en-balance-sheet.png' })

  // Lock the current month: a new voucher dated today is refused with a plain explanation, and works again after unlocking.
  await openFromMenu(page, 'Settings')
  await expect(page.getByText('Locked months')).toBeVisible()
  const todayMonthStart = await page.evaluate(() => {
    const d = new Date()
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-01`
  })
  await page.getByRole('button', { name: `Lock ${todayMonthStart}` }).click()
  await page.getByRole('button', { name: 'Lock', exact: true }).last().click()
  await expect(page.getByRole('button', { name: `Unlock ${todayMonthStart}` })).toBeVisible()
  await page.screenshot({ path: 'test-results/en-settings-periods.png' })

  await openFromMenu(page, 'Receipts')
  await page.getByRole('button', { name: 'New receipt' }).click()
  await pickAccount(page, 'Received into', '111')
  await pickAccount(page, 'Account 1', '511')
  await page.getByLabel('Amount 1').fill('50')
  await page.getByRole('button', { name: 'Save and post' }).click()
  await expect(page.getByText('This month is locked.').first()).toBeVisible()
  await page.screenshot({ path: 'test-results/en-locked-month.png' })
  await page.getByRole('button', { name: 'Cancel' }).click()
  await page.getByRole('button', { name: 'Leave' }).click()

  await openFromMenu(page, 'Settings')
  await page.getByRole('button', { name: `Unlock ${todayMonthStart}` }).click()
  await expect(page.getByRole('button', { name: `Lock ${todayMonthStart}` })).toBeVisible()

  // Hijri dates are off until asked for, and display only.
  await openFromMenu(page, 'Receipts')
  await page.getByRole('button', { name: 'New receipt' }).click()
  await expect(page.locator('.hijri')).toHaveCount(0)
  await openFromMenu(page, 'Settings')
  await page.getByRole('switch', { name: 'Hijri dates' }).click()
  await openFromMenu(page, 'Receipts')
  await page.getByRole('button', { name: 'New receipt' }).click()
  await expect(page.locator('.hijri')).toBeVisible()
  await page.screenshot({ path: 'test-results/en-hijri.png' })
})

test('Arabic: vouchers and reports work right-to-left and nothing needs sideways scrolling', async ({ page }) => {
  await page.goto('/')
  await setLanguage(page, 'ar')
  await createKuwaitCompany(page, companyFile('bookkeeping-ar'), wizardAr)
  await page.getByRole('button', { name: wizardAr.create }).click()
  await expect(page.getByRole('heading', { name: 'الملخص' })).toBeVisible()
  await expect(page.getByText('الصندوق والبنك')).toBeVisible()
  await expectNoHorizontalScroll(page)
  await page.screenshot({ path: 'test-results/ar-summary-dashboard.png' })

  await openFromMenu(page, 'دليل الحسابات')
  await expect(page.getByRole('heading', { name: 'دليل الحسابات' })).toBeVisible()
  await expectNoHorizontalScroll(page)
  await page.screenshot({ path: 'test-results/ar-chart.png' })

  await openFromMenu(page, 'المقبوضات')
  await page.getByRole('button', { name: 'سند قبض جديد' }).click()
  await pickAccount(page, 'المستلَم في', '111')
  await pickAccount(page, 'الحساب 1', '511')
  await page.getByLabel('المبلغ 1').fill('250.5')
  await expectNoHorizontalScroll(page)
  await page.screenshot({ path: 'test-results/ar-receipt-form.png' })
  await page.getByRole('button', { name: 'حفظ وترحيل' }).click()
  await expect(page.getByText(/تم الترحيل برقم RV-\d{4}-0001/)).toBeVisible()

  await openFromMenu(page, 'التقارير')
  await page.getByRole('button', { name: 'فتح ميزان المراجعة' }).click()
  await expect(page.locator('.report-total').first()).toContainText('250.500')
  await expect(page.locator('.check-bad')).toHaveCount(0)
  await expectNoHorizontalScroll(page)
  await page.screenshot({ path: 'test-results/ar-trial-balance.png' })

  await openFromMenu(page, 'الإعدادات')
  await expect(page.getByText('الأشهر المقفلة')).toBeVisible()
  await expectNoHorizontalScroll(page)
  await page.screenshot({ path: 'test-results/ar-settings-cards.png', fullPage: true })

  // The sidebar is on the right in Arabic.
  const sider = await page.locator('.app-sider').boundingBox()
  const content = await page.locator('.app-content').boundingBox()
  expect(sider!.x).toBeGreaterThan(content!.x)
})
