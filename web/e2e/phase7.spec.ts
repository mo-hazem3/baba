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
  for (const file of recent.filter((r) => r.path.includes('p7-')))
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

const menu = (page: Page, name: string) => page.getByRole('menuitem', { name, exact: true })

async function accountIds(request: APIRequestContext) {
  const accounts = (await (await request.get('/api/accounts')).json()) as { id: string; code: string }[]
  return (code: string) => accounts.find((a) => a.code === code)!.id
}

async function signOut(page: Page) {
  await page.getByRole('button', { name: 'Menu' }).click()
  await page.getByRole('menuitem', { name: 'Sign out' }).click()
}

test('English: user accounts are turned on, a clerk sends a receipt for approval and the accountant approves it', async ({ page, request }) => {
  await page.goto('/')
  await setLanguage(page, 'en')
  await createKuwaitCompany(page, companyFile('p7-people'), wizardEn)
  await page.getByRole('button', { name: wizardEn.create }).click()
  await expect(page.getByRole('heading', { name: 'Summary' })).toBeVisible()

  // Everyone can do everything until accounts are turned on, from Settings.
  await menu(page, 'Settings').click()
  await expect(page.getByText('User accounts are off: everyone who opens the company can do everything.')).toBeVisible()
  await page.getByRole('button', { name: 'Turn on user accounts…' }).click()
  const turnOn = page.getByRole('dialog')
  await turnOn.getByLabel('Full name').fill('The Owner')
  await turnOn.getByLabel('User name').fill('owner')
  await turnOn.getByLabel('Password', { exact: true }).fill('owner-password')
  await turnOn.getByLabel('New password again').fill('owner-password')
  await page.screenshot({ path: 'test-results/en-turn-on.png' })
  await turnOn.getByRole('button', { name: 'Turn on' }).click()
  await expect(page.getByText('User accounts are on: everyone signs in with their own name and password.')).toBeVisible()

  // A role of our own, ticking only "add and change" for accounts and vouchers (which also ticks "see").
  await menu(page, 'Users and roles').click()
  await page.getByRole('tab', { name: 'Roles' }).click()
  await expect(page.getByRole('row', { name: /Administrator/ })).toContainText('Built in')
  await page.getByRole('button', { name: '+ New role' }).click()
  const roleDialog = page.getByRole('dialog')
  await roleDialog.getByLabel('Name (English)').fill('Clerk')
  await roleDialog.getByLabel('Accounts, vouchers, periods: Add and change').check()
  await expect(roleDialog.getByLabel('Accounts, vouchers, periods: See')).toBeChecked()
  await page.screenshot({ path: 'test-results/en-role-form.png' })
  await roleDialog.getByRole('button', { name: 'Save' }).click()
  await expect(page.getByRole('row', { name: /Clerk/ })).toBeVisible()

  // Two people: a clerk and an accountant (the built-in accountant may approve).
  await page.getByRole('tab', { name: 'Users' }).click()
  for (const [full, name, role] of [['Carl Clerk', 'carl', 'Clerk'], ['Ann Accountant', 'ann', 'Accountant']] as const) {
    await page.getByRole('button', { name: '+ New user' }).click()
    const dialog = page.getByRole('dialog')
    await dialog.getByLabel('Full name').fill(full)
    await dialog.getByLabel('User name').fill(name)
    await dialog.getByLabel('Role').click()
    await page.locator('.ant-select-item-option', { hasText: new RegExp(`^${role}$`) }).first().click()
    await dialog.getByLabel('Temporary password').fill(`${name}-temporary`)
    await dialog.getByRole('button', { name: 'Save' }).click()
    await expect(page.getByRole('row', { name: new RegExp(full) })).toContainText('Must choose a password')
  }
  await page.screenshot({ path: 'test-results/en-users.png' })

  // Posting needs approval from now on.
  await menu(page, 'Settings').click()
  await page.getByLabel('Ask for approval before posting').click()
  await expect(page.getByLabel('Ask for approval before posting')).toBeChecked()

  // The clerk signs in, is made to choose a password, and sees only the clerk's parts of the program.
  await signOut(page)
  await expect(page.getByText('Sign in to Al Noor Trading')).toBeVisible()
  await expect(page.getByRole('menuitem', { name: 'Settings' })).toHaveCount(0) // nothing but the sign-in until someone has
  await page.getByLabel('User name').fill('carl')
  await page.getByLabel('Password').fill('wrong password')
  await page.getByRole('button', { name: 'Sign in', exact: true }).click()
  await expect(page.getByText('The user name or password is not right. Try again.')).toBeVisible()
  await page.screenshot({ path: 'test-results/en-sign-in.png' })
  await page.getByLabel('Password').fill('carl-temporary')
  await page.getByRole('button', { name: 'Sign in', exact: true }).click()
  await expect(page.getByText('Choose your own password')).toBeVisible()
  await page.getByLabel('Current password').fill('carl-temporary')
  await page.getByLabel('New password', { exact: true }).fill('carl-own-password')
  await page.getByLabel('New password again').fill('carl-own-password')
  await page.getByRole('button', { name: 'Change password' }).click()
  await expect(page.getByRole('heading', { name: 'Summary' })).toBeVisible()
  await expect(menu(page, 'Receipts')).toBeVisible()
  await expect(menu(page, 'Users and roles')).toHaveCount(0)
  await expect(menu(page, 'Reports')).toHaveCount(0)
  await expect(menu(page, 'Payroll')).toHaveCount(0)
  await page.screenshot({ path: 'test-results/en-clerk-summary.png' })

  // The clerk's receipt form offers "Send for approval" and cannot post.
  const id = await accountIds(request)
  const receipt = {
    id: null,
    input: { kind: 'Receipt', date: '2026-10-06', cashAccountId: id('112'), reference: null, memo: 'Rent received', lines: [{ id: null, accountId: id('511'), description: null, debit: 0, credit: 120, partyId: null, costCenterId: null }] },
  }
  const refused = await request.post('/api/vouchers/post', { data: receipt })
  expect(refused.status()).toBe(400)
  await menu(page, 'Receipts').click()
  await page.getByRole('button', { name: '+ New receipt' }).click()
  await expect(page.getByRole('button', { name: 'Send for approval' })).toBeVisible()
  await expect(page.getByText('In this company a voucher or invoice is posted by someone who can approve.')).toBeVisible()
  await page.screenshot({ path: 'test-results/en-clerk-form.png' })
  const sent = await request.post('/api/vouchers/submit', { data: { ...receipt, note: 'Rent for October' } })
  expect(sent.status()).toBe(200)
  await page.getByRole('menuitem', { name: /^Approvals/ }).click()
  await expect(page.getByRole('row', { name: /Rent for October/ })).toContainText('Waiting')
  await expect(page.getByRole('button', { name: /^Approve and post/ })).toHaveCount(0) // the clerk may not approve
  await page.screenshot({ path: 'test-results/en-clerk-approvals.png' })

  // The accountant sees it waiting, approves it, and it is posted.
  await signOut(page)
  await page.getByLabel('User name').fill('ann')
  await page.getByLabel('Password').fill('ann-temporary')
  await page.getByRole('button', { name: 'Sign in', exact: true }).click()
  await page.getByLabel('Current password').fill('ann-temporary')
  await page.getByLabel('New password', { exact: true }).fill('ann-own-password')
  await page.getByLabel('New password again').fill('ann-own-password')
  await page.getByRole('button', { name: 'Change password' }).click()
  await expect(page.getByText('1 waiting for your approval.')).toBeVisible()
  await page.getByRole('menuitem', { name: /^Approvals/ }).click()
  await page.screenshot({ path: 'test-results/en-approvals.png' })
  await page.getByRole('button', { name: /^Approve and post/ }).click()
  await expect(page.getByText(/Approved and posted RV-/)).toBeVisible()
  await expect(page.getByText('Nothing is waiting')).toBeVisible() // decided: it leaves the approver's list (the clerk still sees it, as approved)
  const vouchers = (await (await request.get('/api/vouchers?status=Posted')).json()) as { number: string; memo: string | null }[]
  expect(vouchers.some((v) => v.number.startsWith('RV-'))).toBe(true)

  // The audit log shows who did what, with old and new values.
  await expect(menu(page, 'Audit log')).toHaveCount(0) // the accountant's role does not include the audit log
  await signOut(page)
  await page.getByLabel('User name').fill('owner')
  await page.getByLabel('Password').fill('owner-password')
  await page.getByRole('button', { name: 'Sign in', exact: true }).click()
  await menu(page, 'Audit log').click()
  await expect(page.getByRole('heading', { name: 'Audit log' })).toBeVisible()
  await expect(page.getByRole('row', { name: /carl/ }).first()).toBeVisible()
  await expect(page.getByRole('row', { name: /ann/ }).first()).toBeVisible()
  await expect(page.locator('body')).not.toContainText('PasswordHash')
  await page.getByRole('row', { name: /ann.*User/ }).first().locator('.ant-table-row-expand-icon').click()
  await expect(page.locator('.audit-changes').first()).toBeVisible()
  await expectNoHorizontalScroll(page)
  await page.screenshot({ path: 'test-results/en-audit-log.png' })
})

test('English: a forgotten administrator password is replaced with the company file password', async ({ page, request }) => {
  await page.goto('/')
  await setLanguage(page, 'en')
  await createKuwaitCompany(page, companyFile('p7-recover'), wizardEn)
  await page.getByRole('button', { name: wizardEn.create }).click()
  await expect(page.getByRole('heading', { name: 'Summary' })).toBeVisible()
  const turnedOn = await request.post('/api/session/turn-on', { data: { userName: 'owner', displayName: 'The Owner', password: 'forgotten-password' } })
  expect(turnedOn.status()).toBe(200)
  await request.post('/api/session/sign-out')
  await page.reload()

  await expect(page.getByText('Sign in to Al Noor Trading')).toBeVisible()
  await page.getByRole('button', { name: 'I forgot the password' }).click()
  await page.getByLabel('Company file password').fill('wrong file password')
  await page.getByLabel('Administrator user name').fill('owner')
  await page.getByLabel('Full name').fill('The Owner')
  await page.getByLabel('New password').fill('brand-new-password')
  await page.getByRole('button', { name: 'Set the new password' }).click()
  await expect(page.getByText('That is not the password of this company file.')).toBeVisible()
  await page.screenshot({ path: 'test-results/en-recover.png' })

  await page.getByLabel('Company file password').fill('correct-horse')
  await page.getByRole('button', { name: 'Set the new password' }).click()
  await expect(page.getByRole('heading', { name: 'Summary' })).toBeVisible()
})

test('Arabic: sign-in, users and the audit log read right-to-left', async ({ page, request }) => {
  await page.goto('/')
  await setLanguage(page, 'ar')
  await createKuwaitCompany(page, companyFile('p7-people-ar'), wizardAr)
  await page.getByRole('button', { name: wizardAr.create }).click()
  await expect(page.getByRole('heading', { name: 'الملخص' })).toBeVisible()
  await request.post('/api/session/turn-on', { data: { userName: 'owner', displayName: 'المالك', password: 'owner-password' } })
  await page.reload()

  await menu(page, 'المستخدمون والأدوار').click()
  await expect(page.getByRole('heading', { name: 'المستخدمون والأدوار' })).toBeVisible()
  await expect(page.getByRole('row', { name: /owner/ })).toBeVisible()
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl')
  await expectNoHorizontalScroll(page)
  await page.screenshot({ path: 'test-results/ar-users.png' })

  await page.getByRole('tab', { name: 'الأدوار' }).click()
  await page.getByRole('button', { name: '+ دور جديد' }).click()
  await expect(page.getByRole('dialog')).toContainText('الحسابات والسندات والفترات')
  await page.screenshot({ path: 'test-results/ar-role-form.png' })
  await page.getByRole('dialog').getByRole('button', { name: 'إلغاء' }).click()

  await menu(page, 'سجل المراجعة').click()
  await expect(page.getByRole('heading', { name: 'سجل المراجعة' })).toBeVisible()
  await expect(page.getByRole('row', { name: /owner/ }).first()).toBeVisible()
  await expectNoHorizontalScroll(page)
  await page.screenshot({ path: 'test-results/ar-audit-log.png' })

  await request.post('/api/session/sign-out')
  await page.reload()
  await expect(page.getByText('تسجيل الدخول إلى')).toBeVisible()
  await expectNoHorizontalScroll(page)
  await page.screenshot({ path: 'test-results/ar-sign-in.png' })
})
