import { App, Button, Card, Select, Table, Tag } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { listPeriods, setPeriodLocked } from '../../api/generated/baba'
import type { PeriodDto } from '../../api/generated/model'
import { refreshBooks, useCurrentCompany } from '../../api/hooks'
import { errorMessage } from '../../layout/errors'
import { useSettings } from '../../settings/SettingsContext'
import { formatDate, monthName, parseIsoDate } from '../../utils/format'

/**
 * Locking months (brief sections 8 and 10.2): after filing a VAT return, say, nobody can add, change or delete a posted voucher in
 * that month until it is unlocked again.
 */
export function PeriodsCard() {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const { message, modal } = App.useApp()
  const queryClient = useQueryClient()
  const company = useCurrentCompany()

  const startMonth = company.data?.fiscalYearStartMonth ?? 1
  const today = new Date()
  const currentYear = today.getMonth() + 1 >= startMonth ? today.getFullYear() : today.getFullYear() - 1
  const [year, setYear] = useState(currentYear)

  const periods = useQuery({
    queryKey: ['/api/periods', year],
    queryFn: async () => (await listPeriods({ fiscalYear: year })).data,
  })

  const lock = useMutation({
    mutationFn: (p: { period: PeriodDto; locked: boolean }) => setPeriodLocked({ monthStart: p.period.start, locked: p.locked }),
    onSuccess: () => refreshBooks(queryClient),
    onError: (error) => void message.error(errorMessage(error, t)),
  })

  const toggle = (period: PeriodDto) => {
    const name = `${monthName(parseIsoDate(period.start).getMonth() + 1, settings.language)} ${parseIsoDate(period.start).getFullYear()}`
    if (period.isLocked) return lock.mutate({ period, locked: false })
    modal.confirm({
      title: t('periods.lockTitle', { month: name }),
      content: t('periods.lockBody'),
      okText: t('periods.lock'),
      cancelText: t('common.cancel'),
      onOk: () => lock.mutateAsync({ period, locked: true }).catch(() => undefined),
    })
  }

  const columns: ColumnsType<PeriodDto> = [
    {
      title: t('periods.month'),
      key: 'month',
      render: (_: unknown, p) => `${monthName(parseIsoDate(p.start).getMonth() + 1, settings.language)} ${parseIsoDate(p.start).getFullYear()}`,
    },
    {
      title: t('periods.dates'),
      key: 'dates',
      render: (_: unknown, p) => `${formatDate(parseIsoDate(p.start), settings.digits)} – ${formatDate(parseIsoDate(p.end), settings.digits)}`,
    },
    { title: t('periods.status'), key: 'status', render: (_: unknown, p) => (p.isLocked ? <Tag color="red">{t('periods.locked')}</Tag> : <Tag color="green">{t('periods.open')}</Tag>) },
    {
      title: <span className="visually-hidden">{t('voucher.rowActions')}</span>,
      key: 'action',
      render: (_: unknown, p) => (
        <Button onClick={() => toggle(p)} loading={lock.isPending && lock.variables?.period.start === p.start} aria-label={`${p.isLocked ? t('periods.unlock') : t('periods.lock')} ${p.start}`}>
          {p.isLocked ? t('periods.unlock') : t('periods.lock')}
        </Button>
      ),
    },
  ]

  return (
    <Card
      title={t('periods.title')}
      className="settings-card"
      extra={
        <Select
          value={year}
          onChange={setYear}
          aria-label={t('periods.fiscalYear')}
          options={[-2, -1, 0, 1].map((offset) => ({ value: currentYear + offset, label: `${t('periods.fiscalYear')}: ${currentYear + offset}` }))}
        />
      }
    >
      <p>{t('periods.intro')}</p>
      <Table<PeriodDto> columns={columns} dataSource={periods.data ?? []} rowKey="start" loading={periods.isPending} pagination={false} size="middle" bordered />
    </Card>
  )
}
