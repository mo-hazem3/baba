import { useEffect, useRef } from 'react'

/** The keyboard shortcuts of the app (brief section 7.4). Shown in the "Shortcuts" help panel. */
export const shortcutList = [
  { keys: 'Ctrl+S', action: 'save' },
  { keys: 'Ctrl+P', action: 'print' },
  { keys: 'Ctrl+N', action: 'new' },
  { keys: 'F2', action: 'findAccount' },
  { keys: 'Tab / Enter', action: 'nextCell' },
  { keys: '↑ / ↓', action: 'nextRow' },
  { keys: 'Ctrl + / Ctrl −', action: 'textSize' },
  { keys: 'Alt+←', action: 'back' },
] as const

/** "Ctrl+S" from a key press, so shortcuts can be written as plain text: 'ctrl+s', 'f2', 'ctrl+shift+n'. */
export const describeKey = (event: Pick<KeyboardEvent, 'key' | 'ctrlKey' | 'metaKey' | 'shiftKey' | 'altKey'>): string => {
  const parts: string[] = []
  if (event.ctrlKey || event.metaKey) parts.push('ctrl')
  if (event.altKey) parts.push('alt')
  if (event.shiftKey && event.key.length > 1) parts.push('shift')
  parts.push(event.key.toLowerCase())
  return parts.join('+')
}

/**
 * Runs actions on shortcuts while the screen is shown. A matching key press is taken over from the browser (Ctrl+S would
 * otherwise try to save the web page, Ctrl+P to print it). The newest handlers are always used.
 */
export function useShortcuts(handlers: Record<string, () => void>): void {
  const latest = useRef(handlers)
  useEffect(() => {
    latest.current = handlers // after every render, so the newest handlers are used
  })

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      const handler = latest.current[describeKey(event)]
      if (!handler) return
      event.preventDefault()
      handler()
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [])
}
