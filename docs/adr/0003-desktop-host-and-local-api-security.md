# ADR 0003: Desktop host (WinForms + WebView2) and local API security

- Status: accepted
- Date: 2026-10-06

## Context

The brief (sections 2 and 4) wants the desktop app to be the same code as the future cloud service: the UI talks to
the backend only through the HTTP API, even on desktop. A local HTTP server has a risk the brief does not mention:
on a normal computer any other program, and any web page open in a browser, can send requests to `127.0.0.1`.
Without protection they could read or change the open company file.

## Decision

- **Host:** a small WinForms app (`Baba.Desktop`) with the `Microsoft.Web.WebView2` control, used directly (not Photino).
  It starts the API in-process on `127.0.0.1` with a random free port, serves the built web app from the same server
  (no CORS, no second process), and shows it in the window.
- **Same API as the cloud:** `BabaApi.Create(options, configure)` builds the host for the desktop launcher, the
  standalone dev program (`Baba.Api/Program.cs`) and the tests. Desktop-only abilities are optional services added
  by the launcher (`IFileDialogs`); the UI asks `GET /api/host` what is available.
- **Native dialogs through the API:** the web app cannot get real file paths, so open/save dialogs are an
  `IFileDialogs` service implemented with WinForms and exposed as `POST /api/dialogs/...`. The UI still only
  talks HTTP, and the cloud edition just does not register it.
- **Security, three layers** (all in `Baba.Api/Security`, all tested):
  1. **Per-launch token.** A random secret is made at each launch and stored by the host in a strict same-site,
     HTTP-only cookie. Every `/api` request must carry it (cookie or `X-Baba-Token`). Compared in constant time.
  2. **Host check.** Only `localhost`, `127.0.0.1` and `[::1]` host names are served, which stops DNS rebinding.
  3. **Origin check.** State-changing requests with an `Origin` from another site are rejected.
- **Navigation lock:** the window stays on the app's own address; links to other sites open in the default browser.
  DevTools, zoom, autofill and the default context menu are off in release (text size is an app setting).
- **Single instance:** a mutex plus a named pipe. A second launch (for example double-clicking a `.baba` file) hands the
  path to the running window and exits. The path is read once from `GET /api/startup`.
- **Smoke test:** `Baba.Desktop.exe --smoke-test result.json` starts the real window, loads an API page through
  WebView2 with the cookie, checks that a plain HTTP client without the token gets 401, writes the result and exits.
  It runs as an automated test (`tests/Baba.Desktop.Tests`).
- **Development:** `BABA_WEB_URL` points the window at the Vite dev server (its origin is allowed for changes);
  `BABA_PORT` fixes the API port so the dev server can proxy to it; `BABA_DEVTOOLS=1` turns on DevTools.

## Consequences

- Another program running as the same Windows user can still read the `.baba` file from disk, but it is encrypted
  with the company password; the token only protects the live API.
- Closing the window closes the company file and releases the lock before the process exits.
- TODO (needs the web app): warn about unsaved forms when the window is closed. The page should expose a "can close?"
  check, and the host should ask it from `FormClosing`.
- Installer and `.baba` file association are Phase 0 step P0.8.
