import os from 'node:os'
import path from 'node:path'
import { defineConfig } from '@playwright/test'

// End-to-end tests drive the real app: the built web files served by the real API, with a throw-away
// data folder. Build first: `npm run build`, and build the API: `dotnet build ../src/Baba.Api`.
const port = 5055
export const dataDir = path.join(os.tmpdir(), 'baba-e2e')

export default defineConfig({
  testDir: './e2e',
  outputDir: 'test-results',
  fullyParallel: false,
  workers: 1,
  timeout: 90_000,
  expect: { timeout: 15_000 },
  reporter: [['list']],
  globalSetup: './e2e/global-setup.ts',
  use: {
    baseURL: `http://127.0.0.1:${port}`,
    // Uses the Microsoft Edge that is already installed (the same engine as WebView2), so nothing is downloaded.
    channel: 'msedge',
    viewport: { width: 1366, height: 768 }, // the common office laptop size (brief section 7.6)
    trace: 'retain-on-failure',
  },
  webServer: {
    command: 'dotnet run --project ../src/Baba.Api --no-launch-profile',
    url: `http://127.0.0.1:${port}/api/host`,
    reuseExistingServer: false,
    timeout: 180_000,
    env: {
      BABA_PORT: String(port),
      BABA_WEBROOT: path.resolve('dist'),
      BABA_RECENT_FILES: path.join(dataDir, 'recent.json'),
      ASPNETCORE_ENVIRONMENT: 'Production',
    },
  },
})
