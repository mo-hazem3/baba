import { CheckCircleFilled, CloseCircleFilled } from '@ant-design/icons'
import { Table } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate } from 'react-router'
import type { ReportResult, ReportRow } from '../../api/generated/model'
import { AmountText } from '../../layout/AmountText'
import { useSettings } from '../../settings/SettingsContext'
import { formatDate, parseIsoDate } from '../../utils/format'
import { cellText, drillPath } from './reportModel'

/**
 * Shows any report (brief section 11). Group and total rows are bold, tree levels are indented, amounts line up, and every row with a
 * link can be clicked to drill down: a figure to its account statement, a statement line to its voucher.
 */
export function ReportTable({ report }: { report: ReportResult }) {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const navigate = useNavigate()
  const language = settings.language

  // The text that carries the link is the first one in the row that has any.
  const linkColumn = (row: ReportRow) => row.cells.findIndex((c) => cellText(c, language).length > 0)

  const columns: ColumnsType<ReportRow> = report.columns.map((column, index) => ({
    key: column.key,
    title: language === 'ar' ? column.titleAr : column.titleEn,
    align: column.kind === 'Amount' ? 'end' : 'start',
    // Names need room (they are indented by level); amounts line up in equal columns. The table scrolls inside its box if the screen is narrow.
    width: column.kind === 'Amount' ? 108 : column.key === 'name' ? 200 : column.key === 'code' ? 80 : undefined,
    className: column.kind === 'Amount' ? 'numbers' : undefined,
    render: (_: unknown, row: ReportRow) => {
      const cell = row.cells[index]
      if (column.kind === 'Amount')
        return cell?.amount === null || cell?.amount === undefined ? null : <AmountText value={cell.amount} minorUnits={report.minorUnits} />

      const text = cell?.date ? formatDate(parseIsoDate(cell.date), settings.digits, settings.hijri) : cellText(cell, language)
      const indented = column.key === 'name' && (row.level ?? 0) > 0 ? { paddingInlineStart: `${(row.level ?? 0) * 18}px` } : undefined
      const content = row.link && linkColumn(row) === index ? <Link to={drillPath(row.link)}>{text}</Link> : text
      return <span style={indented}>{content}</span>
    },
  }))

  return (
    <div className="report">
      <Table<ReportRow>
        columns={columns}
        dataSource={[...report.rows]}
        rowKey={(_, index) => String(index)}
        pagination={false}
        size="middle"
        bordered
        sticky
        scroll={{ x: 'max-content' }}
        rowClassName={(row) => `report-row report-${(row.style ?? 'Normal').toLowerCase()}`}
        onRow={(row) => ({ onClick: () => row.link && navigate(drillPath(row.link)), style: row.link ? { cursor: 'pointer' } : undefined })}
      />
      {report.checks.length > 0 && (
        <ul className="report-checks">
          {report.checks.map((check) => (
            <li key={check.textEn} className={check.passed ? 'check-ok' : 'check-bad'}>
              {check.passed ? <CheckCircleFilled /> : <CloseCircleFilled />} {language === 'ar' ? check.textAr : check.textEn}
              {!check.passed && <strong> — {t('reports.checkFailed')}</strong>}
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
