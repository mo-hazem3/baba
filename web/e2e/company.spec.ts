import { expect, test, type Page } from '@playwright/test'
import { chooseOption, companyFile, expectNoHorizontalScroll, password, setLanguage } from './helpers'

/** Walks the new-company wizard to the end for Kuwait (no tax numbers needed). */
async function createKuwaitCompany(page: Page, file: string, labels: Record<string, string>) {
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

// The server is shared by all tests, so every test starts with no company open.
test.beforeEach(async ({ request }) => {
  await request.post('/api/company/close')
})

const en = {
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

test('English: create a company, close it, and reopen it with the password', async ({ page }) => {
  const file = companyFile('english-company')
  await page.goto('/')
  await setLanguage(page, 'en')

  await expect(page.getByRole('heading', { name: 'Welcome to Baba' })).toBeVisible()
  await expect(page.getByText('No companies yet')).toBeVisible()
  await expectNoHorizontalScroll(page)
  await page.screenshot({ path: 'test-results/en-start.png' })

  await createKuwaitCompany(page, file, en)
  await page.screenshot({ path: 'test-results/en-wizard-file.png' })
  await page.getByRole('button', { name: en.create }).click()

  await expect(page.getByRole('heading', { name: 'Summary' })).toBeVisible()
  await expect(page.getByText('Your company is ready')).toBeVisible()
  await expect(page.locator('.company-details')).toContainText('Al Noor Trading')
  await expect(page.locator('.company-details')).toContainText('KWD')
  await expectNoHorizontalScroll(page)
  await page.screenshot({ path: 'test-results/en-summary.png' })
  await expect(page).toHaveTitle('Al Noor Trading · Baba')

  // Close the company from the menu.
  await page.getByRole('button', { name: /Menu/ }).click()
  await page.getByText('Close company', { exact: true }).click()
  await page.getByRole('button', { name: 'Close company' }).last().click()
  await expect(page.getByRole('heading', { name: 'Welcome to Baba' })).toBeVisible()

  // It is now in the recent list; a wrong password is explained, the right one opens it.
  await expect(page.getByText('english-company', { exact: true })).toBeVisible()
  await page.getByRole('button', { name: 'Open english-company' }).click()
  await page.getByLabel('File password').fill('wrong-password')
  await page.getByRole('button', { name: 'Open company', exact: true }).click()
  await expect(page.getByText('The password is wrong, or this is not a Baba file.')).toBeVisible()
  await page.screenshot({ path: 'test-results/en-wrong-password.png' })

  await page.getByLabel('File password').fill(password)
  await page.getByRole('button', { name: 'Open company', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Summary' })).toBeVisible()
  await expect(page.locator('.company-details')).toContainText('Al Noor Trading')
})

test('Arabic: the whole app mirrors to right-to-left and nothing needs sideways scrolling', async ({ page }) => {
  const ar = {
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
  const file = companyFile('arabic-company')
  await page.goto('/')
  await setLanguage(page, 'ar')

  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl')
  await expect(page.getByRole('heading', { name: 'مرحبًا بك في بابا' })).toBeVisible()
  await expectNoHorizontalScroll(page)
  await page.screenshot({ path: 'test-results/ar-start.png' })

  await createKuwaitCompany(page, file, ar)
  await page.screenshot({ path: 'test-results/ar-wizard-file.png' })
  await page.getByRole('button', { name: ar.create }).click()

  await expect(page.getByRole('heading', { name: 'الملخص' })).toBeVisible()
  await expect(page.locator('.company-details')).toContainText('شركة النور للتجارة')
  await expectNoHorizontalScroll(page)
  await page.screenshot({ path: 'test-results/ar-summary.png' })

  // Settings has the print test. This browser-only setup cannot make PDFs, and says so in plain words.
  await page.getByRole('menuitem', { name: 'الإعدادات' }).click()
  await expect(page.getByRole('heading', { name: 'الإعدادات' })).toBeVisible()
  await page.getByRole('button', { name: 'طباعة بالعربية', exact: true }).click()
  await expect(page.getByText('الطباعة إلى ملف PDF متاحة في تطبيق بابا لسطح المكتب.')).toBeVisible()
  await page.screenshot({ path: 'test-results/ar-settings.png' })
  await page.getByRole('menuitem', { name: 'الملخص' }).click()

  // The sidebar is on the right in Arabic.
  const sider = await page.locator('.app-sider').boundingBox()
  const content = await page.locator('.app-content').boundingBox()
  expect(sider!.x).toBeGreaterThan(content!.x)
})

test('Extra-large text still fits a 1366x768 screen in both languages', async ({ page }) => {
  await page.goto('/')
  for (const language of ['en', 'ar'] as const) {
    await page.evaluate(
      (value) => localStorage.setItem('baba.settings', JSON.stringify({ language: value, textSize: 'xlarge', digits: 'western' })),
      language,
    )
    await page.reload()
    await expect(page.locator('html')).toHaveAttribute('data-text-size', 'xlarge')
    await page.getByRole('button').first().waitFor()
    await expectNoHorizontalScroll(page)
    await page.screenshot({ path: `test-results/${language}-start-xlarge.png` })

    await page.goto('/new-company')
    await expectNoHorizontalScroll(page)
    await page.screenshot({ path: `test-results/${language}-wizard-xlarge.png` })
    await page.goto('/')
  }
})

test('Closing the window with a half-filled new company asks first, in the user language', async ({ page }) => {
  await page.goto('/')
  await setLanguage(page, 'en')
  await page.getByRole('button', { name: 'New company' }).click()

  // Nothing typed yet: nothing to lose.
  expect(await page.evaluate(() => window.__babaUnsaved?.() ?? null)).toBeNull()

  await page.getByLabel('Company name (English)').fill('Half Done Trading')
  const prompt = await page.evaluate(() => window.__babaUnsaved?.() ?? null)
  expect(prompt).toMatchObject({ title: 'Leave without saving?', leave: 'Close Baba', stay: 'Keep working', rtl: false })

  // Switching language reloads the page, which stays on the wizard (and starts it again, empty).
  await setLanguage(page, 'ar')
  await page.getByLabel('اسم الشركة (بالإنجليزية)').fill('Half Done Trading')
  expect(await page.evaluate(() => window.__babaUnsaved?.()?.rtl)).toBe(true)

  // Cancelling the wizard leaves nothing pending.
  await page.getByRole('button', { name: 'إلغاء' }).click()
  expect(await page.evaluate(() => window.__babaUnsaved?.() ?? null)).toBeNull()
})

test('A country that requires a registration number checks its format', async ({ page }) => {
  await page.goto('/')
  await setLanguage(page, 'en')
  await page.getByRole('button', { name: 'New company' }).click()
  await page.getByLabel('Company name (English)').fill('Riyadh Supplies')
  await chooseOption(page, 'Country', 'Saudi Arabia')
  await page.getByRole('button', { name: 'Next' }).click()

  await page.getByLabel(/VAT registration number/).fill('12345')
  await page.getByRole('button', { name: 'Next' }).click()
  await expect(page.getByText('This number is not in the right format.')).toBeVisible()
  await page.screenshot({ path: 'test-results/en-tax-invalid.png' })

  await page.getByLabel(/VAT registration number/).fill('300123456789003')
  await page.getByRole('button', { name: 'Next' }).click()
  await expect(page.getByLabel('Address')).toBeVisible()
})
