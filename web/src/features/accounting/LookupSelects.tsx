import { Select } from 'antd'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { CostCenterDto, PartyDto } from '../../api/generated/model'
import { useSettings } from '../../settings/SettingsContext'
import type { Language } from '../../settings/settings'
import { matchesSearch, normalizeArabic } from '../../utils/arabic'

interface Item {
  id: string
  code: string
  nameAr: string
  nameEn: string
  isActive: boolean
}

const byCode = (a: Item, b: Item) => a.code.localeCompare(b.code, 'en', { numeric: true, sensitivity: 'base' })

/** An item's name in the user's language, falling back to the other language when one is missing. */
export const itemName = (item: Pick<Item, 'nameAr' | 'nameEn'>, language: Language): string =>
  (language === 'ar' ? item.nameAr || item.nameEn : item.nameEn || item.nameAr) ?? ''

/** "C001 — Gulf Traders", the way a customer, supplier or cost center is shown in pick-lists. */
export const itemLabel = (item: Item, language: Language): string => `${item.code} — ${itemName(item, language)}`

/** Finds items by code or by Arabic or English name; codes that start with what was typed come first. */
export const searchItems = <T extends Item>(items: readonly T[], query: string): T[] => {
  const typed = normalizeArabic(query).trim()
  const found = items.filter((i) => matchesSearch(query, i.code, i.nameAr, i.nameEn))
  const rank = (i: Item) => (typed && normalizeArabic(i.code).startsWith(typed) ? 0 : 1)
  return [...found].sort((a, b) => rank(a) - rank(b) || byCode(a, b))
}

interface LookupProps {
  value: string | undefined
  onChange: (id: string | undefined) => void
  items: readonly Item[]
  placeholder?: string
  ariaLabel: string
  status?: 'error'
  disabled?: boolean
  id?: string
  allowClear?: boolean
}

/** A search box over a short list (customers, suppliers, cost centers). Switched-off items are not offered unless already chosen. */
function LookupSelect({ value, onChange, items, placeholder, ariaLabel, status, disabled, id, allowClear = true }: LookupProps) {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const [query, setQuery] = useState('')

  const options = useMemo(() => {
    const found = searchItems(items.filter((i) => i.isActive), query).slice(0, 60)
    const chosen = value ? items.find((i) => i.id === value) : undefined
    const shown = chosen && !found.some((i) => i.id === chosen.id) ? [chosen, ...found] : found
    return shown.map((i) => ({ value: i.id, label: itemLabel(i, settings.language) }))
  }, [items, query, value, settings.language])

  return (
    <Select
      id={id}
      showSearch
      allowClear={allowClear}
      filterOption={false}
      value={value}
      options={options}
      onSearch={setQuery}
      onChange={(next) => {
        setQuery('')
        onChange(next)
      }}
      onOpenChange={(open) => !open && setQuery('')}
      placeholder={placeholder}
      notFoundContent={t('lookup.nothingFound')}
      status={status}
      aria-label={ariaLabel}
      disabled={disabled}
      popupMatchSelectWidth={false}
      className="account-select"
    />
  )
}

export function PartySelect({
  parties,
  kind,
  ...props
}: Omit<LookupProps, 'items'> & { parties: readonly PartyDto[]; kind?: PartyDto['kind'] }) {
  return <LookupSelect {...props} items={kind ? parties.filter((p) => p.kind === kind) : parties} />
}

export function CostCenterSelect({ costCenters, ...props }: Omit<LookupProps, 'items'> & { costCenters: readonly CostCenterDto[] }) {
  return <LookupSelect {...props} items={costCenters} />
}
