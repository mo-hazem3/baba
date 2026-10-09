import { Button, Space, Table, Tag } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate } from 'react-router'
import type { BankAccountDto } from '../../api/generated/model'
import { useBankAccounts, useCurrencies, useCurrentCompany } from '../../api/hooks'
import { AmountText } from '../../layout/AmountText'
import { EmptyState } from '../../layout/EmptyState'
import { ExportControls } from '../../layout/ExportControls'
import { PageHeader } from '../../layout/PageHeader'
import { useSettings } from '../../settings/SettingsContext'
import { formatDate, parseIsoDate } from '../../utils/format'
import { itemName } from '../accounting/LookupSelects'
import { exportAndShow } from '../reports/exportReport'

/**
 * The bank and cash accounts (brief section 10.2): what each holds, how far it has been checked against the bank's statements, and the
 * way into a reconciliation, an account's statement and a transfer.
 */
export function BankPage() {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const navigate = useNavigate()
  const company = useCurrentCompany()
  const currencies = useCurrencies()
  const accounts = useBankAccounts()
  const minorUnits = currencies.data?.find((c) => c.code === company.data?.baseCurrencyCode)?.minorUnits ?? 2

  const columns: ColumnsType<BankAccountDto> = [
    { title: t('accounts.code'), dataIndex: 'code', width: 100, render: (code: string) => <span dir="ltr">{code}</span> },
    {
      title: t('accounts.name'),
      key: 'name',
      render: (_: unknown, row) => <span className={row.isActive ? '' : 'muted'}>{itemName(row, settings.language)}</span>,
    },
    { title: t('bank.balance'), key: 'balance', width: 150, align: 'end', render: (_: unknown, row) => <AmountText value={row.balance} minorUnits={minorUnits} /> },
    {
      title: t('bank.reconciledTo'),
      key: 'reconciled',
      width: 210,
      render: (_: unknown, row) =>
        row.lastStatementDate ? (
          <span>
            {formatDate(parseIsoDate(row.lastStatementDate), settings.digits, settings.hijri)} · <AmountText value={row.lastStatementBalance ?? 0} minorUnits={minorUnits} />
          </span>
        ) : (
          <span className="muted">{t('bank.neverReconciled')}</span>
        ),
    },
    {
      title: t('bank.unreconciled'),
      key: 'unreconciled',
      width: 130,
      render: (_: unknown, row) => (row.unreconciled > 0 ? <Tag color="gold">{t('bank.entriesToCheck', { count: row.unreconciled })}</Tag> : <Tag color="green">{t('bank.allChecked')}</Tag>),
    },
    {
      title: <span className="visually-hidden">{t('voucher.rowActions')}</span>,
      key: 'actions',
      width: 300,
      render: (_: unknown, row) => (
        <Space size={0} wrap>
          <Button type="link" onClick={() => navigate(`/bank/${row.accountId}/reconcile`)} aria-label={`${t('bank.reconcile')} ${row.code}`}>
            {t('bank.reconcile')}
          </Button>
          <Link to={`/reports/statement-of-account?accountId=${row.accountId}`}>
            <Button type="link" aria-label={`${t('bank.statement')} ${row.code}`}>
              {t('bank.statement')}
            </Button>
          </Link>
        </Space>
      ),
    },
  ]

  return (
    <div>
      <PageHeader
        title={t('bank.title')}
        help="bank"
        crumbs={[{ label: t('breadcrumb.home'), to: '/' }, { label: t('bank.title') }]}
        action={
          <Space wrap>
            <ExportControls run={(format, layout) => exportAndShow('bank-accounts', {}, format, layout)} />
            <Button type="primary" onClick={() => navigate('/vouchers/transfer/new')}>
              {t('voucher.new.Transfer')}
            </Button>
          </Space>
        }
      />
      {accounts.isSuccess && (accounts.data?.length ?? 0) === 0 ? (
        <EmptyState title={t('bank.emptyTitle')} body={t('bank.emptyBody')} />
      ) : (
        <Table<BankAccountDto> columns={columns} dataSource={accounts.data ?? []} rowKey="accountId" loading={accounts.isPending} pagination={false} size="middle" bordered />
      )}
    </div>
  )
}
