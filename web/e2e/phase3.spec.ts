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

// ---------------------------------------------------------------- Receipts set against invoices, in two currencies

test('English: a dollar invoice is paid at a better rate, the gain is booked and the invoice shows as paid', async ({ page, request }) => {
  await page.goto('/')
  await setLanguage(page, 'en')
  await createKuwaitCompany(page, companyFile('p3-settle'), wizardEn)
  await page.getByRole('button', { name: wizardEn.create }).click()
  await expect(page.getByRole('heading', { name: 'Summary' })).toBeVisible()

  const accounts = (await (await request.get('/api/accounts')).json()) as { id: string; code: string }[]
  const customer = (await (await request.post('/api/parties', { data: { kind: 'Customer', code: 'C100', nameAr: '', nameEn: 'Gulf Traders', phone: null, email: null, address: null, taxNumber: null, creditLimit: 0, paymentTermsDays: 30, notes: null, priceListId: null } })).json()) as { id: string }
  const today = new Date().toISOString().slice(0, 10)
  await request.post('/api/documents/issue', {
    data: { id: null, input: { kind: 'SalesInvoice', date: today, dueDate: null, partyId: customer.id, currencyCode: 'USD', exchangeRate: 0.3, reference: null, memo: null, discountPercent: 0, lines: [{ id: null, productId: null, accountId: accounts.find((a) => a.code === '511')!.id, description: 'Consulting', quantity: 1, unitPrice: 1000, discountPercent: 0 }] } },
  })

  // The invoice list says what is still owed.
  await menu(page, 'Sales').click()
  await expect(page.getByRole('row', { name: /SI-/ })).toContainText('1,000.00')
  await expect(page.getByRole('row', { name: /SI-/ })).toContainText('USD')

  // Open it and receive the money: the payment is in dollars at today's rate, which is better than the invoice's.
  await page.getByRole('link', { name: /SI-/ }).click()
  await page.getByRole('button', { name: 'Receive payment' }).click()
  await expect(page.getByRole('heading', { name: 'Receive payment' })).toBeVisible()
  await pick(page, 'Received into', '112')
  await page.getByLabel(/Rate: 1 USD/).fill('0.32')
  await expect(page.getByLabel(/Paying now SI-/)).toHaveValue('1000.00')
  await expectNoHorizontalScroll(page)
  await page.screenshot({ path: 'test-results/en-receive-payment.png' })
  await page.getByRole('button', { name: 'Record receipt' }).click()
  await expect(page.getByText(/Receipt RV-\d{4}-0001 recorded/)).toBeVisible()
  await expect(page.getByText(/Exchange gain: 20\.000 KWD/)).toBeVisible()

  // The invoice is paid; it can no longer be changed.
  await expect(page.getByRole('row', { name: /SI-/ })).toContainText('Paid')
  await page.getByRole('link', { name: /SI-/ }).click()
  await expect(page.getByText('This invoice has been paid or credited')).toBeVisible()

  // The books: customers owe nothing, 320 dinars came into the bank, and 20 is income in exchange differences.
  await page.goto('/reports/trial-balance')
  await expect(page.getByRole('row', { name: /Exchange differences/ })).toContainText('20.000')
  await expect(page.getByRole('row', { name: /^112/ })).toContainText('320.000')
  await expect(page.locator('.check-ok').first()).toBeVisible()
})

test('Arabic: the receive-payment page reads right-to-left', async ({ page }) => {
  await page.goto('/')
  await setLanguage(page, 'ar')
  await createKuwaitCompany(page, companyFile('p3-settle-ar'), wizardAr)
  await page.getByRole('button', { name: wizardAr.create }).click()
  await expect(page.getByRole('heading', { name: 'الملخص' })).toBeVisible()

  await page.goto('/settlements/receive')
  await expect(page.getByRole('heading', { name: 'استلام دفعة' })).toBeVisible()
  await expectNoHorizontalScroll(page)
  await page.screenshot({ path: 'test-results/ar-receive-payment.png' })
})

// ---------------------------------------------------------------- Recurring invoices

test('English: an invoice is repeated monthly and what is due is made as drafts', async ({ page, request }) => {
  await page.goto('/')
  await setLanguage(page, 'en')
  await createKuwaitCompany(page, companyFile('p3-recurring'), wizardEn)
  await page.getByRole('button', { name: wizardEn.create }).click()
  await expect(page.getByRole('heading', { name: 'Summary' })).toBeVisible()

  const accounts = (await (await request.get('/api/accounts')).json()) as { id: string; code: string }[]
  const customer = (await (await request.post('/api/parties', { data: { kind: 'Customer', code: 'C100', nameAr: '', nameEn: 'Gulf Traders', phone: null, email: null, address: null, taxNumber: null, creditLimit: 0, paymentTermsDays: 30, notes: null, priceListId: null } })).json()) as { id: string }
  const today = new Date()
  const iso = (d: Date) => d.toISOString().slice(0, 10)
  await request.post('/api/documents/issue', {
    data: { id: null, input: { kind: 'SalesInvoice', date: iso(today), dueDate: null, partyId: customer.id, currencyCode: null, exchangeRate: null, reference: null, memo: null, discountPercent: 0, lines: [{ id: null, productId: null, accountId: accounts.find((a) => a.code === '511')!.id, description: 'Rent', quantity: 1, unitPrice: 300, discountPercent: 0 }] } },
  })

  // Repeat the invoice monthly starting two months back: three are due by today.
  await menu(page, 'Sales').click()
  await page.getByRole('link', { name: /SI-/ }).click()
  await page.getByRole('button', { name: 'Repeat…' }).click()
  await expect(page.getByRole('dialog')).toContainText('Repeat this')
  const start = new Date(today.getFullYear(), today.getMonth() - 2, 1)
  const ddmmyyyy = `01/${String(start.getMonth() + 1).padStart(2, '0')}/${start.getFullYear()}`
  await page.getByLabel('Next date').fill(ddmmyyyy)
  await page.keyboard.press('Enter')
  await page.screenshot({ path: 'test-results/en-recurring-modal.png' })
  await page.getByRole('dialog').getByRole('button', { name: 'Save' }).click()
  await expect(page.getByText(/The schedule was saved/)).toBeVisible()

  await menu(page, 'Recurring').click()
  await expect(page.getByRole('row', { name: /Sales invoice/ })).toContainText('Every month')
  await page.getByRole('button', { name: 'Make what is due now' }).click()
  await expect(page.getByText('3 made.')).toBeVisible()
  await expect(page.getByRole('row', { name: /Sales invoice/ })).toContainText('3')
  await expectNoHorizontalScroll(page)
  await page.screenshot({ path: 'test-results/en-recurring.png' })

  // They are drafts: they are in the invoice list but not in the books.
  await menu(page, 'Sales').click()
  await expect(page.getByRole('row', { name: /Draft/ })).toHaveCount(3)
  await page.goto('/reports/trial-balance')
  await expect(page.getByRole('row', { name: /Accounts receivable/ })).toContainText('300.000')
})

// ---------------------------------------------------------------- Tax (a country whose pack has VAT)

async function createVatCompany(request: import('@playwright/test').APIRequestContext, name: string) {
  const response = await request.post('/api/company/create', {
    data: {
      path: companyFile(name),
      password: 'correct-horse-battery',
      company: {
        nameAr: 'شركة الضريبة', nameEn: 'Vat Trading', countryCode: 'SA', baseCurrencyCode: 'SAR', fiscalYearStartMonth: 1, firstFiscalYear: new Date().getFullYear(),
        taxNumbers: { 'vat-number': '300000000000003' }, address: null, chartTemplateKey: 'default', enabledModules: ['bank-cash', 'customers-suppliers', 'sales', 'purchases'],
      },
    },
  })
  expect(response.status()).toBe(200)
}

test('English: VAT is added to an invoice, posted, and shows up in the tax return', async ({ page, request }) => {
  await createVatCompany(request, 'p3-vat')
  await request.post('/api/parties', { data: { kind: 'Customer', code: 'C100', nameAr: '', nameEn: 'Riyadh Stores', phone: null, email: null, address: null, taxNumber: '300111111111113', creditLimit: 0, paymentTermsDays: 30, notes: null, priceListId: null } })
  await page.goto('/')
  await setLanguage(page, 'en')

  // The codes of the country are there, with the standard one as the default.
  await menu(page, 'Tax codes').click()
  await expect(page.getByRole('row', { name: /SA-VAT-STD/ })).toContainText('Default')
  await expect(page.getByRole('row', { name: /SA-VAT-STD/ })).toContainText('15.00')
  await expect(page.getByRole('row', { name: /SA-VAT-EXEMPT/ })).toContainText('Exempt')
  await expectNoHorizontalScroll(page)
  await page.screenshot({ path: 'test-results/en-tax-codes.png' })

  // A new invoice line starts with the default code; the totals show the tax.
  await menu(page, 'Sales').click()
  await page.getByRole('button', { name: 'New invoice' }).click()
  await pick(page, 'Customer', 'Riyadh')
  await pick(page, 'Account 1', '511')
  await page.getByLabel('Quantity 1').fill('1')
  await page.getByLabel('Price 1').fill('1000')
  await expect(page.locator('.ant-select', { has: page.getByLabel('Tax 1') })).toContainText('SA-VAT-STD')
  await expect(page.locator('.doc-totals')).toContainText('150.00')
  await expect(page.locator('.doc-totals')).toContainText('1,150.00')
  await expectNoHorizontalScroll(page)
  await page.screenshot({ path: 'test-results/en-invoice-vat.png' })
  await page.getByRole('button', { name: 'Issue and post' }).click()
  await expect(page.getByText(/Issued as SI-\d{4}-0001/)).toBeVisible()
  await expect(page.getByRole('row', { name: /SI-/ })).toContainText('1,150.00')

  // The books: customers owe 1,150, revenue 1,000, tax payable 150.
  await page.goto('/reports/trial-balance')
  await expect(page.getByRole('row', { name: /Accounts receivable/ })).toContainText('1,150.00')
  await expect(page.getByRole('row', { name: /Taxes payable/ })).toContainText('150.00')

  // Excel exports of the lists and of the return are real .xlsx files.
  for (const key of ['documents', 'products', 'parties', 'tax-codes', 'tax-return']) {
    const response = await request.get(`/api/reports/${key}/export?format=Xlsx&layout=Both`)
    expect(response.status(), key).toBe(200)
    expect((await response.body()).subarray(0, 2).toString(), key).toBe('PK')
  }

  // The return agrees with the ledger.
  await menu(page, 'Reports').click()
  await page.getByRole('button', { name: 'Open Tax return' }).click()
  await expect(page.getByRole('row', { name: /Tax on sales/ })).toContainText('150.00')
  await expect(page.getByRole('row', { name: /SA-VAT-STD/ })).toContainText('1,000.00')
  await expect(page.locator('.check-bad')).toHaveCount(0)
  await page.screenshot({ path: 'test-results/en-tax-return.png' })
})

test('Arabic: the tax codes page and the tax return read right-to-left', async ({ page, request }) => {
  await createVatCompany(request, 'p3-vat-ar')
  await page.goto('/')
  await setLanguage(page, 'ar')

  await menu(page, 'رموز الضريبة').click()
  await expect(page.getByRole('heading', { name: 'رموز الضريبة' })).toBeVisible()
  await expect(page.getByRole('row', { name: /SA-VAT-STD/ })).toContainText('الافتراضي')
  await expectNoHorizontalScroll(page)
  await page.screenshot({ path: 'test-results/ar-tax-codes.png' })

  await page.goto('/reports/tax-return')
  await expect(page.getByRole('heading', { name: 'الإقرار الضريبي' })).toBeVisible()
  await expectNoHorizontalScroll(page)
  await page.screenshot({ path: 'test-results/ar-tax-return.png' })
})

// ---------------------------------------------------------------- Excel/CSV import of products and journal entries

test('English: products and journal entries are imported from files, and a template can be downloaded', async ({ page, request }) => {
  await createVatCompany(request, 'p3-import')
  await page.goto('/')
  await setLanguage(page, 'en')
  const csv = (text: string, name: string) => ({ name, mimeType: 'text/csv', buffer: Buffer.from(text, 'utf8') })

  await menu(page, 'Products and prices').click()
  await page.getByRole('button', { name: 'Import…' }).click()
  const download = page.waitForEvent('download')
  await page.getByRole('button', { name: 'Download a template' }).click()
  expect((await download).suggestedFilename()).toBe('products-template.xlsx')

  // A wrong file imports nothing and names the row; the corrected file goes in.
  await page.getByTestId('import-file').setInputFiles(csv('Code,Name,Sale price,Revenue account,Tax code\nA1,Widget,10,511,SA-VAT-STD\nA2,Gadget,abc,511,\n', 'products.csv'))
  await expect(page.getByText('Row 3: The price is not a number.')).toBeVisible()
  await page.getByTestId('import-file').setInputFiles(csv('Code,Name,Sale price,Revenue account,Tax code\nA1,Widget,10,511,SA-VAT-STD\nA2,Gadget,25.5,511,\n', 'products.csv'))
  await expect(page.getByText('2 imported.').first()).toBeVisible()
  await page.getByRole('button', { name: 'Close' }).last().click()
  await expect(page.getByRole('row', { name: /Gadget/ })).toContainText('25.50')

  // Journal entries: two entries, each balanced.
  await menu(page, 'Journal vouchers').click()
  await page.getByRole('button', { name: 'Import…' }).click()
  await page.getByTestId('import-file').setInputFiles(csv('Entry,Date,Account,Debit,Credit\n1,2026-10-02,111,500,\n1,,31,,500\n2,2026-10-03,111,50,\n2,,31,,50\n', 'journal.csv'))
  await expect(page.getByText('2 imported.').first()).toBeVisible()
  await page.getByRole('button', { name: 'Close' }).last().click()
  await expect(page.getByRole('link', { name: /JV-/ })).toHaveCount(2)
  await page.screenshot({ path: 'test-results/en-journal-import.png' })
})
