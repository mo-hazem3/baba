import '@fontsource/noto-sans-arabic/arabic-400.css'
import '@fontsource/noto-sans-arabic/arabic-600.css'
import '@fontsource/noto-sans-arabic/arabic-700.css'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router'
import { App } from './App'
import { AppProviders } from './app/AppProviders'
import './i18n'
import './styles.css'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <AppProviders>
      <BrowserRouter>
        <App />
      </BrowserRouter>
    </AppProviders>
  </StrictMode>,
)
