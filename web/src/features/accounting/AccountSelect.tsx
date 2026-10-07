import { Select } from 'antd'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { AccountDto } from '../../api/generated/model'
import { useSettings } from '../../settings/SettingsContext'
import { accountLabel, pickableAccounts, searchAccounts } from './accountTree'

/**
 * Pick an account by typing its code, or part of its Arabic or English name (brief section 10.1). Press F2 anywhere on the form
 * to open the search window over the whole chart instead. Only accounts that can be posted to are offered by default.
 */
export function AccountSelect({
  value,
  onChange,
  accounts,
  usableOnly = true,
  cashOnly = false,
  status,
  placeholder,
  ariaLabel,
  autoFocus,
  disabled,
  id,
}: {
  value: string | undefined
  onChange: (accountId: string | undefined) => void
  accounts: readonly AccountDto[]
  usableOnly?: boolean
  cashOnly?: boolean
  status?: 'error'
  placeholder?: string
  ariaLabel?: string
  autoFocus?: boolean
  disabled?: boolean
  id?: string
}) {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const [query, setQuery] = useState('')

  const options = useMemo(() => {
    const offered = pickableAccounts(accounts, { onlyUsable: usableOnly, cashOnly })
    const found = searchAccounts(offered, query).slice(0, 60)
    // The chosen account always stays in the list, or its name would turn into an id while the user types something else.
    const chosen = value ? accounts.find((a) => a.id === value) : undefined
    const shown = chosen && !found.some((a) => a.id === chosen.id) ? [chosen, ...found] : found
    return shown.map((a) => ({ value: a.id, label: accountLabel(a, settings.language) }))
  }, [accounts, usableOnly, cashOnly, query, value, settings.language])

  return (
    <Select
      id={id}
      showSearch
      allowClear
      filterOption={false}
      value={value}
      options={options}
      onSearch={setQuery}
      onChange={(next) => {
        setQuery('')
        onChange(next)
      }}
      onOpenChange={(open) => !open && setQuery('')}
      placeholder={placeholder ?? t('accounting.accountPlaceholder')}
      notFoundContent={t('accounting.noAccountFound')}
      status={status}
      aria-label={ariaLabel ?? t('accounting.account')}
      autoFocus={autoFocus}
      disabled={disabled}
      popupMatchSelectWidth={false}
      className="account-select"
    />
  )
}
