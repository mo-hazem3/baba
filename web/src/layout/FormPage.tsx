import { Button } from 'antd'
import type { ReactNode } from 'react'
import { PageHeader, type Crumb } from './PageHeader'
import type { HelpTopic } from './HelpLink'

export interface FormAction {
  label: string
  onClick: () => void
  loading?: boolean
  disabled?: boolean
  /** Why the button is off, shown as its tooltip text. */
  title?: string
}

/**
 * Every form screen has the same shape (brief section 7.3): a full page (not a popup) with the buttons always in the same
 * place and order: Save (the main one), Save as draft, Print, Cancel. A destructive Delete is kept apart on the other side and is red.
 */
export function FormPage({
  title,
  help,
  crumbs,
  badge,
  children,
  save,
  saveAsDraft,
  print,
  cancel,
  remove,
}: {
  title: string
  help: HelpTopic
  crumbs?: Crumb[]
  /** A small status next to the title, such as "Draft" or "Posted". */
  badge?: ReactNode
  children: ReactNode
  save?: FormAction
  saveAsDraft?: FormAction
  print?: FormAction
  cancel?: FormAction
  remove?: FormAction
}) {
  const button = (action: FormAction | undefined, props: { type?: 'primary'; danger?: boolean } = {}) =>
    action && (
      <Button {...props} onClick={action.onClick} loading={action.loading} disabled={action.disabled} title={action.title}>
        {action.label}
      </Button>
    )

  return (
    <div className="form-page">
      <PageHeader title={title} help={help} crumbs={crumbs} action={badge} />
      {children}
      <div className="form-bar">
        <div className="form-bar-main">
          {button(save, { type: 'primary' })}
          {button(saveAsDraft)}
          {button(print)}
          {button(cancel)}
        </div>
        <div className="form-bar-danger">{button(remove, { danger: true })}</div>
      </div>
    </div>
  )
}
