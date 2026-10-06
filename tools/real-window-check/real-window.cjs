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

const exe = path.join(root, 'src', 'Baba.Desktop', 'bin', 'Debug', 'net10.0-windows', 'Baba.Desktop.exe')
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
  }, 420000)

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
    let page
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
    await expect(page.getByRole('heading', { name: 'Summary' })).toBeVisible({ timeout: 60000 })
    await expect(page.locator('.company-details')).toContainText('Real Window Trading')
    log('company created; file exists:', fs.existsSync(companyFile), '| size', fs.statSync(companyFile).size, 'bytes | window title:', await page.title())
    await page.screenshot({ path: path.join(shots, '3-summary.png') })

    // Backup through the native Save dialog.
    typeIntoDialog(backupFile)
    await page.getByRole('button', { name: 'Back up now' }).click()
    await expect(page.getByText(/Backup saved to/)).toBeVisible({ timeout: 60000 })
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

    // ---- ADD THE NEXT PHASE'S FLOWS HERE (before the close-window test) ----

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

    await browser.close().catch(() => {})
    console.log('\nREAL WINDOW FLOW: ALL STEPS PASSED')
  } finally {
    clearTimeout(watchdog)
    app.kill()
  }
}

main().catch((e) => {
  console.error('\nREAL WINDOW FLOW FAILED:', e.message.split('\n').slice(0, 6).join('\n'))
  process.exitCode = 1
})
