import { expect, test, type APIRequestContext, type Page } from '@playwright/test'
import { companyFile, createKuwaitCompany, expectNoHorizontalScroll, setLanguage } from './helpers'

// The server is shared by all tests, so every test starts with no company open.
test.beforeEach(async ({ request }) => {
  await request.post('/api/company/close')
})

// The start-screen tests expect an empty recent list, so these companies are taken off it again.
test.afterEach(async ({ request }) => {
  await request.post('/api/company/close')
  const recent = (await (await request.get('/api/recent-files')).json()) as { path: string }[]
  for (const file of recent.filter((r) => r.path.includes('p6-')))
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

const modules = ['bank-cash', 'customers-suppliers', 'sales', 'purchases', 'fixed-assets', 'payroll', 'expense-claims', 'budgets']

const menu = (page: Page, name: string) => page.getByRole('menuitem', { name, exact: true })

async function accountIds(request: APIRequestContext) {
  const accounts = (await (await request.get('/api/accounts')).json()) as { id: string; code: string }[]
  return (code: string) => accounts.find((a) => a.code === code)!.id
}

test('English: an asset is bought, depreciated month by month, checked against the books, and sold', async ({ page, request }) => {
  await page.goto('/')
  await setLanguage(page, 'en')
  await createKuwaitCompany(page, companyFile('p6-assets'), wizardEn)
  await page.getByRole('button', { name: wizardEn.create }).click()
  await expect(page.getByRole('heading', { name: 'Summary' })).toBeVisible()

  await request.post('/api/company/modules', { data: { modules } })
  const id = await accountIds(request)

  // The purchase is posted like any other (cost into the asset account), and the asset is entered in the register.
  const bought = await request.post('/api/vouchers/post', {
    data: { id: null, input: { kind: 'Payment', date: '2026-01-15', cashAccountId: id('112'), reference: null, memo: null, lines: [{ id: null, accountId: id('121'), description: null, debit: 1200, credit: 0, partyId: null, costCenterId: null }] } },
  })
  expect(bought.status()).toBe(200)
  const created = await request.post('/api/assets', {
    data: { code: 'A001', nameAr: '', nameEn: 'Laptop', kind: 'Tangible', acquisitionDate: '2026-01-15', cost: 1200, salvage: 0, usefulLifeMonths: 12, method: 'StraightLine', annualRate: 0, assetAccountId: id('121'), accumulatedAccountId: null, expenseAccountId: null },
  })
  expect(created.status()).toBe(200)
  await page.reload()

  await menu(page, 'Fixed assets').click()
  await expect(page.getByRole('row', { name: /A001/ })).toContainText('1,200.000')
  await page.screenshot({ path: 'test-results/en-assets.png' })

  // Post the depreciation of every month that has ended.
  await page.getByRole('button', { name: 'Post depreciation' }).click()
  await page.screenshot({ path: 'test-results/en-assets-run.png' })
  await page.getByRole('dialog').getByRole('button', { name: 'Post', exact: true }).click()
  await expect(page.getByRole('dialog')).toBeHidden()
  await expect(page.getByRole('row', { name: /A001/ })).toContainText('900.000') // Jan to Sep: nine months of 100 (the run on opening may have done it already)
  await page.screenshot({ path: 'test-results/en-assets-depreciated.png' })

  // The register agrees with the ledger.
  await page.goto('/reports/asset-register')
  await expect(page.getByRole('row', { name: /A001/ })).toBeVisible()
  await expect(page.locator('.check-ok').first()).toBeVisible()
  await expect(page.locator('.check-bad')).toHaveCount(0)
  await expectNoHorizontalScroll(page)
  await page.screenshot({ path: 'test-results/en-asset-register.png' })

  // A new asset through the form.
  await menu(page, 'Fixed assets').click()
  await page.getByRole('button', { name: '+ New asset' }).click()
  await page.getByLabel('Name (English)').fill('Printer')
  await page.getByLabel(/^Cost/).fill('300')
  await page.getByLabel('Asset account').click()
  await page.getByLabel('Asset account').fill('121')
  await page.locator('.ant-select-dropdown:visible .ant-select-item-option', { hasText: '121' }).first().click()
  await page.screenshot({ path: 'test-results/en-asset-form.png' })
  await page.getByRole('dialog').getByRole('button', { name: 'Save' }).click()
  await expect(page.getByRole('row', { name: /Printer/ })).toContainText('300.000')

  // Selling the laptop takes it off the books.
  await page.getByRole('button', { name: /Sell or scrap A001/ }).click()
  await page.screenshot({ path: 'test-results/en-asset-dispose.png' })
  await page.getByRole('dialog').getByRole('button', { name: 'Sell or scrap', exact: true }).click()
  await expect(page.getByRole('row', { name: /A001/ })).toContainText('Disposed')
})

test('Arabic: the fixed assets page reads right-to-left', async ({ page, request }) => {
  await page.goto('/')
  await setLanguage(page, 'ar')
  await createKuwaitCompany(page, companyFile('p6-assets-ar'), wizardAr)
  await page.getByRole('button', { name: wizardAr.create }).click()
  await expect(page.getByRole('heading', { name: 'الملخص' })).toBeVisible()
  await request.post('/api/company/modules', { data: { modules } })
  await page.reload()

  await menu(page, 'الأصول الثابتة').click()
  await expect(page.getByRole('heading', { name: 'الأصول الثابتة' })).toBeVisible()
  await expect(page.getByText('لا توجد أصول بعد')).toBeVisible()
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl')
  await expectNoHorizontalScroll(page)
  await page.screenshot({ path: 'test-results/ar-assets.png' })

  await page.getByRole('button', { name: '+ أصل جديد' }).click()
  await expect(page.getByRole('dialog')).toContainText('أصل جديد')
  await page.screenshot({ path: 'test-results/ar-asset-form.png' })
})
