import { Empty, Typography } from 'antd'
import type { ReactNode } from 'react'

/** An empty list says what to do next, never just "no data" (brief section 7.4). */
export function EmptyState({ title, body, action }: { title: string; body: string; action?: ReactNode }) {
  return (
    <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={<Typography.Text strong>{title}</Typography.Text>}>
      <p className="empty-body">{body}</p>
      {action}
    </Empty>
  )
}
