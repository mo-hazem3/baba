import { App, Button, Input, Space, Table, Tag } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { deleteAccount, setAccountActive } from '../../api/generated/baba'
import type { AccountDto } from '../../api/generated/model'
import { ApiError } from '../../api/http'
import { refreshBooks, useAccounts } from '../../api/hooks'
import { EmptyState } from '../../layout/EmptyState'
import { errorMessage } from '../../layout/errors'
import { ExportControls } from '../../layout/ExportControls'
import { ListPage } from '../../layout/ListPage'
import { useShortcuts } from '../../layout/useShortcuts'
import { useSettings } from '../../settings/SettingsContext'
import { matchesSearch } from '../../utils/arabic'
import { exportAndShow } from '../reports/exportReport'
import { accountName, buildTree, type AccountNode } from '../accounting/accountTree'
import { ImportModal } from '../../layout/ImportModal'
import { AccountFormModal } from './AccountFormModal'
import { MoveEntriesModal } from './MoveEntriesModal'

type Row = Omit<AccountNode, 'children'> & { children?: Row[] }

/** Leaves must not carry an empty `children` list, or the table shows an expander for them. */
const toRows = (nodes: AccountNode[]): Row[] => nodes.map((n) => ({ ...n, children: n.children.length > 0 ? toRows(n.children) : undefined }))

/** The chart of accounts (brief section 10.1): an unlimited-depth tree you can search, add to, edit, move, deactivate and delete from. */
export function ChartOfAccountsPage() {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const { message, modal } = App.useApp()
  const queryClient = useQueryClient()
  const accountsQuery = useAccounts()
  const accounts = useMemo(() => accountsQuery.data ?? [], [accountsQuery.data])

  const [search, setSearch] = useState('')
  const [form, setForm] = useState<{ open: boolean; editing?: AccountDto }>({ open: false })
  const [moving, setMoving] = useState<AccountDto>()
  const [importing, setImporting] = useState(false)

  const searching = search.trim().length > 0
  const rows: Row[] = useMemo(
    () => (searching ? accounts.filter((a) => matchesSearch(search, a.code, a.nameAr, a.nameEn)).map((a) => ({ ...a, children: undefined })) : toRows(buildTree(accounts))),
    [accounts, search, searching],
  )
  const groupIds = useMemo(() => accounts.filter((a) => !a.isPosting).map((a) => a.id), [accounts])

  const toggle = useMutation({
    mutationFn: (a: AccountDto) => setAccountActive(a.id, { active: !a.isActive }),
    onSuccess: () => refreshBooks(queryClient),
    onError: (error) => void message.error(errorMessage(error, t)),
  })

  const remove = useMutation({
    mutationFn: (a: AccountDto) => deleteAccount(a.id),
    onSuccess: async () => {
      await refreshBooks(queryClient)
      void message.success(t('accounts.deleted'))
    },
    onError: (error, account) => {
      const codes = error instanceof ApiError ? error.issues.map((i) => i.code) : []
      if (codes.includes('account.has-entries')) setMoving(account) // say why, and offer to move the entries
      else if (codes.includes('account.has-children')) void message.error(t('accounts.issues.account.has-children'))
      else void message.error(errorMessage(error, t))
    },
  })

  const confirmDelete = (account: AccountDto) =>
    modal.confirm({
      title: t('accounts.deleteTitle', { name: `${account.code} ${accountName(account, settings.language)}` }),
      content: t('accounts.deleteBody'),
      okText: t('accounts.delete'),
      okButtonProps: { danger: true },
      cancelText: t('common.cancel'),
      onOk: () => remove.mutateAsync(account).catch(() => undefined),
    })

  useShortcuts({ 'ctrl+n': () => setForm({ open: true }) })

  const columns: ColumnsType<Row> = [
    { title: t('accounts.code'), dataIndex: 'code', width: 130, render: (code: string, row) => <span dir="ltr" className={row.isPosting ? '' : 'strong'}>{code}</span> },
    {
      title: t('accounts.name'),
      key: 'name',
      render: (_: unknown, row) => <span className={row.isPosting ? (row.isActive ? '' : 'muted') : 'strong'}>{accountName(row, settings.language)}</span>,
    },
    { title: t('accounts.type'), dataIndex: 'type', width: 130, render: (type: AccountDto['type']) => t(`accounts.types.${type}`) },
    {
      title: t('accounts.kind'),
      key: 'kind',
      width: 130,
      render: (_: unknown, row) => (
        <Space size={4} wrap>
          {row.isPosting ? t('accounts.postable') : t('accounts.group')}
          {row.role !== 'None' && <Tag>{t(`accounts.roles.${row.role}`)}</Tag>}
        </Space>
      ),
    },
    { title: t('accounts.status'), key: 'status', width: 110, render: (_: unknown, row) => (row.isActive ? t('accounts.active') : <Tag>{t('accounts.inactive')}</Tag>) },
    {
      title: <span className="visually-hidden">{t('voucher.rowActions')}</span>,
      key: 'actions',
      width: 280,
      render: (_: unknown, row) => (
        <Space size={0} wrap>
          <Button type="link" onClick={() => setForm({ open: true, editing: row })} aria-label={`${t('accounts.edit')} ${row.code}`}>
            {t('accounts.edit')}
          </Button>
          <Button type="link" onClick={() => toggle.mutate(row)} aria-label={`${row.isActive ? t('accounts.deactivate') : t('accounts.activate')} ${row.code}`}>
            {row.isActive ? t('accounts.deactivate') : t('accounts.activate')}
          </Button>
          <Button type="link" danger onClick={() => confirmDelete(row)} aria-label={`${t('accounts.delete')} ${row.code}`}>
            {t('accounts.delete')}
          </Button>
        </Space>
      ),
    },
  ]

  return (
    <ListPage
      title={t('accounts.title')}
      help="accounts"
      newLabel={t('accounts.new')}
      onNew={() => setForm({ open: true })}
      actions={
        <Space wrap>
          <Button onClick={() => setImporting(true)}>{t('import.accountsButton')}</Button>
          <ExportControls run={(format, layout) => exportAndShow('chart-of-accounts', {}, format, layout)} />
        </Space>
      }
      filters={
        <Input.Search
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder={t('accounts.search')}
          aria-label={t('accounts.search')}
          allowClear
          className="filter-search"
        />
      }
    >
      {accountsQuery.isSuccess && accounts.length === 0 ? (
        <EmptyState title={t('accounts.emptyTitle')} body={t('accounts.emptyBody')} />
      ) : (
        <Table<Row>
          key={searching ? 'flat' : 'tree'} // the tree opens fully each time it comes back from a search
          columns={columns}
          dataSource={rows}
          rowKey="id"
          loading={accountsQuery.isPending}
          pagination={false}
          size="middle"
          bordered
          expandable={{ defaultExpandedRowKeys: groupIds }}
          rowClassName={(row) => (row.isPosting ? '' : 'report-group')}
        />
      )}

      <AccountFormModal open={form.open} editing={form.editing} accounts={accounts} onClose={() => setForm({ open: false })} />
      <ImportModal
        open={importing}
        onClose={() => setImporting(false)}
        title={t('import.accountsTitle')}
        intro={t('import.accountsIntro')}
        columns="Code, Name, Name (Arabic), Parent code, Type, Kind, Special use"
        url="/api/import/accounts"
        templateKey="accounts"
      />
      <MoveEntriesModal account={moving} accounts={accounts.filter((a) => a.isPosting && a.isActive)} onClose={() => setMoving(undefined)} />
    </ListPage>
  )
}
