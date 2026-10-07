import { App, Button, Space, Table, Tag } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { deleteRecurring, runDueRecurring, setRecurringActive } from '../../api/generated/baba'
import type { RecurringDto } from '../../api/generated/model'
import { refreshBooks } from '../../api/hooks'
import { useListRecurring } from '../../api/generated/baba'
import { EmptyState } from '../../layout/EmptyState'
import { ExportControls } from '../../layout/ExportControls'
import { exportAndShow } from '../reports/exportReport'
import { errorMessage } from '../../layout/errors'
import { PageHeader } from '../../layout/PageHeader'
import { useSettings } from '../../settings/SettingsContext'
import { formatDate, parseIsoDate } from '../../utils/format'
import { RecurringModal } from './RecurringModal'

/** Everything Baba makes again by itself (brief section 10.3), with a button to make what is due now. */
export function RecurringPage() {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const { message, modal } = App.useApp()
  const queryClient = useQueryClient()
  const query = useListRecurring({ query: { select: (r) => r.data } })
  const [editing, setEditing] = useState<RecurringDto>()
  const schedules = query.data ?? []

  const date = (value: string | null | undefined) => (value ? formatDate(parseIsoDate(value), settings.digits, settings.hijri) : '')
  const kindLabel = (s: RecurringDto) => (s.template === 'Document' ? t(`trade.title.${s.templateKind}`) : t(`voucher.title.${s.templateKind}`))

  const toggle = useMutation({
    mutationFn: (s: RecurringDto) => setRecurringActive(s.id, { active: !s.isActive }),
    onSuccess: () => refreshBooks(queryClient),
    onError: (error) => void message.error(errorMessage(error, t)),
  })
  const remove = useMutation({
    mutationFn: (s: RecurringDto) => deleteRecurring(s.id),
    onSuccess: async () => {
      await refreshBooks(queryClient)
      void message.success(t('recurring.deleted'))
    },
    onError: (error) => void message.error(errorMessage(error, t)),
  })
  const run = useMutation({
    mutationFn: () => runDueRecurring(),
    onSuccess: async (response) => {
      await refreshBooks(queryClient)
      const made = response.data.items.filter((i) => !i.problemCode).length
      const stopped = response.data.items.filter((i) => i.problemCode)
      if (stopped.length > 0)
        void message.warning(stopped.map((i) => `${i.name}: ${t(`recurring.issues.${i.problemCode}`, { defaultValue: t(`voucher.issues.${i.problemCode}`, { defaultValue: i.problemCode ?? '' }) })}`).join(' '), 8)
      else void message.success(made > 0 ? t('recurring.ran', { count: made }) : t('recurring.nothingDue'))
    },
    onError: (error) => void message.error(errorMessage(error, t)),
  })

  const columns: ColumnsType<RecurringDto> = [
    { title: t('recurring.name'), dataIndex: 'name', width: 220, render: (name: string, s) => <span className={s.isActive ? '' : 'muted'}>{name}</span> },
    { title: t('recurring.makes'), key: 'kind', width: 170, render: (_: unknown, s) => kindLabel(s) },
    { title: t('recurring.frequency'), key: 'frequency', width: 120, render: (_: unknown, s) => t(`recurring.frequencies.${s.frequency}`) },
    { title: t('recurring.nextRun'), key: 'next', width: 170, render: (_: unknown, s) => date(s.nextRunDate) },
    { title: t('recurring.ends'), key: 'end', width: 170, render: (_: unknown, s) => date(s.endDate) },
    { title: t('recurring.mode'), key: 'mode', width: 120, render: (_: unknown, s) => (s.autoIssue ? t('recurring.modeIssue') : t('recurring.modeDraft')) },
    { title: t('recurring.runs'), dataIndex: 'runCount', width: 80 },
    { title: t('parties.status'), key: 'status', width: 100, render: (_: unknown, s) => (s.isActive ? t('parties.active') : <Tag>{t('recurring.paused')}</Tag>) },
    {
      title: <span className="visually-hidden">{t('voucher.rowActions')}</span>,
      key: 'actions',
      width: 280,
      render: (_: unknown, s) => (
        <Space size={0} wrap>
          <Button type="link" onClick={() => setEditing(s)} aria-label={`${t('parties.edit')} ${s.name}`}>
            {t('parties.edit')}
          </Button>
          <Button type="link" onClick={() => toggle.mutate(s)} aria-label={`${s.isActive ? t('recurring.pause') : t('recurring.resume')} ${s.name}`}>
            {s.isActive ? t('recurring.pause') : t('recurring.resume')}
          </Button>
          <Button
            type="link"
            danger
            aria-label={`${t('parties.delete')} ${s.name}`}
            onClick={() =>
              modal.confirm({
                title: t('recurring.deleteTitle', { name: s.name }),
                content: t('recurring.deleteBody'),
                okText: t('parties.delete'),
                okButtonProps: { danger: true },
                cancelText: t('common.cancel'),
                onOk: () => remove.mutateAsync(s).catch(() => undefined),
              })
            }
          >
            {t('parties.delete')}
          </Button>
        </Space>
      ),
    },
  ]

  return (
    <div>
      <PageHeader
        title={t('recurring.title')}
        help="recurring"
        action={
          <Space wrap>
            <ExportControls run={(format, layout) => exportAndShow('recurring', {}, format, layout)} />
            <Button type="primary" onClick={() => run.mutate()} loading={run.isPending}>
              {t('recurring.runNow')}
            </Button>
          </Space>
        }
      />
      {query.isSuccess && schedules.length === 0 ? (
        <EmptyState title={t('recurring.emptyTitle')} body={t('recurring.emptyBody')} />
      ) : (
        <Table<RecurringDto> columns={columns} dataSource={schedules} rowKey="id" loading={query.isPending} pagination={false} size="middle" bordered />
      )}
      {editing && <RecurringModal key={editing.id} open editing={editing} onClose={() => setEditing(undefined)} />}
    </div>
  )
}
