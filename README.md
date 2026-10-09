# Baba (بابا)

A bilingual (English / العربية) accounting-first ERP for small and medium businesses in the Middle East (Egypt, Saudi Arabia,
the UAE and Kuwait today, more countries later). Each company is one encrypted file on your computer (`My Company.baba`) that
works fully offline. The same code is designed to run later as a hosted, multi-user service.

**Status: Phases 0 to 7 are built** (foundation, accounting core, banking and sub-ledgers, sales and purchases, tax, inventory,
assets / payroll / expense claims / budgets, and users / roles / approval / audit log). The cloud edition (Phase 8) is next.
Everything below is checked by automated tests and by driving the real desktop window; what is *not* done is listed honestly
under [What is not done yet](#what-is-not-done-yet).

## Try it

1. Build the installer (see below), or ask whoever built it for `Baba-Setup-<version>.exe`.
2. Double-click it, click **Next**, **Install**, **Finish**. A **Baba** icon appears on the desktop and in the Start menu, next to
   **Uninstall Baba**. No administrator password is needed.
3. Open Baba, click **New company**, and follow the steps.

New to Baba? Read the **[user guide](docs/user-guide/README.md)** (English) or the **[الدليل بالعربية](docs/user-guide/ar/README.md)**:
install, a 30-minute practice tour, one guide for each job (owner, accountant, sales, storekeeper, viewer), routines, and what to
do when something goes wrong. It is written for people who are not used to computers.

## What it does

| Area | What you can do |
|---|---|
| **Company and files** | Create a company in Arabic or English, one password-protected file each; backups, restore, automatic copy before an update or a year-end. |
| **Accounting** | Chart of accounts, payment / receipt / journal / transfer vouchers (draft or posted), opening balances, locked months, year-end close and reopen, foreign currencies with exchange rates, cost centers and projects, recurring invoices and entries. |
| **Bank and cash** | Balances, transfers, bank reconciliation with a loaded statement. |
| **Customers and suppliers** | Statements, balances, credit limits, payment terms, aging (30 / 60 / 90+). |
| **Sales and purchases** | Quotes, orders, delivery notes, invoices, credit and debit notes with one-click conversion; receive payment / pay supplier against specific invoices with exchange gain or loss; products and price lists; printable PDFs. |
| **Tax** | Tax codes from the country pack, tax on every line, a tax return that checks itself against the ledger, tax invoices with the QR code where the country needs it. |
| **Inventory** | Warehouses, stock counts, transfers, invoices that move stock and book the cost of sales (weighted average), a valuation that agrees with the ledger, a low-stock warning. |
| **Fixed assets** | Register, monthly depreciation that posts by itself, disposal with gain or loss. |
| **Payroll and claims** | Employees, payslips with the country's social insurance, posting and paying, end-of-service provision, leave, expense claims with approval, budgets and budget versus actual. |
| **People and security** | Optional user accounts, roles with per-area permissions (five ready-made roles plus your own), approval before posting, sign-in recovery with the file password, an audit log you can read and export. |
| **Reports and Excel** | Every report and every list exports to PDF, Excel (real numbers and dates) or CSV, in Arabic, English or both; Excel and CSV import with downloadable templates, all-or-nothing. |
| **Bilingual** | Full right-to-left layout, Arabic printouts, Arabic search normalisation, optional Hijri dates, adjustable text size. |

## What is not done yet

- **E-invoicing submission** to the tax authorities (signed XML, clearance and reporting for Saudi Arabia, Egypt, the UAE) needs the
  authorities' credentials and test environments. Only the printed tax invoice and the Saudi QR code are built.
- **Payroll bank files** (WPS, Mudad), income-tax withholding, and **verified** insurance rates and end-of-service rules: the rates in
  the country packs are starting values to be checked against the law ([docs/countries/payroll.md](docs/countries/payroll.md)).
- **Stock:** first-in-first-out, batches and serials, landed cost, a barcode scanning screen.
- **Attachments** on documents and claims, a global search bar, a cash-flow statement, sales-by-customer reports, Excel import of
  price lists and invoices.
- **Release work:** code signing (the installer shows Windows' "protected your PC" warning), bundling the WebView2 installer,
  automatic updates, a test on a clean second computer.
- **Cloud edition** (hosted, many people at once) and more interface languages.

## What is in the box

- **Company files:** SQLite encrypted with SQLCipher, one password per file, locked against a second window, with an audit log of
  every change. Money is stored as scaled whole numbers (exact, no rounding drift).
- **Country packs:** everything country-specific (currency, tax numbers and codes, e-invoicing, payroll rules, calendars, chart of
  accounts) lives in `src/Baba.Localization/<Country>/`. Adding a country is one folder; see
  [docs/countries](docs/countries/README.md). Architecture tests fail if any other code names a country.
- **Desktop app:** a small WinForms window that shows the React app with WebView2, talking to a local API over HTTP (protected by a
  per-launch secret). The cloud edition will use the same API.
- **Security:** every API route has a permission rule (deny by default, a test checks every route); the program's own automatic runs
  are exempt; passwords are salted and slow-hashed; the audit log never contains a password.

## Build, run and test

Requirements: Windows 10/11, the [.NET 10 SDK](https://dotnet.microsoft.com/download), Node.js 24, and the Microsoft Edge
WebView2 runtime (part of current Windows). Browser tests use the Microsoft Edge that is already installed.

```powershell
# Backend: build and run all tests (about 700 tests; the API and infrastructure suites take several minutes)
dotnet build Baba.slnx
dotnet test Baba.slnx

# Web app (in web/)
cd web
npm ci
npm run lint        # ESLint (also checks right-to-left safe CSS) and Stylelint
npm run typecheck
npm test            # unit tests
npm run build       # produces web/dist, which the desktop app bundles
npm run e2e         # end-to-end tests in Microsoft Edge (build the web app and Baba.Api first)

# Run the desktop app (after the web build)
dotnet build src/Baba.Desktop
src\Baba.Desktop\bin\Debug\net10.0-windows\Baba.Desktop.exe
```

When the API changes, building `Baba.Api` rewrites `web/openapi/Baba.Api.json`; run `npm run api` in `web/` to regenerate the typed
client in `web/src/api/generated` and commit both together (CI fails if they are out of date).

Development with hot reload: run `npm run dev` in `web/`, then start the desktop app with
`BABA_PORT=5054` and `BABA_WEB_URL=http://localhost:5173`.

**Drive the real desktop window** (run after every phase; takes screenshots into `artifacts/real-window/`):

```powershell
node tools\real-window-check\real-window.cjs                      # with the native Windows dialogs (needs an unlocked desktop)
$env:BABA_NO_NATIVE = "1"; node tools\real-window-check\real-window.cjs   # without them
```

**Build the installer** (needs [Inno Setup 6](https://jrsoftware.org/isinfo.php); falls back to a portable zip without it). The
result is `artifacts\installer\Baba-Setup-<version>.exe`; see [docs/desktop-app-and-icon.md](docs/desktop-app-and-icon.md) for the
icon and what to do before giving it to customers:

```powershell
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
```

Database changes use EF Core migrations (`dotnet tool restore`, then
`dotnet tool run dotnet-ef migrations add <Name> --project src/Baba.Infrastructure --output-dir Persistence/Migrations`). A company
file is migrated when it is opened, after an automatic backup.

## Layout

| Folder | What |
|---|---|
| `src/Baba.Domain` | Entities, money, ledger and posting rules, security model (no framework dependencies) |
| `src/Baba.Application` | Use cases, validation, ports (accounting, trade, inventory, assets, payroll, claims, budgets, reporting, printing, importing, security) |
| `src/Baba.Infrastructure` | EF Core, the encrypted company file, stores, Excel / PDF printing, fonts |
| `src/Baba.Localization` | Country pack contract and the country packs (Egypt, Saudi Arabia, the UAE, Kuwait, and a template) |
| `src/Baba.Api` | HTTP API (OpenAPI), its security, sign-in and the permission map |
| `src/Baba.Desktop` | The desktop window, native dialogs and PDF renderer |
| `web/` | React + TypeScript + Vite + Ant Design, the generated API client, browser tests in `web/e2e` |
| `tests/` | Unit, integration, API, architecture and desktop smoke tests |
| `installer/` | Inno Setup script and build script |
| `branding/` | The Baba icon and logo |
| `tools/real-window-check/` | Drives the real desktop window end to end |
| `docs/user-guide/` | The guide for the people who use Baba, in English and Arabic |
| `docs/adr/` | Decision records (why things are the way they are), 0001 to 0012 |
| `docs/countries/` | How to add a country, the Saudi notes, and the payroll rules to verify |

## Rules the code follows

- Money is `decimal` in code and a scaled whole number in the file, never `double`; it rounds to each currency's minor units
  (2 decimals, or 3 for dinars).
- Only the posting engine writes ledger entries; invoices, payroll, depreciation and stock post through it. Balances are never
  stored; reports read from the ledger, and the reports that can check themselves do.
- Every UI string goes through i18n (English and Arabic files must match, checked by tests), and layout uses CSS logical
  properties only (checked by lint).
- No country-specific code outside its country pack (checked by architecture tests and CI).
- Every list and report must export to Excel; imports are all-or-nothing.
- Messages for people are plain sentences in their language: no codes or technical text on screen.
