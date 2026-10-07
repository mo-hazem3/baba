import { DatePicker } from 'antd'
import dayjs from 'dayjs'
import { useTranslation } from 'react-i18next'
import { useSettings } from '../settings/SettingsContext'
import { formatHijri, parseIsoDate, toIsoDate } from '../utils/format'

/**
 * A date as day/month/year (06/10/2026), typed or picked, kept as the API's text (2026-10-06). When Hijri dates are switched on in
 * Settings, the Hijri date is shown next to it (display only: dates are always entered as Gregorian).
 */
export function DateField({
  value,
  onChange,
  id,
  status,
  disabled,
  allowEmpty = false,
  ariaLabel,
}: {
  value: string | undefined
  onChange: (value: string | undefined) => void
  id?: string
  status?: 'error'
  disabled?: boolean
  allowEmpty?: boolean
  ariaLabel?: string
}) {
  const { t } = useTranslation()
  const { settings } = useSettings()

  return (
    <span className="date-field">
      <DatePicker
        id={id}
        value={value ? dayjs(parseIsoDate(value)) : null}
        onChange={(date) => onChange(date ? toIsoDate(date.toDate()) : undefined)}
        format="DD/MM/YYYY"
        allowClear={allowEmpty}
        status={status}
        disabled={disabled}
        aria-label={ariaLabel}
        placeholder="DD/MM/YYYY"
        className="date-input"
      />
      {settings.hijri && value && (
        <span className="muted hijri" dir="ltr">
          {formatHijri(value, settings.digits)} {t('common.hijriSuffix')}
        </span>
      )}
    </span>
  )
}
