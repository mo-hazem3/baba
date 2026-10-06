import { defineConfig } from 'orval'

// Generates the typed API client from the OpenAPI document the backend writes at build time.
// Run `npm run api` after changing the API (build Baba.Api first so web/openapi/Baba.Api.json is current).
export default defineConfig({
  baba: {
    input: './openapi/Baba.Api.json',
    output: {
      target: './src/api/generated/baba.ts',
      schemas: './src/api/generated/model',
      client: 'react-query',
      httpClient: 'fetch',
      clean: true,
      override: {
        mutator: { path: './src/api/http.ts', name: 'http' },
      },
    },
  },
})
