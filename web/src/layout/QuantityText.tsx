import { useSettings } from '../settings/SettingsContext'
import { formatQuantity } from '../utils/format'

/** A quantity of stock for reading, in the user's digit style; negative quantities (a loss) keep their minus sign. */
export function QuantityText({ value }: { value: number | null | undefined }) {
  const { settings } = useSettings()
  if (value === null || value === undefined) return null

  return (
    <bdi dir="ltr" className={value < 0 ? 'numbers negative' : 'numbers'}>
      {formatQuantity(value, settings.digits)}
    </bdi>
  )
}
