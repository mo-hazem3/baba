# Baba (بابا)

A bilingual (English / العربية) accounting-first ERP for small and medium businesses in the Middle East.
Each company is one encrypted file on your computer (`My Company.baba`) that works fully offline. The same code is
designed to run later as a hosted, multi-user service.

**Status: Phase 0 (foundation).** You can create a company in Arabic or English, close it, reopen it with its password,
switch language, text size and number style, and print a test page in Arabic, English or both. Accounting screens come next.

## What is in the box

- **Company files:** SQLite encrypted with SQLCipher, one password per file, locked against a second window, with
  backup and an audit log of every change.
- **Truly bilingual:** full right-to-left layout, Arabic printouts, bilingual master data, Arabic search normalisation.
- **Country packs:** everything country-specific (currency, tax numbers, calendars, chart of accounts) lives in
  `src/Baba.Localization/<Country>/`. Adding a country is one folder; see [docs/countries](docs/countries/README.md).
  Architecture tests fail if any other code names a country.
- **Desktop app:** a small WinForms window that shows the React app with WebView2, talking to a local API over HTTP
  (protected by a per-launch secret). The cloud edition will use the same API.

## Build, run and test

Requirements: Windows 10/11, the [.NET 10 SDK](https://dotnet.microsoft.com/download), Node.js 24, and the Microsoft Edge
WebView2 runtime (part of current Windows).

```powershell
# Backend: build and run all tests
dotnet build Baba.slnx
dotnet test Baba.slnx

# Web app (in web/)
cd web
npm ci
npm run lint
npm test            # unit tests
npm run build       # produces web/dist, which the desktop app bundles
npm run e2e         # end-to-end tests in Microsoft Edge (build the web app and Baba.Api first)

# Run the desktop app (after the web build)
dotnet build src/Baba.Desktop
src\Baba.Desktop\bin\Debug\net10.0-windows\Baba.Desktop.exe
```

Development with hot reload: run `npm run dev` in `web/`, then start the desktop app with
`BABA_PORT=5054` and `BABA_WEB_URL=http://localhost:5173`.

Build the installer (needs [Inno Setup 6](https://jrsoftware.org/isinfo.php); falls back to a portable zip without it):

```powershell
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
```

## Layout

| Folder | What |
|---|---|
| `src/Baba.Domain` | Entities, money, ledger rules (no framework dependencies) |
| `src/Baba.Application` | Use cases, validation, ports |
| `src/Baba.Infrastructure` | EF Core, the encrypted company file, printing fonts |
| `src/Baba.Localization` | Country pack contract and the country packs |
| `src/Baba.Api` | HTTP API (OpenAPI) and its security |
| `src/Baba.Desktop` | The desktop window and PDF renderer |
| `web/` | React + TypeScript + Vite + Ant Design |
| `tests/` | Unit, API, architecture and desktop smoke tests |
| `installer/` | Inno Setup script and build script |
| `docs/adr/` | Decision records (why things are the way they are) |

## Rules the code follows

- Money is `decimal`, never `double`, and rounds to each currency's minor units (2 decimals, or 3 for dinars).
- Balances are never stored; reports read from the ledger.
- Every UI string goes through i18n, and layout uses CSS logical properties only (checked by lint).
- No country-specific code outside its country pack (checked by architecture tests and CI).
