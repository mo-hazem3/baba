import { InputNumber, Select } from 'antd'
import { useEffect, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { getExchangeRateOn } from '../../api/generated/baba'
import { useCurrencies, useCurrentCompany } from '../../api/hooks'
import { useSettings } from '../../settings/SettingsContext'

/**
 * The currency of a voucher or document and, for a foreign currency, how many units of the company's currency one unit is worth
 * (brief section 5). Choosing a currency fills in the latest rate on or before the date; it can be typed over. Without a rate on file
 * the field is left empty and says so, so nothing is booked at a guessed rate.
 */
export function CurrencyFields({
  date,
  currencyCode,
  rate,
  onChange,
  disabled,
  rateStatus,
}: {
  date: string
  currencyCode: string
  rate: number | null
  onChange: (change: { currencyCode: string; rate: number | null }) => void
  disabled?: boolean
  rateStatus?: 'error'
}) {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const company = useCurrentCompany()
  const currencies = useCurrencies()
  const base = company.data?.baseCurrencyCode ?? ''
  const foreign = currencyCode !== '' && currencyCode !== base

  // The rate follows the currency and the date until the person types one. `typed` remembers that they did.
  const typed = useRef(rate !== null)
  const lookup = useRef(0)
  useEffect(() => {
    if (!foreign || typed.current || disabled) return
    const ticket = ++lookup.current
    void getExchangeRateOn({ currency: currencyCode, date }).then((response) => {
      if (ticket !== lookup.current || response.status !== 200) return
      onChange({ currencyCode, rate: response.data.found ? response.data.rate : null })
    })
    // eslint-disable-next-line react-hooks/exhaustive-deps -- onChange changes on every render; the lookup is about the currency and date
  }, [currencyCode, date, foreign, disabled])

  const options = (currencies.data ?? [])
    .slice()
    .sort((a, b) => (a.code === base ? -1 : b.code === base ? 1 : a.code.localeCompare(b.code)))
    .map((c) => ({ value: c.code, label: `${c.code} — ${settings.language === 'ar' ? c.nameAr : c.nameEn}` }))

  return (
    <>
      <div className="field">
        <label htmlFor="voucher-currency">{t('voucher.currency')}</label>
        <Select
          id="voucher-currency"
          value={currencyCode || undefined}
          showSearch
          optionFilterProp="label"
          options={options}
          disabled={disabled}
          aria-label={t('voucher.currency')}
          className="currency-select"
          onChange={(code: string) => {
            typed.current = false
            onChange({ currencyCode: code, rate: code === base ? null : null })
          }}
        />
      </div>
      {foreign && (
        <div className="field">
          <label htmlFor="voucher-rate">{t('voucher.exchangeRate', { from: currencyCode, to: base })}</label>
          <InputNumber
            id="voucher-rate"
            value={rate}
            min={0}
            precision={6}
            controls={false}
            disabled={disabled}
            status={rateStatus}
            className="amount-input"
            aria-label={t('voucher.exchangeRate', { from: currencyCode, to: base })}
            onChange={(v) => {
              typed.current = true
              onChange({ currencyCode, rate: typeof v === 'number' ? v : null })
            }}
          />
          {rate === null && <span className="muted">{t('voucher.noRateOnFile')}</span>}
        </div>
      )}
    </>
  )
}
