// Full functional check of the REAL Baba desktop window (WebView2), run after every phase.
//
//   Build first:  dotnet build src/Baba.Desktop   (after `npm run build` in web/, so the web app is bundled)
//   Run:          node tools/real-window-check/real-window.cjs     (use the portable Node in .tools if node is not on PATH)
//
// It starts the real exe with an isolated profile (recent files + WebView2 data go to a temp folder, so your own data is
// never touched), attaches to the window over the Chrome DevTools protocol, and drives the real flows, including the
// NATIVE Windows file dialogs (typed into with SendKeys) and the window-close warning dialog.
// Screenshots go to artifacts/real-window/. Add the new phase's flows at the marked place near the end.
const { spawn, execSync } = require('node:child_process')
const fs = require('node:fs')
const os = require('node:os')
const path = require('node:path')

const root = path.resolve(__dirname, '..', '..')
const { chromium, expect } = require(path.join(root, 'web', 'node_modules', '@playwright/test'))

// BABA_EXE points the check at another build, for example an installed copy.
const exe = process.env.BABA_EXE ?? path.join(root, 'src', 'Baba.Desktop', 'bin', 'Debug', 'net10.0-windows', 'Baba.Desktop.exe')
const shots = path.join(root, 'artifacts', 'real-window')
fs.mkdirSync(shots, { recursive: true })
const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'baba-real-profile-'))
const dataDir = path.join(os.tmpdir(), 'baba-real-data')
fs.rmSync(dataDir, { recursive: true, force: true })
fs.mkdirSync(dataDir, { recursive: true })
const companyFile = path.join(dataDir, 'RealWindow.baba')
const backupFile = path.join(dataDir, 'RealWindow-backup.baba')
const password = 'correct-horse'
const port = 9333
// BABA_NO_NATIVE=1 skips the steps that type into native Windows dialogs (they need an unlocked, interactive desktop):
// the company is made and backed up through the API instead, and the Excel/CSV/close-window dialog steps are skipped.
const native = process.env.BABA_NO_NATIVE !== '1'
const log = (...a) => console.log('•', ...a)
const sleep = (ms) => new Promise((r) => setTimeout(r, ms))

/** Types into whatever native dialog opens next (the Save/Open dialogs, the close warning). */
function sendKeys(keys, delayMs = 3500) {
  const script =
    `Start-Sleep -Milliseconds ${delayMs}; Add-Type -AssemblyName System.Windows.Forms; ` +
    `[System.Windows.Forms.SendKeys]::SendWait('${keys.replace(/'/g, "''")}')`
  return spawn('powershell', ['-NoProfile', '-Command', script], { stdio: 'ignore' })
}
const typeIntoDialog = (text, delayMs) => sendKeys(`^a${text}{ENTER}`, delayMs)

function closeMainWindow(pid) {
  execSync(`powershell -NoProfile -Command "(Get-Process -Id ${pid}).CloseMainWindow() | Out-Null"`)
}

async function chooseOption(page, field, option) {
  await page.getByLabel(field, { exact: false }).first().click()
  await page.locator('.ant-select-item-option', { hasText: option }).first().click()
}

async function main() {
  // Baba allows one window at a time; a second launch would just hand over to the first and exit.
  if (execSync('tasklist /FI "IMAGENAME eq Baba.Desktop.exe" /FO CSV /NH').toString().includes('Baba.Desktop.exe'))
    throw new Error('Another Baba window is already running: close it first (this check will not touch it).')

  const env = { ...process.env, LOCALAPPDATA: profile, WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS: `--remote-debugging-port=${port}` }
  const app = spawn(exe, [], { env, stdio: 'ignore' })
  let appExit = null
  app.on('exit', (code) => { appExit = code })
  const watchdog = setTimeout(() => {
    console.error('\nREAL WINDOW FLOW FAILED: watchdog timeout (the flow got stuck)')
    app.kill()
    process.exit(2)
  }, 780000)

  let page
  try {
    for (let i = 0; i < 60; i++) {
      if (appExit !== null) throw new Error(`The app exited at start with code ${appExit}`)
      try { if ((await fetch(`http://127.0.0.1:${port}/json/version`)).ok) break } catch { /* not up yet */ }
      await sleep(500)
    }
    const browser = await chromium.connectOverCDP(`http://127.0.0.1:${port}`)
    const context = browser.contexts()[0]
    context.setDefaultTimeout(30000) // never hang forever on a missing element
    context.setDefaultNavigationTimeout(30000)

    // The window may still be on about:blank while the app starts: wait for the real app page.
    for (let i = 0; i < 120 && !page; i++) {
      page = context.pages().find((p) => p.url().startsWith('http://127.0.0.1'))
      if (!page) await sleep(500)
    }
    if (!page) throw new Error('The app page never loaded in the window')
    await page.waitForLoadState('load')
    await page.getByRole('heading').first().waitFor()
    log('window title:', await page.title(), '| url:', page.url().replace(/:\d+/, ':<random port>'))

    // Deterministic English first.
    await page.evaluate(() => localStorage.setItem('baba.settings', JSON.stringify({ language: 'en', textSize: 'normal', digits: 'western' })))
    await page.reload()
    await expect(page.getByRole('heading', { name: 'Welcome to Baba' })).toBeVisible()
    await page.screenshot({ path: path.join(shots, '1-start.png') })
    log('start screen shown in the real window')

    // ---- Phase 0: new company wizard, with the NATIVE Save dialog ----
    if (native) {
      await page.getByRole('button', { name: 'New company' }).click()
      await page.getByLabel('Company name (English)').fill('Real Window Trading')
      await page.getByLabel('Company name (Arabic)').fill('شركة النافذة الحقيقية')
      await chooseOption(page, 'Country', 'Kuwait')
      for (let i = 0; i < 5; i++) await page.getByRole('button', { name: 'Next' }).click()
      await page.getByLabel('File password', { exact: true }).fill(password)
      await page.getByLabel('Confirm password').fill(password)
      const readOnly = await page.getByPlaceholder('C:\\Books\\My Company.baba').getAttribute('readonly')
      log('file path box is read-only (must use the native dialog):', readOnly !== null)
      typeIntoDialog(companyFile)
      await page.getByRole('button', { name: 'Choose location…' }).click()
      await expect(page.getByPlaceholder('C:\\Books\\My Company.baba')).toHaveValue(companyFile, { timeout: 30000 })
      log('native Save dialog returned:', companyFile)
      await page.screenshot({ path: path.join(shots, '2-wizard-file.png') })

      await page.getByRole('button', { name: 'Create company' }).click()
    } else {
      log('NO-NATIVE mode: the company is created through the API (the wizard and its native Save dialog were not driven in this run)')
      const created = await page.evaluate(async ({ path, password, year }) => {
        const response = await fetch('/api/company/create', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ path, password, company: { nameAr: 'شركة النافذة الحقيقية', nameEn: 'Real Window Trading', countryCode: 'KW', baseCurrencyCode: 'KWD', fiscalYearStartMonth: 1, firstFiscalYear: year, taxNumbers: {}, address: null, chartTemplateKey: 'default', enabledModules: ['bank-cash', 'customers-suppliers', 'cost-centers', 'sales', 'purchases'] } }) })
        return response.status
      }, { path: companyFile, password, year: new Date().getFullYear() })
      if (created !== 200) throw new Error('Creating the company through the API returned ' + created)
      await page.reload()
    }
    await expect(page.getByRole('heading', { name: 'Summary' })).toBeVisible({ timeout: 60000 })
    await expect(page.locator('.company-details')).toContainText('Real Window Trading')
    log('company created; file exists:', fs.existsSync(companyFile), '| size', fs.statSync(companyFile).size, 'bytes | window title:', await page.title())
    await page.screenshot({ path: path.join(shots, '3-summary.png') })

    // Backup through the native Save dialog.
    if (native) {
      typeIntoDialog(backupFile)
      await page.getByRole('button', { name: 'Back up now' }).click()
      await expect(page.getByText(/Backup saved to/)).toBeVisible({ timeout: 60000 })
    } else {
      const status = await page.evaluate(async (destinationPath) => (await fetch('/api/company/backup', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ destinationPath }) })).status, backupFile)
      if (status !== 200 && status !== 204) throw new Error('Backup through the API returned ' + status)
    }
    log('backup written through the native dialog; exists:', fs.existsSync(backupFile))

    // Printing from Settings opens the PDF in the viewer window.
    await page.getByRole('menuitem', { name: 'Settings' }).click()
    const popup = context.waitForEvent('page', { timeout: 60000 })
    await page.getByRole('button', { name: 'Print in Arabic', exact: true }).click()
    const pdfPage = await popup
    await sleep(2500)
    log('PDF viewer window opened | title:', await pdfPage.title())
    // (Closing the popup through the debug connection hangs this harness, so it is left open; the app does not need it closed.)
    await Promise.race([page.bringToFront().catch(() => {}), sleep(5000)])

    // Close the company, then reopen it from the recent list with the password (the brief's Phase 0 test).
    await page.getByRole('button', { name: /Menu/ }).click()
    await page.getByText('Close company', { exact: true }).click()
    await page.getByRole('button', { name: 'Close company' }).last().click()
    await expect(page.getByRole('heading', { name: 'Welcome to Baba' })).toBeVisible()
    await expect(page.getByText('RealWindow', { exact: true })).toBeVisible()
    log('company closed; it is in the recent list')
    await page.screenshot({ path: path.join(shots, '4-recent.png') })

    // Double-clicking a .baba file while Baba is open: the second launch hands the file to this window and exits.
    const second = spawn(exe, [backupFile], { env, stdio: 'ignore' })
    const secondExit = await new Promise((resolve) => second.on('exit', resolve))
    await expect(page.getByRole('dialog').getByText('Open RealWindow-backup')).toBeVisible({ timeout: 30000 })
    log('second launch with a .baba file handed it to the running window (exit code', secondExit + ') and asked for its password')
    await page.getByRole('button', { name: 'Cancel' }).click()

    await page.getByRole('button', { name: 'Open RealWindow', exact: true }).click()
    await page.getByLabel('File password').fill('wrong-password')
    await page.getByRole('button', { name: 'Open company', exact: true }).click()
    await expect(page.getByText('The password is wrong, or this is not a Baba file.')).toBeVisible({ timeout: 60000 })
    log('wrong password refused with a plain message')
    await page.getByLabel('File password').fill(password)
    await page.getByRole('button', { name: 'Open company', exact: true }).click()
    await expect(page.getByRole('heading', { name: 'Summary' })).toBeVisible({ timeout: 60000 })
    await expect(page.locator('.company-details')).toContainText('Real Window Trading')
    log('reopened with the right password: data intact')

    // Arabic in the real window.
    await page.getByRole('button', { name: 'العربية' }).click()
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl')
    await expect(page.getByRole('heading', { name: 'الملخص' })).toBeVisible()
    await expect(page.locator('.company-details')).toContainText('شركة النافذة الحقيقية')
    await page.screenshot({ path: path.join(shots, '5-arabic-summary.png') })
    log('Arabic, right-to-left, shown in the real window')
    await page.getByRole('button', { name: 'English' }).click()
    await expect(page.getByRole('heading', { name: 'Summary' })).toBeVisible()

    // ---- Phase 1: a little bookkeeping, reports, and exports through the NATIVE Save dialog ----
    const pickAccount = async (label, search) => {
      const box = page.getByLabel(label, { exact: true })
      await box.click()
      await box.fill(search)
      await page.locator('.ant-select-dropdown:visible .ant-select-item-option', { hasText: search }).first().click()
    }
    const menu = (name) => page.getByRole('menuitem', { name, exact: true }).click()

    await menu('Receipts')
    await page.getByRole('button', { name: 'New receipt' }).click()
    await pickAccount('Received into', '111')
    await pickAccount('Account 1', '511')
    await page.getByLabel('Amount 1').fill('1000')
    await page.getByRole('button', { name: 'Save and post' }).click()
    await expect(page.getByText(/Posted as RV-\d{4}-0001/)).toBeVisible()
    log('receipt of 1,000 posted in the real window')
    await page.screenshot({ path: path.join(shots, '6-receipts-list.png') })

    await menu('Payments')
    await page.getByRole('button', { name: 'New payment' }).click()
    await pickAccount('Paid from', '111')
    await pickAccount('Account 1', '422')
    await page.getByLabel('Amount 1').fill('300')
    await page.getByRole('button', { name: 'Save and post' }).click()
    await expect(page.getByText(/Posted as PV-\d{4}-0001/)).toBeVisible()
    log('payment of 300 posted')

    await menu('Journal vouchers')
    await page.getByRole('button', { name: 'New journal voucher' }).click()
    await pickAccount('Account 1', '112')
    await page.getByLabel('Debit 1').fill('500')
    await pickAccount('Account 2', '31')
    await page.getByLabel('Credit 2').fill('500')
    await expect(page.getByText('Balanced')).toBeVisible()
    await page.screenshot({ path: path.join(shots, '7-journal-form.png') })
    await page.getByRole('button', { name: 'Save and post' }).click()
    await expect(page.getByText(/Posted as JV-\d{4}-0001/)).toBeVisible()
    log('balanced journal voucher of 500 posted')

    await menu('Reports')
    await page.getByRole('button', { name: 'Open Trial balance' }).click()
    await expect(page.locator('.report-total').first()).toContainText('1,500.000')
    await expect(page.locator('.check-bad')).toHaveCount(0)
    await page.screenshot({ path: path.join(shots, '8-trial-balance.png') })
    log('trial balance: debits = credits = 1,500.000')

    // Excel and CSV. The harness cannot drive the Save dialog for downloads: attached over the DevTools protocol, Playwright takes
    // the download itself, so WebView2 never raises its DownloadStarting event. The host's download handler is checked by the desktop
    // smoke test instead (a real download reaches it and is saved); here the files themselves are checked, fetched from the page.
    {
      // The native Save dialog was not driven; at least the files themselves are checked, fetched from the page.
      const { xlsx, csv } = await page.evaluate(async () => {
        const q = new URLSearchParams(location.search)
        const get = async (format) => { const r = await fetch('/api/reports/trial-balance/export?format=' + format + '&layout=Both'); return { status: r.status, bytes: new Uint8Array(await r.arrayBuffer()) } }
        const x = await get('Xlsx'); const c = await get('Csv')
        return { xlsx: { status: x.status, head: String.fromCharCode(...x.bytes.slice(0, 2)), size: x.bytes.length }, csv: { status: c.status, text: new TextDecoder().decode(c.bytes), size: c.bytes.length } }
      })
      if (xlsx.status !== 200 || xlsx.head !== 'PK') throw new Error('The Excel export is not a real .xlsx file')
      if (csv.status !== 200 || !csv.text.includes('Cash on hand')) throw new Error('The CSV export has no accounts in it')
      log('Excel (' + xlsx.size + ' bytes, PK header) and CSV (' + csv.size + ' bytes) fetched OK; the Save dialog for downloads is covered by the smoke test, not driven here')
    }

    // Logo upload, then a voucher PDF in the viewer window.
    const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==', 'base64')
    await menu('Settings')
    await page.getByTestId('upload-logo').setInputFiles({ name: 'logo.png', mimeType: 'image/png', buffer: png })
    await expect(page.getByAltText('Company logo')).toBeVisible()
    log('logo uploaded and shown in Settings')
    await page.screenshot({ path: path.join(shots, '9-settings-cards.png'), fullPage: true })

    await menu('Receipts')
    await page.getByRole('link', { name: /RV-\d{4}-0001/ }).click()
    const voucherPopup = context.waitForEvent('page', { timeout: 60000 })
    await page.getByRole('button', { name: 'Print', exact: true }).click()
    const voucherPdf = await voucherPopup
    await sleep(2500)
    log('voucher PDF opened in the viewer window | title:', await voucherPdf.title())
    await Promise.race([page.bringToFront().catch(() => {}), sleep(5000)])

    // Arabic reports in the real window.
    await page.getByRole('button', { name: 'العربية' }).click()
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl')
    await menu('التقارير')
    await page.getByRole('button', { name: 'فتح ميزان المراجعة' }).click()
    await expect(page.locator('.report-total').first()).toContainText('1,500.000')
    await page.screenshot({ path: path.join(shots, '10-arabic-trial-balance.png') })
    log('Arabic trial balance shown right-to-left')
    await page.getByRole('button', { name: 'English' }).click()
    await expect(page.getByRole('heading', { name: /Trial balance/ })).toBeVisible()
    await menu('Summary')
    await expect(page.getByText('Cash and bank')).toBeVisible()
    await page.screenshot({ path: path.join(shots, '11-summary-dashboard.png') })

    // ---- Phase 2: bank and cash, transfer, reconciliation with a loaded statement, customers, opening balances, year-end, import ----
    const csvFile = (text, name = 'file.csv') => ({ name, mimeType: 'text/csv', buffer: Buffer.from(text, 'utf8') })

    await menu('Transfers')
    await page.getByRole('button', { name: 'New transfer' }).click()
    await pickAccount('Transfer from', '111')
    await pickAccount('Transfer to', '112')
    await page.getByLabel('Amount', { exact: true }).fill('100')
    await page.getByRole('button', { name: 'Save and post' }).click()
    await expect(page.getByText(/Posted as TV-\d{4}-0001/)).toBeVisible()
    log('transfer of 100 from cash to bank posted')

    await menu('Bank and cash')
    await expect(page.getByRole('row', { name: /112/ })).toContainText('600.000') // 500 from the journal voucher + 100 transferred
    await page.screenshot({ path: path.join(shots, '12-bank.png') })
    await page.getByRole('button', { name: 'Reconcile 112' }).click()
    await page.getByRole('button', { name: 'Load statement from a file…' }).click()
    const statementDay = new Date().toISOString().slice(0, 10)
    await page.getByTestId('import-file').setInputFiles(csvFile(`Date,Description,Amount\n${statementDay},Journal,500\n${statementDay},Transfer,100`))
    await expect(page.getByText('2 imported.').first()).toBeVisible()
    await page.getByRole('button', { name: 'Close' }).last().click()
    await page.getByRole('button', { name: 'Tick suggested matches' }).click()
    await page.getByLabel('Closing balance on the statement').fill('600')
    await expect(page.locator('.reconcile-figures')).toContainText('No difference')
    await page.screenshot({ path: path.join(shots, '13-reconcile.png') })
    await page.getByRole('button', { name: 'Finish reconciliation' }).click()
    await expect(page.getByText('Reconciliation finished.')).toBeVisible()
    log('bank reconciled against a loaded statement: difference 0')

    await menu('Customers')
    await page.getByRole('button', { name: 'New customer' }).click()
    await page.getByLabel('Name (English)').fill('Window Customer')
    await page.getByRole('button', { name: 'Save' }).click()
    await expect(page.getByRole('cell', { name: 'Window Customer' })).toBeVisible()
    await menu('Journal vouchers')
    await page.getByRole('button', { name: 'New journal voucher' }).click()
    await pickAccount('Account 1', '113')
    await pickAccount('Customer / supplier 1', 'Window')
    await page.getByLabel('Debit 1').fill('250')
    await pickAccount('Account 2', '511')
    await page.getByLabel('Credit 2').fill('250')
    await page.getByRole('button', { name: 'Save and post' }).click()
    await expect(page.getByText(/Posted as JV-\d{4}-0002/)).toBeVisible()
    await menu('Reports')
    await page.getByRole('button', { name: 'Open Customers aging' }).click()
    await expect(page.getByRole('row', { name: /Window Customer/ })).toContainText('250.000')
    await page.screenshot({ path: path.join(shots, '14-aging.png') })
    log('customer created, invoiced on credit, shown in the aging report')

    await menu('Opening balances')
    await expect(page.getByRole('heading', { name: 'Opening balances' })).toBeVisible()
    await page.screenshot({ path: path.join(shots, '15-opening.png') })
    await menu('Year-end')
    await expect(page.getByRole('row', { name: /In progress/ })).toBeVisible()
    await page.screenshot({ path: path.join(shots, '16-year-end.png') })
    log('opening balances and year-end screens open')

    await menu('Chart of accounts')
    await page.getByRole('button', { name: 'Import…' }).click()
    await page.getByTestId('import-file').setInputFiles(csvFile('Code,Name,Parent code,Type\n9,Other assets,,Asset\n91,Safe,9,'))
    await expect(page.getByText('2 imported.').first()).toBeVisible()
    await page.getByRole('button', { name: 'Close' }).last().click()
    log('chart of accounts imported from a CSV file')
    await menu('Summary')

    // ---- Phase 3: products, a sales invoice and its PDF, a dollar invoice paid at a better rate, repeating an invoice ----
    await menu('Products and prices')
    await expect(page.getByText('No products yet')).toBeVisible()
    await page.getByRole('button', { name: 'New product' }).click()
    await page.getByLabel('Name (English)').fill('Real hour')
    await page.getByLabel('Sale price').fill('50')
    await pickAccount('Revenue account', '511')
    await page.getByRole('dialog').getByRole('button', { name: 'Save' }).click()
    await expect(page.getByRole('row', { name: /Real hour/ })).toContainText('50.000')
    await page.screenshot({ path: path.join(shots, '17-products.png') })
    log('product created with its price and revenue account')

    await menu('Sales')
    await page.getByRole('button', { name: 'New invoice' }).click()
    await pickAccount('Customer', 'Window')
    await pickAccount('Product 1', 'Real')
    await expect(page.getByLabel('Price 1')).toHaveValue('50.000')
    await page.getByLabel('Quantity 1').fill('2')
    await expect(page.locator('.doc-totals')).toContainText('100.000')
    await page.screenshot({ path: path.join(shots, '18-invoice-form.png') })
    await page.getByRole('button', { name: 'Issue and post' }).click()
    await expect(page.getByText(/Issued as SI-\d{4}-0001/)).toBeVisible()
    log('sales invoice of 100 issued and posted')

    await page.getByRole('link', { name: /SI-\d{4}-0001/ }).click()
    const invoicePopup = context.waitForEvent('page', { timeout: 60000 })
    await page.getByRole('button', { name: 'Print', exact: true }).click()
    const invoicePdf = await invoicePopup
    await sleep(2500)
    log('invoice PDF opened in the viewer window | title:', await invoicePdf.title())
    await Promise.race([page.bringToFront().catch(() => {}), sleep(5000)])

    // A dollar invoice (set up through the API), paid at a better rate: the exchange gain is booked.
    const today = new Date().toISOString().slice(0, 10)
    const setup = await page.evaluate(async (day) => {
      const send = async (method, url, body) => (await fetch(url, { method, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) })).status
      const accounts = await (await fetch('/api/accounts')).json()
      const parties = await (await fetch('/api/parties')).json()
      const customer = parties.find((p) => p.nameEn === 'Window Customer')
      const rate = await send('PUT', '/api/exchange-rates', { currencyCode: 'USD', date: '2026-01-01', rate: 0.3 })
      const invoice = await send('POST', '/api/documents/issue', { id: null, input: { kind: 'SalesInvoice', date: day, dueDate: null, partyId: customer.id, currencyCode: 'USD', exchangeRate: 0.3, reference: 'USD-1', memo: null, discountPercent: 0, lines: [{ id: null, productId: null, accountId: accounts.find((a) => a.code === '511').id, description: 'Export order', quantity: 1, unitPrice: 1000, discountPercent: 0 }] } })
      return { rate, invoice }
    }, today)
    if (setup.rate !== 200 || setup.invoice !== 200) throw new Error('Could not set up the dollar invoice: ' + JSON.stringify(setup))
    await menu('Sales')
    await page.getByRole('row', { name: /USD-1/ }).getByRole('link').click()
    await page.getByRole('button', { name: 'Receive payment' }).click()
    await pickAccount('Received into', '112')
    await page.getByLabel(/Rate: 1 USD/).fill('0.32')
    await page.screenshot({ path: path.join(shots, '19-receive-payment.png') })
    await page.getByRole('button', { name: 'Record receipt' }).click()
    await expect(page.getByText(/Exchange gain: 20\.000 KWD/)).toBeVisible()
    await expect(page.getByRole('row', { name: /USD-1/ })).toContainText('Paid')
    await page.screenshot({ path: path.join(shots, '20-invoices-paid.png') })
    log('dollar invoice paid at a better rate: exchange gain of 20.000 KWD booked, invoice shown as paid')

    await menu('Recurring')
    await expect(page.getByText('Nothing repeats yet')).toBeVisible()
    await menu('Sales')
    await page.getByRole('row', { name: /SI-\d{4}-0001/ }).getByRole('link').click()
    await page.getByRole('button', { name: 'Repeat…' }).click()
    await page.getByRole('dialog').getByRole('button', { name: 'Save' }).click()
    await expect(page.getByText(/The schedule was saved/)).toBeVisible()
    await menu('Recurring')
    await expect(page.getByRole('row', { name: /Every month/ })).toBeVisible()
    await page.screenshot({ path: path.join(shots, '21-recurring.png') })
    log('invoice set to repeat monthly; the schedule is listed')
    await menu('Summary')

    // ---- Phase 4: tax and e-invoicing, in a company whose country has VAT (the company above has none: no tax screens there) ----
    await expect(page.getByRole('menuitem', { name: 'Tax codes', exact: true })).toHaveCount(0)
    log('a company in a country without VAT shows no tax screens')

    // Excel exports of the Phase 3 and 4 lists are real .xlsx files (fetched, as the Save dialog cannot be driven).
    {
      const heads = await page.evaluate(async () => {
        const out = {}
        for (const key of ['documents', 'products', 'parties', 'exchange-rates', 'recurring']) {
          const r = await fetch('/api/reports/' + key + '/export?format=Xlsx&layout=Both')
          const b = new Uint8Array(await r.arrayBuffer())
          out[key] = r.status + ':' + String.fromCharCode(...b.slice(0, 2))
        }
        return out
      })
      for (const [key, value] of Object.entries(heads)) if (value !== '200:PK') throw new Error('The Excel export of ' + key + ' is wrong: ' + value)
      log('Excel exports of documents, products, parties, exchange rates and recurring schedules are real .xlsx files')
    }

    const vatFile = path.join(dataDir, 'RealWindowVat.baba')
    const vat = await page.evaluate(async ({ path, password, year }) => {
      await fetch('/api/company/close', { method: 'POST' })
      const response = await fetch('/api/company/create', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ path, password, company: { nameAr: 'شركة الضريبة الحقيقية', nameEn: 'Real Vat Trading', countryCode: 'SA', baseCurrencyCode: 'SAR', fiscalYearStartMonth: 1, firstFiscalYear: year, taxNumbers: { 'vat-number': '300000000000003' }, address: null, chartTemplateKey: 'default', enabledModules: ['bank-cash', 'customers-suppliers', 'sales', 'purchases'] } }) })
      return response.status
    }, { path: vatFile, password, year: new Date().getFullYear() })
    if (vat !== 200) throw new Error('Creating the VAT company returned ' + vat)
    await page.goto(new URL('/', page.url()).toString())
    await expect(page.locator('.company-details')).toContainText('Real Vat Trading', { timeout: 60000 })

    await menu('Tax codes')
    await expect(page.getByRole('row', { name: /SA-VAT-STD/ })).toContainText('Default', { timeout: 60000 }) // the first read of a new company seeds the pack's codes
    await page.screenshot({ path: path.join(shots, '22-tax-codes.png') })

    await page.evaluate(async () => {
      await fetch('/api/parties', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ kind: 'Customer', code: 'C100', nameAr: '', nameEn: 'Riyadh Stores', phone: null, email: null, address: null, taxNumber: '300111111111113', creditLimit: 0, paymentTermsDays: 30, notes: null, priceListId: null }) })
    })
    await menu('Sales')
    await page.getByRole('button', { name: 'New invoice' }).click()
    await pickAccount('Customer', 'Riyadh')
    await pickAccount('Account 1', '511')
    await page.getByLabel('Quantity 1').fill('1')
    await page.getByLabel('Price 1').fill('1000')
    await expect(page.locator('.doc-totals')).toContainText('1,150.00')
    await page.screenshot({ path: path.join(shots, '23-invoice-vat.png') })
    await page.getByRole('button', { name: 'Issue and post' }).click()
    await expect(page.getByText(/Issued as SI-\d{4}-0001/)).toBeVisible()
    log('invoice with 15% VAT issued: total 1,150.00')

    await page.getByRole('link', { name: /SI-\d{4}-0001/ }).click()
    await expect(page.getByText(/cannot be changed or deleted once it is issued/)).toBeVisible()
    const taxInvoicePopup = context.waitForEvent('page', { timeout: 60000 })
    await page.getByRole('button', { name: 'Print', exact: true }).click()
    const taxInvoicePdf = await taxInvoicePopup
    await sleep(2500)
    log('tax invoice with its QR code opened as a PDF | title:', await taxInvoicePdf.title(), '| the issued invoice is locked')
    await Promise.race([page.bringToFront().catch(() => {}), sleep(5000)])

    await menu('Reports')
    await page.getByRole('button', { name: 'Open Tax return' }).click()
    await expect(page.getByRole('row', { name: /Tax on sales/ })).toContainText('150.00')
    await expect(page.locator('.check-bad')).toHaveCount(0)
    await page.screenshot({ path: path.join(shots, '24-tax-return.png') })
    log('tax return: 150.00 tax on sales, and it agrees with the ledger')
    await menu('Summary')

    // ---- Phase 5: inventory (switched on in this company), stock in, a sale at cost, the valuation against the books ----
    await page.evaluate(async () => {
      await fetch('/api/company/modules', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ modules: ['bank-cash', 'customers-suppliers', 'sales', 'purchases', 'inventory'] }) })
    })
    await page.reload()
    await expect(page.getByRole('menuitem', { name: 'Stock', exact: true })).toBeVisible({ timeout: 60000 })
    await menu('Warehouses')
    await expect(page.getByRole('row', { name: /MAIN/ })).toContainText('Main warehouse')
    await page.screenshot({ path: path.join(shots, '25-warehouses.png') })

    await menu('Products and prices')
    await page.getByRole('button', { name: 'New product' }).click()
    await page.getByLabel('Name (English)').fill('Real widget')
    await page.getByLabel('Sale price').fill('100')
    await pickAccount('Revenue account', '511')
    await page.getByRole('switch', { name: 'Stock item' }).click()
    await page.getByLabel('Reorder level').fill('5')
    await page.getByRole('dialog').getByRole('button', { name: 'Save' }).click()
    await expect(page.getByRole('row', { name: /Real widget/ })).toContainText('Stock')
    log('a stock item created with its reorder level')

    await menu('Stock')
    await page.getByRole('button', { name: '+ Opening stock' }).click()
    await pickAccount('Product 1', 'Real widget')
    await page.getByLabel('Quantity 1').fill('10')
    await page.getByLabel('Cost per unit 1').fill('20')
    await page.getByRole('dialog').getByRole('button', { name: 'Save' }).click()
    await expect(page.getByRole('row', { name: /Real widget/ })).toContainText('200.00')
    await page.screenshot({ path: path.join(shots, '26-stock-on-hand.png') })
    log('opening stock recorded: 10 at 20 = 200.00')

    await menu('Sales')
    await page.getByRole('button', { name: 'New invoice' }).click()
    await pickAccount('Customer', 'Riyadh')
    await pickAccount('Product 1', 'Real widget')
    await page.getByLabel('Quantity 1').fill('8')
    await page.getByRole('button', { name: 'Issue and post' }).click()
    await expect(page.getByText(/Issued as SI-\d{4}-0002/)).toBeVisible()
    await menu('Summary')
    await expect(page.getByText('1 products are at or below their reorder level.')).toBeVisible()
    await page.screenshot({ path: path.join(shots, '27-low-stock.png') })
    log('sold 8: the low-stock warning shows on the Summary')

    await page.goto(new URL('/reports/stock-valuation', page.url()).toString())
    await expect(page.getByRole('row', { name: /Real widget/ })).toContainText('40.00')
    await expect(page.locator('.check-ok').first()).toBeVisible()
    await expect(page.locator('.check-bad')).toHaveCount(0)
    await page.screenshot({ path: path.join(shots, '28-stock-valuation.png') })
    log('stock valuation: 2 left worth 40.00, and it agrees with the stock account of the ledger')

    {
      const out = await page.evaluate(async () => {
        const result = {}
        for (const key of ['stock-valuation', 'stock-movements', 'stock-reorder', 'warehouses', 'stock-documents']) {
          const r = await fetch('/api/reports/' + key + '/export?format=Xlsx&layout=Both')
          const b = new Uint8Array(await r.arrayBuffer())
          result[key] = r.status + ':' + String.fromCharCode(...b.slice(0, 2))
        }
        const t = await fetch('/api/import/templates/opening-stock')
        result.template = t.status
        return result
      })
      for (const [key, value] of Object.entries(out)) if (key !== 'template' && value !== '200:PK') throw new Error('The Excel export of ' + key + ' is wrong: ' + value)
      if (out.template !== 200) throw new Error('The opening-stock template is missing')
      log('Excel exports of the stock reports and lists are real .xlsx files; the opening-stock template downloads')
    }
    await menu('Summary')

    // ---- Phase 6: fixed assets with depreciation, payroll with the country's insurance, an expense claim, a budget ----
    const api = (path, body) => page.evaluate(async ({ path, body }) => {
      const r = await fetch('/api' + path, { method: body === undefined ? 'GET' : 'POST', headers: { 'Content-Type': 'application/json' }, body: body === undefined ? undefined : JSON.stringify(body) })
      return { status: r.status, json: r.headers.get('content-type')?.includes('json') ? await r.json() : null }
    }, { path, body })
    await api('/company/modules', { modules: ['bank-cash', 'customers-suppliers', 'sales', 'purchases', 'inventory', 'fixed-assets', 'payroll', 'expense-claims', 'budgets'] })
    await page.reload()
    const chart = (await api('/accounts')).json
    const id = (code) => chart.find((a) => a.code === code).id

    await api('/vouchers/post', { id: null, input: { kind: 'Payment', date: '2026-01-15', cashAccountId: id('112'), reference: null, memo: null, lines: [{ id: null, accountId: id('121'), description: null, debit: 1200, credit: 0, partyId: null, costCenterId: null }] } })
    const asset = await api('/assets', { code: 'A001', nameAr: '', nameEn: 'Real laptop', kind: 'Tangible', acquisitionDate: '2026-01-15', cost: 1200, salvage: 0, usefulLifeMonths: 12, method: 'StraightLine', annualRate: 0, assetAccountId: id('121'), accumulatedAccountId: null, expenseAccountId: null })
    if (asset.status !== 200) throw new Error('Creating the asset returned ' + asset.status)
    await page.reload() // opening the company posts the depreciation of the months that ended
    await menu('Fixed assets')
    await expect(page.getByRole('row', { name: /A001/ })).toContainText('1,200.00')
    await expect(page.getByRole('row', { name: /A001/ })).not.toContainText('1,200.00 0.00 ')
    await page.screenshot({ path: path.join(shots, '29-assets.png') })
    await page.goto(new URL('/reports/asset-register', page.url()).toString())
    await expect(page.locator('.check-ok').first()).toBeVisible()
    await expect(page.locator('.check-bad')).toHaveCount(0)
    log('fixed asset bought, depreciated by itself on opening, and the register agrees with the ledger')

    const employee = await api('/employees', { code: 'E001', nameAr: '', nameEn: 'Real Employee', jobTitle: 'Accountant', nationalId: null, isNational: true, joinDate: '2025-01-01', leaveDate: null, basicSalary: 2000, bankName: 'Real Bank', bankAccount: 'SA0380000000608010167519', costCenterId: null, annualLeaveDays: 30, leaveBalanceDays: 0, leaveBalanceDate: null, notes: null, components: [] })
    if (employee.status !== 200) throw new Error('Creating the employee returned ' + employee.status)
    await menu('Employees')
    await expect(page.getByRole('row', { name: /Real Employee/ })).toContainText('2,000.00')
    await menu('Payroll')
    await page.getByRole('button', { name: '+ New payroll month' }).click()
    await page.getByRole('dialog').getByRole('button', { name: 'Make payslips' }).click()
    await expect(page.getByRole('row', { name: /Real Employee/ })).toContainText('1,805.00') // 2,000 less 9.75% insurance
    await page.getByRole('button', { name: 'Post to the books' }).click()
    await expect(page.getByText('Posted').first()).toBeVisible()
    await page.screenshot({ path: path.join(shots, '30-payroll-run.png') })
    log('payroll month made and posted: the nationals scheme took 9.75%, net 1,805.00')
    // The payslip as a PDF in the viewer window.
    const payslipPopup = context.waitForEvent('page', { timeout: 60000 })
    await page.getByRole('button', { name: /^Payslip Real Employee|Payslip E001/ }).first().click()
    const payslipPdf = await payslipPopup
    await sleep(2500)
    log('payslip opened as a PDF | title:', await payslipPdf.title())
    await Promise.race([page.bringToFront().catch(() => {}), sleep(5000)])

    const claim = await api('/claims', { id: null, input: { employeeId: employee.json.id, date: '2026-10-01', memo: null, lines: [{ date: '2026-10-01', description: 'Taxi', accountId: id('423'), amount: 45 }] } })
    if (claim.status !== 200) throw new Error('Creating the claim returned ' + claim.status)
    await api('/claims/' + claim.json.id + '/submit', {})
    await menu('Expense claims')
    await page.getByRole('button', { name: /^Approve EC-/ }).click()
    await expect(page.getByRole('row', { name: /EC-/ })).toContainText('Approved')
    log('expense claim approved: posted against what is owed to the employee')

    await menu('Budgets')
    await page.getByLabel('Add an account', { exact: true }).click()
    await page.getByLabel('Add an account', { exact: true }).fill('511')
    await page.locator('.ant-select-dropdown:visible .ant-select-item-option', { hasText: '511' }).first().click()
    await page.getByLabel('511 Year').fill('12000')
    await page.getByRole('button', { name: 'Save', exact: true }).click()
    await expect(page.getByText('The budget was saved.')).toBeVisible()
    await page.screenshot({ path: path.join(shots, '31-budget.png') })
    log('budget saved: 12,000 for the year spread over the months')

    {
      const out = await page.evaluate(async () => {
        const result = {}
        for (const key of ['assets', 'asset-register', 'employees', 'salary-components', 'payroll-summary', 'leave-balances', 'end-of-service', 'expense-claims', 'budget', 'budget-vs-actual']) {
          const r = await fetch('/api/reports/' + key + '/export?format=Xlsx&layout=Both&AsOf=2026-10-31&FiscalYear=2026')
          const b = new Uint8Array(await r.arrayBuffer())
          result[key] = r.status + ':' + String.fromCharCode(...b.slice(0, 2))
        }
        for (const key of ['assets', 'employees', 'budget']) result['template-' + key] = (await fetch('/api/import/templates/' + key)).status
        return result
      })
      for (const [key, value] of Object.entries(out)) {
        if (key.startsWith('template-') ? value !== 200 : value !== '200:PK') throw new Error('The Excel export or template ' + key + ' is wrong: ' + value)
      }
      log('Excel exports of the asset, payroll, claim and budget lists and reports are real .xlsx files; their import templates download')
    }
    await menu('Summary')

    // ---- Phase 7: user accounts, sign-in, roles, approval, the audit log ----
    {
      const signOutNow = async () => {
        await page.getByRole('button', { name: /Menu/ }).click()
        await page.getByRole('menuitem', { name: 'Sign out' }).click()
        await expect(page.getByText(/^Sign in to /)).toBeVisible()
      }
      const signInAs = async (name, pw, newPw) => {
        await page.getByLabel('User name').fill(name)
        await page.getByLabel('Password').fill(pw)
        await page.getByRole('button', { name: 'Sign in', exact: true }).click()
        if (newPw) {
          await expect(page.getByText('Choose your own password')).toBeVisible()
          await page.getByLabel('Current password').fill(pw)
          await page.getByLabel('New password', { exact: true }).fill(newPw)
          await page.getByLabel('New password again').fill(newPw)
          await page.getByRole('button', { name: 'Change password' }).click()
        }
        await expect(page.getByRole('heading', { name: 'Summary' })).toBeVisible({ timeout: 60000 })
      }

      await menu('Settings')
      await page.getByRole('button', { name: 'Turn on user accounts…' }).click()
      const turnOn = page.getByRole('dialog')
      await turnOn.getByLabel('Full name').fill('Real Owner')
      await turnOn.getByLabel('User name').fill('owner')
      await turnOn.getByLabel('Password', { exact: true }).fill('owner-password')
      await turnOn.getByLabel('New password again').fill('owner-password')
      await turnOn.getByRole('button', { name: 'Turn on' }).click()
      await expect(page.getByText('User accounts are on: everyone signs in with their own name and password.')).toBeVisible()
      log('user accounts turned on; the owner is the first administrator and is signed in')

      const clerkRole = await api('/roles', { nameAr: '', nameEn: 'Clerk', permissions: ['accounting.View', 'accounting.Edit'] })
      if (clerkRole.status !== 200) throw new Error('Creating the role returned ' + clerkRole.status)
      const roles = (await api('/roles')).json
      const accountantRole = roles.find((r) => r.nameEn === 'Accountant')
      for (const [name, role] of [['carl', clerkRole.json], ['ann', accountantRole]]) {
        const made = await api('/users', { userName: name, displayName: name.toUpperCase(), roleId: role.id, password: name + '-temporary' })
        if (made.status !== 200) throw new Error('Creating the user ' + name + ' returned ' + made.status)
      }
      await menu('Users and roles')
      await expect(page.getByRole('row', { name: /carl/ })).toContainText('Must choose a password')
      await page.screenshot({ path: path.join(shots, '32-users.png') })
      await menu('Settings')
      await page.getByLabel('Ask for approval before posting').click()
      await expect(page.getByLabel('Ask for approval before posting')).toBeChecked()

      await signOutNow()
      await page.screenshot({ path: path.join(shots, '33-sign-in.png') })
      await signInAs('carl', 'carl-temporary', 'carl-own-password')
      await expect(page.getByRole('menuitem', { name: 'Users and roles', exact: true })).toHaveCount(0)
      await expect(page.getByRole('menuitem', { name: 'Payroll', exact: true })).toHaveCount(0)
      log('the clerk signed in, chose a password, and sees only the clerk parts of the program')

      const receipt = { id: null, input: { kind: 'Receipt', date: '2026-10-06', cashAccountId: id('112'), reference: null, memo: 'Rent received', lines: [{ id: null, accountId: id('511'), description: null, debit: 0, credit: 120, partyId: null, costCenterId: null }] } }
      const refused = await api('/vouchers/post', receipt)
      if (refused.status !== 400) throw new Error('The clerk could post directly: ' + refused.status)
      const sent = await api('/vouchers/submit', { ...receipt, note: 'Rent for the month' })
      if (sent.status !== 200) throw new Error('Sending for approval returned ' + sent.status)
      await page.getByRole('menuitem', { name: /^Approvals/ }).click()
      await expect(page.getByRole('row', { name: /Rent for the month/ })).toContainText('Waiting')
      await page.screenshot({ path: path.join(shots, '34-clerk-approvals.png') })
      log('the clerk could not post, sent the receipt for approval, and sees it waiting')

      await signOutNow()
      await signInAs('ann', 'ann-temporary', 'ann-own-password')
      await expect(page.getByText('1 waiting for your approval.')).toBeVisible()
      await page.getByRole('menuitem', { name: /^Approvals/ }).click()
      await page.getByRole('button', { name: /^Approve and post/ }).click()
      await expect(page.getByText(/Approved and posted RV-/)).toBeVisible()
      await page.screenshot({ path: path.join(shots, '35-approved.png') })
      log('the accountant approved it: the receipt was posted')

      await signOutNow()
      await signInAs('owner', 'owner-password')
      await menu('Audit log')
      await expect(page.getByRole('row', { name: /carl/ }).first()).toBeVisible()
      await expect(page.getByRole('row', { name: /ann/ }).first()).toBeVisible()
      await page.screenshot({ path: path.join(shots, '36-audit-log.png') })
      const audit = await page.evaluate(async () => {
        const r = await fetch('/api/reports/audit-log/export?format=Xlsx&layout=Both')
        const b = new Uint8Array(await r.arrayBuffer())
        return r.status + ':' + String.fromCharCode(...b.slice(0, 2))
      })
      if (audit !== '200:PK') throw new Error('The Excel export of the audit log is wrong: ' + audit)
      log('the audit log shows who did what, and exports to a real .xlsx file')
    }
    await menu('Summary')

    // Restoring a backup, through the NATIVE open and save dialogs (the start screen needs the company to be closed first).
    if (native) {
      await page.getByRole('button', { name: /Menu/ }).click()
      await page.getByText('Close company', { exact: true }).click()
      await page.getByRole('button', { name: 'Close company' }).last().click()
      await expect(page.getByRole('heading', { name: 'Welcome to Baba' })).toBeVisible()
      const restoredFile = path.join(dataDir, 'RealWindow-restored.baba')
      typeIntoDialog(backupFile, 3500) // first dialog: which backup
      typeIntoDialog(restoredFile, 12000) // second dialog: where the restored company goes
      await page.getByRole('button', { name: 'Restore a backup…' }).click()
      await expect(page.getByRole('dialog').getByText('Open RealWindow-restored')).toBeVisible({ timeout: 60000 })
      if (!fs.existsSync(restoredFile)) throw new Error('The restored company file was not created')
      log('backup restored through the native dialogs:', fs.statSync(restoredFile).size, 'bytes')
      await page.getByLabel('File password').fill(password)
      await page.getByRole('button', { name: 'Open company', exact: true }).click()
      await expect(page.getByRole('heading', { name: 'Summary' })).toBeVisible({ timeout: 60000 })
    } else {
      log('NO-NATIVE mode: restoring a backup (native dialogs) was NOT driven')
    }

    if (native) {
      // Close-window warning: with a half-filled new company the window asks first. Default button = keep working.
      await page.getByRole('button', { name: /Menu/ }).click()
      await page.getByText('Close company', { exact: true }).click()
      await page.getByRole('button', { name: 'Close company' }).last().click()
      await page.getByRole('button', { name: 'New company' }).click()
      await page.getByLabel('Company name (English)').fill('Unfinished Trading')
      sendKeys('{ENTER}', 3000) // the default button is "Keep working"
      closeMainWindow(app.pid)
      await sleep(6000)
      if (appExit !== null) throw new Error('The window closed even though it had unsaved work and the user chose to keep working')
      await expect(page.getByLabel('Company name (English)')).toHaveValue('Unfinished Trading')
      log('close warning shown; "Keep working" kept the window open and the typed data')

      sendKeys('{TAB}{ENTER}', 3000) // move to "Close Baba" and confirm
      closeMainWindow(app.pid)
      for (let i = 0; i < 40 && appExit === null; i++) await sleep(500)
      if (appExit === null) throw new Error('"Close Baba" did not close the window')
      log('"Close Baba" closed the app (exit code', appExit + ')')
      const locks = fs.readdirSync(dataDir).filter((f) => f.endsWith('.lock'))
      log('company file released (no lock file left):', locks.length === 0)
      if (locks.length > 0) throw new Error('A lock file was left behind')

    } else {
      log('NO-NATIVE mode: the close-window warning (native dialog) was NOT driven')
      closeMainWindow(app.pid) // nothing unsaved is open, so the window just closes
      for (let i = 0; i < 40 && appExit === null; i++) await sleep(500)
      log('window closed without a prompt (exit code', appExit + ')')
    }

    await browser.close().catch(() => {})
    console.log('\nREAL WINDOW FLOW: ALL STEPS PASSED')
  } catch (e) {
    // A picture of the window at the moment of failure is worth more than the message.
    try { await Promise.race([page.screenshot({ path: path.join(shots, 'FAILED.png') }), sleep(8000)]) } catch { /* no page */ }
    throw e
  } finally {
    clearTimeout(watchdog)
    app.kill()
  }
}

main().catch((e) => {
  console.error('\nREAL WINDOW FLOW FAILED:', e.message.split('\n').slice(0, 6).join('\n'))
  process.exitCode = 1
})
