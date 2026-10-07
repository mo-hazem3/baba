import { expect, test, type APIRequestContext, type Page } from '@playwright/test'
import { companyFile, createKuwaitCompany, expectNoHorizontalScroll, password, setLanguage } from './helpers'

// The server is shared by all tests, so every test starts with no company open.
test.beforeEach(async ({ request }) => {
  await request.post('/api/company/close')
})

// The start-screen tests expect an empty recent list, so these companies are taken off it again.
test.afterEach(async ({ request }) => {
  await request.post('/api/company/close')
  const recent = (await (await request.get('/api/recent-files')).json()) as { path: string }[]
  for (const file of recent.filter((r) => r.path.includes('p2-')))
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
const csv = (text: string) => ({ name: 'file.csv', mimeType: 'text/csv', buffer: Buffer.from(text, 'utf8') })

test('English: transfer, bank reconciliation with a loaded statement, opening balances and imports', async ({ page }) => {
  await page.goto('/')
  await setLanguage(page, 'en')
  await createKuwaitCompany(page, companyFile('p2-en'), wizardEn)
  await page.getByRole('button', { name: wizardEn.create }).click()
  await expect(page.getByRole('heading', { name: 'Summary' })).toBeVisible()
  await expect(menu(page, 'Bank and cash')).toBeVisible()

  // 10,000 of capital into the bank, then 1,000 of rent paid from the bank.
  await menu(page, 'Receipts').click()
  await page.getByRole('button', { name: 'New receipt' }).click()
  await pick(page, 'Received into', '112')
  await pick(page, 'Account 1', '31')
  await page.getByLabel('Amount 1').fill('10000')
  await page.getByRole('button', { name: 'Save and post' }).click()
  await expect(page.getByText(/Posted as RV-\d{4}-0001/)).toBeVisible()

  await menu(page, 'Payments').click()
  await page.getByRole('button', { name: 'New payment' }).click()
  await pick(page, 'Paid from', '112')
  await pick(page, 'Account 1', '422')
  await page.getByLabel('Amount 1').fill('1000')
  await page.getByRole('button', { name: 'Save and post' }).click()
  await expect(page.getByText(/Posted as PV-\d{4}-0001/)).toBeVisible()

  // A transfer of 300 from the cash box to the bank: only bank and cash accounts are offered.
  await menu(page, 'Transfers').click()
  await expect(page.getByText('No transfers yet')).toBeVisible()
  await page.getByRole('button', { name: 'New transfer' }).click()
  await pick(page, 'Transfer from', '111')
  await page.getByLabel('Transfer to', { exact: true }).click()
  await page.getByLabel('Transfer to', { exact: true }).fill('422')
  await expect(page.locator('.ant-select-dropdown:visible').getByText('No account matches.')).toBeVisible() // rent is not a bank account
  await page.keyboard.press('Escape')
  await pick(page, 'Transfer to', '112')
  await page.getByLabel('Amount', { exact: true }).fill('300')
  await page.screenshot({ path: 'test-results/en-transfer-form.png' })
  await page.getByRole('button', { name: 'Save and post' }).click()
  await expect(page.getByText(/Posted as TV-\d{4}-0001/)).toBeVisible()

  // The bank account holds 10,000 - 1,000 + 300 = 9,300, with three entries still to check.
  await menu(page, 'Bank and cash').click()
  const bankRow = page.getByRole('row', { name: /112/ })
  await expect(bankRow).toContainText('9,300.000')
  await expect(bankRow).toContainText('3 entries')
  await expect(bankRow).toContainText('Never reconciled')
  await page.screenshot({ path: 'test-results/en-bank.png' })

  // Reconcile: with the wrong balance the button stays off and the difference is shown.
  await page.getByRole('button', { name: 'Reconcile 112' }).click()
  await expect(page.getByRole('heading', { name: /Bank reconciliation: 112/ })).toBeVisible()
  await page.getByRole('button', { name: 'Tick all', exact: true }).click()
  await page.getByLabel('Closing balance on the statement').fill('9000')
  await expect(page.getByRole('button', { name: 'Finish reconciliation' })).toBeDisabled()
  await expect(page.locator('.reconcile-figures')).toContainText('300.000') // 9,000 - 9,300 = -300
  await page.screenshot({ path: 'test-results/en-reconcile-difference.png' })

  // Load the bank's statement from a file: its three lines are matched with the three entries.
  await page.getByRole('button', { name: 'Load statement from a file…' }).click()
  const today = new Date().toISOString().slice(0, 10)
  await page.getByTestId('import-file').setInputFiles(csv(`Date,Description,Reference,Amount\n${today},Capital deposit,DEP1,"10,000.000"\n${today},Rent,CHQ7,-1000\n${today},Cash deposit,DEP2,300`))
  await expect(page.getByText('3 imported.').first()).toBeVisible()
  await page.getByRole('button', { name: 'Close' }).last().click()
  await expect(page.getByText(/Matches (RV|PV|TV)-/).first()).toBeVisible()
  await page.getByRole('button', { name: 'Untick all' }).click()
  await page.getByRole('button', { name: 'Tick suggested matches' }).click()
  await page.getByLabel('Closing balance on the statement').fill('9300')
  await expect(page.locator('.reconcile-figures')).toContainText('No difference')
  await expect(page.getByRole('button', { name: 'Finish reconciliation' })).toBeEnabled()
  await page.screenshot({ path: 'test-results/en-reconcile-ready.png' })
  await page.getByRole('button', { name: 'Finish reconciliation' }).click()
  await expect(page.getByText('Reconciliation finished.')).toBeVisible()
  await expect(page.getByText('Earlier reconciliations')).toBeVisible()
  await page.screenshot({ path: 'test-results/en-reconciled.png' })

  await menu(page, 'Bank and cash').click()
  await expect(page.getByRole('row', { name: /112/ })).toContainText('All checked')

  // The rent payment was checked against the statement, so it cannot be changed.
  await menu(page, 'Payments').click()
  await page.getByRole('link', { name: /PV-\d{4}-0001/ }).click()
  await page.getByLabel('Amount 1').fill('1100')
  await page.getByRole('button', { name: 'Save and post' }).click()
  await expect(page.getByText('This voucher has been checked against a bank statement', { exact: false }).first()).toBeVisible()
  await page.getByRole('button', { name: 'Cancel' }).click()
  await page.getByRole('button', { name: 'Leave' }).click()

  // Undo the reconciliation: everything is open again.
  await menu(page, 'Bank and cash').click()
  await page.getByRole('button', { name: 'Reconcile 112' }).click()
  await page.getByRole('button', { name: 'Undo the latest' }).click()
  await page.getByRole('button', { name: 'OK' }).click()
  await expect(page.getByText('The reconciliation was undone.')).toBeVisible()
  await expect(page.getByRole('row', { name: /Rent|PV-/ }).first()).toBeVisible()

  // Opening balances: one voucher, balance sheet accounts only.
  await menu(page, 'Opening balances').click()
  await expect(page.getByRole('heading', { name: 'Opening balances' })).toBeVisible()
  await pick(page, 'Account 1', '111')
  await page.getByLabel('Debit 1').fill('500')
  await page.getByLabel('Account 2', { exact: true }).click()
  await page.getByLabel('Account 2', { exact: true }).fill('422')
  await expect(page.locator('.ant-select-dropdown:visible .ant-select-item-option', { hasText: '422' })).toHaveCount(0) // an expense account is not offered
  await page.keyboard.press('Escape')
  await pick(page, 'Account 2', '31')
  await page.getByLabel('Credit 2').fill('500')
  await expectNoHorizontalScroll(page)
  await page.screenshot({ path: 'test-results/en-opening.png' })
  await page.getByRole('button', { name: 'Save and post' }).click()
  await expect(page.getByText(/Posted as OB-\d{4}-0001/)).toBeVisible()
  await menu(page, 'Opening balances').click()
  await expect(page.getByRole('heading', { name: /Opening balances OB-\d{4}-0001/ })).toBeVisible() // it opens the one that exists

  // Import the chart of accounts and customers from files.
  await menu(page, 'Chart of accounts').click()
  await page.getByRole('button', { name: 'Import…' }).click()
  await page.getByTestId('import-file').setInputFiles(csv('Code,Name,Parent code,Type\n9,Other assets,,Asset\n91,Safe,9,\n111,Duplicate,,Asset'))
  await expect(page.getByText('Nothing was imported.', { exact: false })).toBeVisible()
  await expect(page.getByText('Row 4: Another account already uses this code.')).toBeVisible()
  await page.screenshot({ path: 'test-results/en-import-errors.png' })
  await page.getByTestId('import-file').setInputFiles(csv('Code,Name,Parent code,Type\n9,Other assets,,Asset\n91,Safe,9,'))
  await expect(page.getByText('2 imported.').first()).toBeVisible()
  await page.getByRole('button', { name: 'Close' }).last().click()
  await page.getByPlaceholder('Search by code or name').fill('Safe')
  await expect(page.getByRole('cell', { name: 'Safe', exact: true })).toBeVisible()

  await menu(page, 'Customers').click()
  await page.getByRole('button', { name: 'Import…' }).click()
  await page.getByTestId('import-file').setInputFiles(csv('Name,Credit limit,Payment terms\nImported Trading,1500,45'))
  await expect(page.getByText('1 imported.').first()).toBeVisible()
  await page.getByRole('button', { name: 'Close' }).last().click()
  await expect(page.getByRole('row', { name: /Imported Trading/ })).toContainText('1,500.000')
})

/** A company whose books start in 2024, made through the API so that two fiscal years have already ended. */
async function createOldCompany(request: APIRequestContext, name: string) {
  const created = await request.post('/api/company/create', {
    data: {
      path: companyFile(name),
      password,
      company: {
        nameAr: 'شركة قديمة',
        nameEn: 'Old Books',
        countryCode: 'KW',
        baseCurrencyCode: 'KWD',
        fiscalYearStartMonth: 1,
        firstFiscalYear: 2024,
        taxNumbers: {},
        address: null,
        chartTemplateKey: 'default',
        enabledModules: ['bank-cash', 'customers-suppliers'],
      },
    },
  })
  expect(created.status()).toBe(200)
  const accounts = (await (await request.get('/api/accounts')).json()) as { id: string; code: string }[]
  const id = (code: string) => accounts.find((a) => a.code === code)!.id
  const post = async (kind: 'Receipt' | 'Payment', date: string, account: string, amount: number) => {
    const response = await request.post('/api/vouchers/post', {
      data: {
        id: null,
        input: {
          kind,
          date,
          cashAccountId: id('112'),
          reference: null,
          memo: null,
          lines: [{ id: null, accountId: id(account), description: null, debit: kind === 'Payment' ? amount : 0, credit: kind === 'Receipt' ? amount : 0, partyId: null, costCenterId: null }],
        },
      },
    })
    expect(response.status()).toBe(200)
  }
  await post('Receipt', '2024-06-01', '511', 1000) // sales of 1,000 in 2024
  await post('Payment', '2024-07-01', '422', 400) // rent of 400 in 2024
}

test('English: closing a fiscal year at its end, and reopening it', async ({ page, request }) => {
  await page.goto('/')
  await setLanguage(page, 'en')
  await createOldCompany(request, 'p2-year')
  await page.reload()
  await expect(page.getByRole('heading', { name: 'Summary' })).toBeVisible()

  await menu(page, 'Year-end').click()
  await expect(page.getByRole('heading', { name: 'Year-end' })).toBeVisible()
  const row2024 = page.getByRole('row', { name: /2024/ })
  await expect(row2024).toContainText('Ready to close')
  await expect(row2024).toContainText('600.000') // 1,000 - 400
  await expect(page.getByRole('row', { name: /2026/ })).toContainText('In progress')
  await expect(page.getByRole('button', { name: 'Close year 2026' })).toHaveCount(0) // it has not ended
  await page.screenshot({ path: 'test-results/en-year-end.png' })

  await page.getByRole('button', { name: 'Close year 2024' }).click()
  await expect(page.getByText('Baba will:')).toBeVisible()
  await page.screenshot({ path: 'test-results/en-year-end-confirm.png' })
  await page.getByRole('button', { name: 'Close year', exact: true }).last().click()
  const done = page.getByRole('dialog', { name: 'Year 2024 is closed' })
  await expect(done).toContainText('CL-2024-0001')
  await expect(done).toContainText('before-close-2024')
  await page.getByRole('button', { name: 'Got it' }).click()
  await expect(row2024).toContainText('Closed')
  await expect(row2024).toContainText('600.000') // the profit is still shown for the closed year
  await expect(page.getByRole('button', { name: 'Reopen 2024' })).toBeVisible()
  await page.screenshot({ path: 'test-results/en-year-closed.png' })

  // The closed year is locked: a new voucher dated in it is refused.
  const accounts = (await (await request.get('/api/accounts')).json()) as { id: string; code: string }[]
  const refused = await request.post('/api/vouchers/post', {
    data: {
      id: null,
      input: {
        kind: 'Receipt', date: '2024-09-01', cashAccountId: accounts.find((a) => a.code === '112')!.id, reference: null, memo: null,
        lines: [{ id: null, accountId: accounts.find((a) => a.code === '511')!.id, description: null, debit: 0, credit: 5, partyId: null, costCenterId: null }],
      },
    },
  })
  expect(refused.status()).toBe(400)

  await page.getByRole('button', { name: 'Reopen 2024' }).click()
  await page.getByRole('button', { name: 'Reopen', exact: true }).last().click()
  await expect(page.getByText('Year 2024 is open again.')).toBeVisible()
  await expect(row2024).toContainText('Ready to close')
})

test('Arabic: bank, reconciliation, year-end and import read right-to-left', async ({ page }) => {
  await page.goto('/')
  await setLanguage(page, 'ar')
  await createKuwaitCompany(page, companyFile('p2-ar'), wizardAr)
  await page.getByRole('button', { name: wizardAr.create }).click()
  await expect(page.getByRole('heading', { name: 'الملخص' })).toBeVisible()

  await menu(page, 'البنك والصندوق').click()
  await expect(page.getByRole('heading', { name: 'البنك والصندوق' })).toBeVisible()
  await expect(page.getByRole('row', { name: /112/ })).toContainText('لم تُسوَّ بعد')
  await expectNoHorizontalScroll(page)
  await page.screenshot({ path: 'test-results/ar-bank.png' })

  await page.getByRole('button', { name: 'تسوية 112' }).click()
  await expect(page.getByRole('heading', { name: /تسوية البنك/ })).toBeVisible()
  await expectNoHorizontalScroll(page)
  await page.screenshot({ path: 'test-results/ar-reconcile.png' })
  await page.getByRole('button', { name: 'تحميل الكشف من ملف…' }).click()
  await page.getByTestId('import-file').setInputFiles(csv('التاريخ,البيان,المبلغ\nبلا تاريخ,خطأ,10'))
  await expect(page.getByText('الصف 2: التاريخ غير صحيح.')).toBeVisible()
  await page.screenshot({ path: 'test-results/ar-import-errors.png' })
  await page.getByRole('button', { name: 'إلغاء' }).click()

  await menu(page, 'التحويلات').click()
  await page.getByRole('button', { name: 'تحويل جديد' }).click()
  await expect(page.getByRole('heading', { name: 'تحويل جديد' })).toBeVisible()
  await expectNoHorizontalScroll(page)
  await page.screenshot({ path: 'test-results/ar-transfer.png' })
  await page.getByRole('button', { name: 'إلغاء' }).click()

  await menu(page, 'إقفال السنة').click()
  await expect(page.getByRole('heading', { name: 'إقفال السنة' })).toBeVisible()
  await expect(page.getByText('جارية')).toBeVisible()
  await expectNoHorizontalScroll(page)
  await page.screenshot({ path: 'test-results/ar-year-end.png' })

  await menu(page, 'الأرصدة الافتتاحية').click()
  await expect(page.getByRole('heading', { name: 'الأرصدة الافتتاحية' })).toBeVisible()
  await expectNoHorizontalScroll(page)
  await page.screenshot({ path: 'test-results/ar-opening.png' })
})
