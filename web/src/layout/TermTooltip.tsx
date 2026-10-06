import { InfoCircleOutlined } from '@ant-design/icons'
import { Tooltip } from 'antd'
import { useTranslation } from 'react-i18next'

export type Term = 'fiscalYear' | 'baseCurrency' | 'chartOfAccounts' | 'taxRegistration' | 'modules' | 'filePassword'

/** The ⓘ next to an accounting term: a plain-words explanation, reachable by mouse or keyboard (brief section 7.2). */
export function TermTooltip({ term }: { term: Term }) {
  const { t } = useTranslation()
  const text = t(`terms.${term}`)

  return (
    <Tooltip title={text}>
      <InfoCircleOutlined tabIndex={0} role="img" aria-label={text} className="term-tooltip" />
    </Tooltip>
  )
}
