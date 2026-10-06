import { useEffect } from 'react'
import { useTranslation } from 'react-i18next'

/**
 * Never lose work (brief section 7.4): while a form has unsaved changes, closing the window or the page asks first.
 * The desktop window cannot rely on the browser's own prompt, so it asks the page through `window.__babaUnsaved`.
 */
export function useUnsavedWork(hasUnsavedWork: boolean): void {
  const { t, i18n } = useTranslation()

  useEffect(() => {
    if (!hasUnsavedWork) return

    window.__babaUnsaved = () => ({
      title: t('unsaved.title'),
      body: t('unsaved.body'),
      leave: t('unsaved.leave'),
      stay: t('unsaved.stay'),
      rtl: i18n.language === 'ar',
    })
    const onBeforeUnload = (event: BeforeUnloadEvent) => event.preventDefault()
    window.addEventListener('beforeunload', onBeforeUnload)

    return () => {
      delete window.__babaUnsaved
      window.removeEventListener('beforeunload', onBeforeUnload)
    }
  }, [hasUnsavedWork, t, i18n.language])
}
