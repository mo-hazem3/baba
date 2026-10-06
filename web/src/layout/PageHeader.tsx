import { Breadcrumb, Typography } from 'antd'
import type { ReactNode } from 'react'
import { Link } from 'react-router'
import { HelpLink, type HelpTopic } from './HelpLink'

export interface Crumb {
  label: string
  to?: string
}

/** The top of every page: breadcrumbs, the title with its help link, and the one main action (brief section 7.3). */
export function PageHeader({
  title,
  crumbs,
  help,
  action,
}: {
  title: string
  crumbs?: Crumb[]
  help: HelpTopic
  action?: ReactNode
}) {
  return (
    <header className="page-header">
      {crumbs && crumbs.length > 0 && (
        <Breadcrumb
          items={crumbs.map((c) => ({ title: c.to ? <Link to={c.to}>{c.label}</Link> : c.label }))}
        />
      )}
      <div className="page-header-row">
        <Typography.Title level={1} className="page-title">
          {title}
        </Typography.Title>
        <HelpLink topic={help} />
        <div className="page-header-action">{action}</div>
      </div>
    </header>
  )
}
