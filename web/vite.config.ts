/// <reference types="vitest/config" />
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// During development the app runs on Vite and talks to the API through this proxy.
// Start the desktop host (or `dotnet run --project src/Baba.Api`) with BABA_PORT set to the same port.
const apiPort = process.env.BABA_PORT ?? '5054'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    strictPort: true,
    proxy: { '/api': { target: `http://127.0.0.1:${apiPort}` } },
  },
  build: { outDir: 'dist', sourcemap: false },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.test.{ts,tsx}'],
  },
})
