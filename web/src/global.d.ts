/** What the desktop window shows when it is closed while there is unsaved work. Null means nothing to lose. */
interface UnsavedWorkPrompt {
  title: string
  body: string
  leave: string
  stay: string
  rtl: boolean
}

interface Window {
  /** Set by the screen that has unsaved work. The desktop host asks it before closing the window. */
  __babaUnsaved?: () => UnsavedWorkPrompt | null
}
