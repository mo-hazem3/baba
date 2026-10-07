import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import type { AccountDto } from '../../api/generated/model'
import '../../i18n'
import { SettingsProvider } from '../../settings/SettingsContext'
import { VoucherLinesGrid } from './VoucherLinesGrid'
import { newRow, type LineRow } from './voucherModel'

const cash = { id: 'a1', code: '111', nameEn: 'Cash on hand', nameAr: 'النقدية', type: 'Asset', isPosting: true, isActive: true } as AccountDto

function Harness({ kind, onRows }: { kind: 'Payment' | 'Receipt' | 'Journal'; onRows?: (rows: LineRow[]) => void }) {
  const [rows, setRows] = useState<LineRow[]>([newRow()])
  return (
    <SettingsProvider>
      <VoucherLinesGrid
        kind={kind}
        rows={rows}
        onChange={(next) => {
          setRows(next)
          onRows?.(next)
        }}
        accounts={[cash]}
        issues={{}}
        minorUnits={3}
      />
    </SettingsProvider>
  )
}

describe('VoucherLinesGrid', () => {
  it('moves from the description to the amount with Enter, then on to the next line', async () => {
    const user = userEvent.setup()
    render(<Harness kind="Payment" />)

    await user.type(screen.getByLabelText('Description 1'), 'Office rent')
    await user.keyboard('{Enter}')
    await vi.waitFor(() => expect(screen.getByLabelText('Amount 1')).toHaveFocus())

    await user.keyboard('300{Enter}')
    // The line that was typed in is full, so a new empty line has appeared and the focus is on its account box.
    await vi.waitFor(() => expect(screen.getByLabelText('Account 2')).toHaveFocus())
  })

  it('moves between rows with the arrow keys, staying in the same column', async () => {
    const user = userEvent.setup()
    render(<Harness kind="Payment" />)

    await user.type(screen.getByLabelText('Description 1'), 'First')
    await vi.waitFor(() => expect(screen.getByLabelText('Description 2')).toBeInTheDocument())

    await user.click(screen.getByLabelText('Description 1'))
    await user.keyboard('{ArrowDown}')
    await vi.waitFor(() => expect(screen.getByLabelText('Description 2')).toHaveFocus())
    await user.keyboard('{ArrowUp}')
    await vi.waitFor(() => expect(screen.getByLabelText('Description 1')).toHaveFocus())
  })

  it('always keeps one empty line at the bottom to type into', async () => {
    const user = userEvent.setup()
    render(<Harness kind="Payment" />)

    expect(screen.queryByLabelText('Description 2')).not.toBeInTheDocument()
    await user.type(screen.getByLabelText('Description 1'), 'x')
    expect(await screen.findByLabelText('Description 2')).toBeInTheDocument()
  })

  it('lets a journal line be a debit or a credit, never both', async () => {
    const user = userEvent.setup()
    let latest: LineRow[] = []
    render(<Harness kind="Journal" onRows={(rows) => (latest = rows)} />)

    await user.type(screen.getByLabelText('Debit 1'), '500')
    expect(latest[0]).toMatchObject({ debit: 500, credit: null })

    await user.type(screen.getByLabelText('Credit 1'), '200')
    expect(latest[0]).toMatchObject({ debit: null, credit: 200 }) // typing a credit cleared the debit
  })

  it('puts a receipt amount on the credit side and a payment amount on the debit side', async () => {
    const user = userEvent.setup()
    let latest: LineRow[] = []
    const { unmount } = render(<Harness kind="Receipt" onRows={(rows) => (latest = rows)} />)
    await user.type(screen.getByLabelText('Amount 1'), '75')
    expect(latest[0]).toMatchObject({ debit: null, credit: 75 })
    unmount()

    render(<Harness kind="Payment" onRows={(rows) => (latest = rows)} />)
    await user.type(screen.getByLabelText('Amount 1'), '40')
    expect(latest[0]).toMatchObject({ debit: 40, credit: null })
  })
})
