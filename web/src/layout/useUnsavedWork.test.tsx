import { renderHook } from '@testing-library/react'
import i18n from '../i18n'
import { useUnsavedWork } from './useUnsavedWork'

afterEach(async () => {
  await i18n.changeLanguage('en')
})

describe('useUnsavedWork', () => {
  it('offers nothing when there is nothing to lose', () => {
    renderHook(() => useUnsavedWork(false))

    expect(window.__babaUnsaved).toBeUndefined()
  })

  it('offers a prompt in English while there is unsaved work, and removes it afterwards', () => {
    const { rerender, unmount } = renderHook(({ dirty }) => useUnsavedWork(dirty), { initialProps: { dirty: true } })

    expect(window.__babaUnsaved?.()).toEqual({
      title: 'Leave without saving?',
      body: expect.stringContaining('has not been created yet'),
      leave: 'Close Baba',
      stay: 'Keep working',
      rtl: false,
    })

    rerender({ dirty: false })
    expect(window.__babaUnsaved).toBeUndefined()

    rerender({ dirty: true })
    expect(window.__babaUnsaved).toBeDefined()
    unmount()
    expect(window.__babaUnsaved).toBeUndefined()
  })

  it('asks in Arabic, right to left, when the app is in Arabic', async () => {
    await i18n.changeLanguage('ar')
    renderHook(() => useUnsavedWork(true))

    const prompt = window.__babaUnsaved?.()
    expect(prompt?.rtl).toBe(true)
    expect(prompt?.title).toMatch(/[؀-ۿ]/)
  })

  it('also asks before the page itself is closed', () => {
    renderHook(() => useUnsavedWork(true))
    const event = new Event('beforeunload', { cancelable: true })

    window.dispatchEvent(event)

    expect(event.defaultPrevented).toBe(true)
  })
})
