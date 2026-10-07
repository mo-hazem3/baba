import { fireEvent, renderHook } from '@testing-library/react'
import { describeKey, shortcutList, useShortcuts } from './useShortcuts'

describe('describeKey', () => {
  const key = (init: Partial<KeyboardEventInit> & { key: string }) => new KeyboardEvent('keydown', init)

  it('writes a key press as plain text', () => {
    expect(describeKey(key({ key: 'F2' }))).toBe('f2')
    expect(describeKey(key({ key: 's', ctrlKey: true }))).toBe('ctrl+s')
    expect(describeKey(key({ key: 'S', ctrlKey: true }))).toBe('ctrl+s') // caps lock does not matter
    expect(describeKey(key({ key: 'n', metaKey: true }))).toBe('ctrl+n') // Cmd counts as Ctrl
    expect(describeKey(key({ key: 'ArrowLeft', altKey: true }))).toBe('alt+arrowleft')
    expect(describeKey(key({ key: 'Tab', shiftKey: true }))).toBe('shift+tab')
    expect(describeKey(key({ key: 'A', shiftKey: true }))).toBe('a') // shift on a letter is just the capital
  })
})

describe('useShortcuts', () => {
  it('runs the action for a shortcut and takes the key from the browser', () => {
    const save = vi.fn()
    renderHook(() => useShortcuts({ 'ctrl+s': save }))

    const event = new KeyboardEvent('keydown', { key: 's', ctrlKey: true, cancelable: true })
    window.dispatchEvent(event)

    expect(save).toHaveBeenCalledOnce()
    expect(event.defaultPrevented).toBe(true) // so the browser does not try to save the page
  })

  it('leaves every other key alone', () => {
    const save = vi.fn()
    renderHook(() => useShortcuts({ 'ctrl+s': save }))

    const event = new KeyboardEvent('keydown', { key: 'a', cancelable: true })
    fireEvent(window, event)

    expect(save).not.toHaveBeenCalled()
    expect(event.defaultPrevented).toBe(false)
  })

  it('always uses the newest action and stops when the screen goes away', () => {
    const first = vi.fn()
    const second = vi.fn()
    const { rerender, unmount } = renderHook(({ action }) => useShortcuts({ f2: action }), { initialProps: { action: first } })

    rerender({ action: second })
    fireEvent.keyDown(window, { key: 'F2' })
    expect(first).not.toHaveBeenCalled()
    expect(second).toHaveBeenCalledOnce()

    unmount()
    fireEvent.keyDown(window, { key: 'F2' })
    expect(second).toHaveBeenCalledOnce()
  })

  it('lists the shortcuts the brief asks for', () => {
    const keys = shortcutList.map((s) => s.keys)
    for (const wanted of ['Ctrl+S', 'Ctrl+P', 'Ctrl+N', 'F2']) expect(keys).toContain(wanted)
  })
})
