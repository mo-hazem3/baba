import { Button, Card } from 'antd'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { PageHeader } from '../../layout/PageHeader'
import { useCapabilities, useModules } from '../../api/hooks'
import { reportKeys, reportModule, reportNeedsTaxReturn } from './reportModel'

/** All the reports in one place (brief section 10.1). Each opens with the current fiscal year chosen. */
export function ReportsPage() {
  const { t } = useTranslation()
  const modules = useModules()
  const capabilities = useCapabilities()

  return (
    <div>
      <PageHeader
        title={t('reports.title')}
        help="reports"
        crumbs={[{ label: t('breadcrumb.home'), to: '/' }, { label: t('reports.title') }]}
      />
      <div className="report-cards">
        {reportKeys.filter((key) => (!reportModule[key] || modules.has(reportModule[key])) && (!reportNeedsTaxReturn(key) || capabilities?.hasTaxReturn)).map((key) => (
          <Card key={key} title={t(`reports.names.${key}`)} className="report-card">
            <p>{t(`reports.descriptions.${key}`)}</p>
            <Link to={`/reports/${key}`}>
              <Button type="primary" aria-label={`${t('common.open')} ${t(`reports.names.${key}`)}`}>
                {t('common.open')}
              </Button>
            </Link>
          </Card>
        ))}
      </div>
    </div>
  )
}
