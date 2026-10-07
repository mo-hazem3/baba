import { App, Button, Input, Space, Table, Tag } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { deleteParty, setPartyActive } from '../../api/generated/baba'
import type { PartyDto, PartyKind } from '../../api/generated/model'
import { ApiError } from '../../api/http'
import { refreshBooks, useCurrencies, useCurrentCompany, useParties } from '../../api/hooks'
import { AmountText } from '../../layout/AmountText'
import { EmptyState } from '../../layout/EmptyState'
import { errorMessage } from '../../layout/errors'
import { ListPage } from '../../layout/ListPage'
import { useShortcuts } from '../../layout/useShortcuts'
import { useSettings } from '../../settings/SettingsContext'
import { matchesSearch } from '../../utils/arabic'
import { itemName } from '../accounting/LookupSelects'
import { ImportModal } from '../../layout/ImportModal'
import { PartyFormModal } from './PartyFormModal'

/**
 * The customers or the suppliers of the company (brief section 10.2), with what each owes or is owed, a link to their statement,
 * and the usual add, edit, switch off and delete. A party that has been used on a voucher cannot be deleted, only switched off.
 */
export function PartiesPage({ kind }: { kind: PartyKind }) {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const { message, modal } = App.useApp()
  const queryClient = useQueryClient()
  const company = useCurrentCompany()
  const currencies = useCurrencies()
  const partiesQuery = useParties({ kind })
  const allQuery = useParties()
  const parties = useMemo(() => partiesQuery.data ?? [], [partiesQuery.data])
  const customer = kind === 'Customer'
  const minorUnits = currencies.data?.find((c) => c.code === company.data?.baseCurrencyCode)?.minorUnits ?? 2

  const [search, setSearch] = useState('')
  const [importing, setImporting] = useState(false)
  const [form, setForm] = useState<{ open: boolean; editing?: PartyDto }>({ open: false })

  const rows = useMemo(() => parties.filter((p) => matchesSearch(search, p.code, p.nameAr, p.nameEn, p.phone ?? '')), [parties, search])

  const toggle = useMutation({
    mutationFn: (p: PartyDto) => setPartyActive(p.id, { active: !p.isActive }),
    onSuccess: () => refreshBooks(queryClient),
    onError: (error) => void message.error(errorMessage(error, t)),
  })

  const remove = useMutation({
    mutationFn: (p: PartyDto) => deleteParty(p.id),
    onSuccess: async () => {
      await refreshBooks(queryClient)
      void message.success(t('parties.deleted'))
    },
    onError: (error) => {
      const inUse = error instanceof ApiError && error.issues.some((i) => i.code === 'party.in-use')
      void message.error(inUse ? t('parties.issues.party.in-use') : errorMessage(error, t))
    },
  })

  const confirmDelete = (party: PartyDto) =>
    modal.confirm({
      title: t('parties.deleteTitle', { name: `${party.code} ${itemName(party, settings.language)}` }),
      content: t('parties.deleteBody'),
      okText: t('parties.delete'),
      okButtonProps: { danger: true },
      cancelText: t('common.cancel'),
      onOk: () => remove.mutateAsync(party).catch(() => undefined),
    })

  useShortcuts({ 'ctrl+n': () => setForm({ open: true }) })

  const columns: ColumnsType<PartyDto> = [
    { title: t('parties.code'), dataIndex: 'code', width: 110, render: (code: string) => <span dir="ltr">{code}</span> },
    {
      title: t('parties.name'),
      key: 'name',
      render: (_: unknown, row) => <span className={row.isActive ? '' : 'muted'}>{itemName(row, settings.language)}</span>,
    },
    { title: t('parties.phone'), dataIndex: 'phone', width: 150, render: (phone: string | null) => (phone ? <bdi dir="ltr">{phone}</bdi> : null) },
    {
      title: t('parties.terms'),
      dataIndex: 'paymentTermsDays',
      width: 110,
      render: (days: number) => t('parties.days', { count: days }),
    },
    ...(customer
      ? [
          {
            title: t('parties.creditLimit'),
            key: 'creditLimit',
            width: 140,
            align: 'end' as const,
            render: (_: unknown, row: PartyDto) => (row.creditLimit > 0 ? <AmountText value={row.creditLimit} minorUnits={minorUnits} /> : <span className="muted">{t('parties.noLimit')}</span>),
          },
        ]
      : []),
    {
      title: t(customer ? 'parties.owesYou' : 'parties.youOwe'),
      key: 'balance',
      width: 150,
      align: 'end',
      render: (_: unknown, row) => (
        <Space size={6}>
          <AmountText value={row.balance} minorUnits={minorUnits} />
          {customer && row.creditLimit > 0 && row.balance > row.creditLimit && <Tag color="red">{t('parties.overLimit')}</Tag>}
        </Space>
      ),
    },
    { title: t('parties.status'), key: 'status', width: 100, render: (_: unknown, row) => (row.isActive ? t('parties.active') : <Tag>{t('parties.inactive')}</Tag>) },
    {
      title: <span className="visually-hidden">{t('voucher.rowActions')}</span>,
      key: 'actions',
      width: 330,
      render: (_: unknown, row) => (
        <Space size={0} wrap>
          <Link to={`/reports/party-statement?partyId=${row.id}`}>
            <Button type="link" aria-label={`${t('parties.statement')} ${row.code}`}>
              {t('parties.statement')}
            </Button>
          </Link>
          <Button type="link" onClick={() => setForm({ open: true, editing: row })} aria-label={`${t('parties.edit')} ${row.code}`}>
            {t('parties.edit')}
          </Button>
          <Button type="link" onClick={() => toggle.mutate(row)} aria-label={`${row.isActive ? t('parties.deactivate') : t('parties.activate')} ${row.code}`}>
            {row.isActive ? t('parties.deactivate') : t('parties.activate')}
          </Button>
          <Button type="link" danger onClick={() => confirmDelete(row)} aria-label={`${t('parties.delete')} ${row.code}`}>
            {t('parties.delete')}
          </Button>
        </Space>
      ),
    },
  ]

  return (
    <ListPage
      title={t(customer ? 'parties.customers' : 'parties.suppliers')}
      help="parties"
      newLabel={t(customer ? 'parties.newCustomer' : 'parties.newSupplier')}
      onNew={() => setForm({ open: true })}
      actions={<Button onClick={() => setImporting(true)}>{t('import.partiesButton')}</Button>}
      filters={
        <Input.Search
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder={t('parties.search')}
          aria-label={t('parties.search')}
          allowClear
          className="filter-search"
        />
      }
    >
      {partiesQuery.isSuccess && parties.length === 0 ? (
        <EmptyState title={t(customer ? 'parties.emptyCustomersTitle' : 'parties.emptySuppliersTitle')} body={t(customer ? 'parties.emptyCustomersBody' : 'parties.emptySuppliersBody')} />
      ) : (
        <Table<PartyDto> columns={columns} dataSource={rows} rowKey="id" loading={partiesQuery.isPending} pagination={false} size="middle" bordered />
      )}

      <ImportModal
        open={importing}
        onClose={() => setImporting(false)}
        title={t(customer ? 'import.customersTitle' : 'import.suppliersTitle')}
        intro={t('import.partiesIntro')}
        columns="Code, Name, Name (Arabic), Phone, Email, Address, Tax number, Credit limit, Payment terms"
        url={`/api/import/parties/${kind}`}
        template={'Code,Name,Name (Arabic),Phone,Email,Address,Tax number,Credit limit,Payment terms\n,Gulf Traders,الخليج للتجارة,+965 5555 1111,,,,1500,30\n'}
        templateName={customer ? 'customers-template.csv' : 'suppliers-template.csv'}
      />
      <PartyFormModal
        open={form.open}
        kind={kind}
        editing={form.editing}
        parties={allQuery.data ?? []}
        minorUnits={minorUnits}
        onClose={() => setForm({ open: false })}
      />
    </ListPage>
  )
}
