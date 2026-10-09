import { Alert, Input, Select, Table, Tag } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useListAuditLog } from '../../api/generated/baba'
import type { AuditEntryDto } from '../../api/generated/model'
import { DateField } from '../../layout/DateField'
import { EmptyState } from '../../layout/EmptyState'
import { errorMessage } from '../../layout/errors'
import { ExportControls } from '../../layout/ExportControls'
import { ListPage } from '../../layout/ListPage'
import { useSettings } from '../../settings/SettingsContext'
import { formatDateTime } from '../../utils/format'
import { exportAndShow } from '../reports/exportReport'

const limit = 500

/** A field as the program names it (MustChangePassword) read as words (Must change password). */
const fieldLabel = (field: string): string => field.replace(/([a-z])([A-Z])/g, '$1 $2').replace(/^./, (c) => c.toUpperCase())

// The kinds of record worth filtering by; anything else is still in the log and shows under "everything".
const entityKinds = ['Voucher', 'Document', 'Account', 'Party', 'Product', 'Employee', 'PayrollRun', 'ExpenseClaim', 'FixedAsset', 'AppUser', 'Role', 'ApprovalRequest'] as const

/** Who changed what and when, with the old and new values (brief section 10.4). Read only; it can be exported like any list. */
export function AuditLogPage() {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const [from, setFrom] = useState<string>()
  const [to, setTo] = useState<string>()
  const [user, setUser] = useState('')
  const [entity, setEntity] = useState<string>()

  const params = { ...(from ? { from } : {}), ...(to ? { to } : {}), ...(user.trim() ? { user: user.trim() } : {}), ...(entity ? { entity } : {}), limit }
  const log = useListAuditLog(params, { query: { select: (r) => r.data } })
  const rows = log.data ?? []
  const ar = settings.language === 'ar'

  const columns: ColumnsType<AuditEntryDto> = [
    { title: t('security.audit.when'), key: 'at', width: 170, render: (_: unknown, r) => <bdi dir="ltr">{formatDateTime(r.at, settings.digits)}</bdi> },
    { title: t('security.audit.who'), dataIndex: 'userId', key: 'user', width: 150 },
    {
      title: t('security.audit.what'),
      key: 'action',
      width: 120,
      render: (_: unknown, r) => <Tag color={r.action === 'Created' ? 'green' : r.action === 'Deleted' ? 'red' : 'blue'}>{t(`security.audit.action.${r.action}`)}</Tag>,
    },
    {
      title: t('security.audit.record'),
      key: 'record',
      render: (_: unknown, r) => (
        <span>
          {ar ? r.entityLabelAr : r.entityLabelEn} {r.reference && <bdi dir="auto">{r.reference}</bdi>}
        </span>
      ),
    },
  ]

  return (
    <ListPage
      title={t('security.audit.title')}
      help="auditLog"
      crumbs={[{ label: t('breadcrumb.home'), to: '/' }, { label: t('security.audit.title') }]}
      actions={<ExportControls run={(format, layout) => exportAndShow('audit-log', { From: from, To: to, User: user.trim() || undefined, Entity: entity }, format, layout)} />}
      filters={
        <>
          <label>
            {t('security.audit.from')} <DateField value={from} onChange={setFrom} allowEmpty ariaLabel={t('security.audit.from')} />
          </label>
          <label>
            {t('security.audit.to')} <DateField value={to} onChange={setTo} allowEmpty ariaLabel={t('security.audit.to')} />
          </label>
          <Input value={user} onChange={(e) => setUser(e.target.value)} placeholder={t('security.audit.userFilter')} aria-label={t('security.audit.userFilter')} allowClear className="filter-input" />
          <Select
            value={entity}
            onChange={setEntity}
            allowClear
            placeholder={t('security.audit.kindFilter')}
            aria-label={t('security.audit.kindFilter')}
            className="filter-select"
            options={entityKinds.map((k) => ({ value: k, label: t(`security.audit.kinds.${k}`) }))}
          />
        </>
      }
    >
      {log.isError && <Alert type="error" showIcon message={errorMessage(log.error, t)} className="form-alert" />}
      {log.isSuccess && rows.length === 0 ? (
        <EmptyState title={t('security.audit.emptyTitle')} body={t('security.audit.emptyBody')} />
      ) : (
        <>
          <Table<AuditEntryDto>
            columns={columns}
            dataSource={[...rows]}
            rowKey="id"
            loading={log.isPending}
            pagination={{ pageSize: 50, showSizeChanger: false }}
            size="middle"
            bordered
            expandable={{
              rowExpandable: (r) => r.changes.length > 0,
              expandedRowRender: (r) => (
                <table className="audit-changes">
                  <thead>
                    <tr>
                      <th scope="col" className="audit-head">{t('security.audit.field')}</th>
                      <th scope="col" className="audit-head">{t('security.audit.before')}</th>
                      <th scope="col" className="audit-head">{t('security.audit.after')}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {r.changes.map((c) => (
                      <tr key={c.field}>
                        <th scope="row" dir="ltr" className="audit-head">
                          {fieldLabel(c.field)}
                        </th>
                        <td dir="auto" className="audit-cell">{c.before ?? <span className="muted">–</span>}</td>
                        <td dir="auto" className="audit-cell">{c.after ?? <span className="muted">–</span>}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              ),
            }}
          />
          {rows.length >= limit && <p className="muted">{t('security.audit.limited', { count: limit })}</p>}
        </>
      )}
    </ListPage>
  )
}
