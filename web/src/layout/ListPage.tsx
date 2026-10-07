import { Button, Space } from 'antd'
import type { ReactNode } from 'react'
import { PageHeader, type Crumb } from './PageHeader'
import type { HelpTopic } from './HelpLink'

/**
 * Every list screen has the same shape (brief section 7.3): the title with the one obvious "+ New" button, a row of filters, then
 * the list. Clicking a row opens it.
 */
export function ListPage({
  title,
  help,
  crumbs,
  newLabel,
  onNew,
  actions,
  filters,
  children,
}: {
  title: string
  help: HelpTopic
  crumbs?: Crumb[]
  newLabel?: string
  onNew?: () => void
  /** Other buttons next to "+ New", for example Export. */
  actions?: ReactNode
  filters?: ReactNode
  children: ReactNode
}) {
  return (
    <div>
      <PageHeader
        title={title}
        help={help}
        crumbs={crumbs}
        action={
          <Space wrap>
            {actions}
            {onNew && (
              <Button type="primary" onClick={onNew}>
                + {newLabel}
              </Button>
            )}
          </Space>
        }
      />
      {filters && <div className="filter-bar">{filters}</div>}
      {children}
    </div>
  )
}
