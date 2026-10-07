import { useSettings } from '../settings/SettingsContext'
import { formatAmount } from '../utils/format'

/**
 * An amount for reading: thousands separators, the currency's decimals, the user's digit style. A negative amount has a minus
 * sign and is also red (never colour alone), and it is isolated left to right so the sign stays in front inside Arabic text.
 */
export function AmountText({ value, minorUnits, blankZero = false }: { value: number | null | undefined; minorUnits: number; blankZero?: boolean }) {
  const { settings } = useSettings()
  if (value === null || value === undefined || (blankZero && value === 0)) return null

  return (
    <bdi dir="ltr" className={value < 0 ? 'numbers negative' : 'numbers'}>
      {formatAmount(value, minorUnits, settings.digits)}
    </bdi>
  )
}
