import { App, Card, Checkbox } from 'antd'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { setEnabledModules } from '../../api/generated/baba'
import { refreshCompany, useCurrentCompany } from '../../api/hooks'
import { errorMessage } from '../../layout/errors'

/** The optional modules that exist so far. A module that is not built yet is not offered here (the new-company wizard lists them all). */
export const availableModules = ['bank-cash', 'customers-suppliers', 'cost-centers', 'sales', 'purchases', 'inventory', 'fixed-assets', 'payroll', 'expense-claims', 'budgets'] as const

/**
 * Switching optional parts of Baba on or off after the company was made (brief section 10). Switching one off only hides its
 * screens: nothing recorded is deleted, and it all comes back when the module is switched on again.
 */
export function ModulesCard() {
  const { t } = useTranslation()
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const company = useCurrentCompany()
  const enabled = company.data?.enabledModules ?? []

  const change = useMutation({
    mutationFn: (modules: string[]) => setEnabledModules({ modules }),
    onSuccess: () => refreshCompany(queryClient),
    onError: (error) => void message.error(errorMessage(error, t)),
  })

  return (
    <Card title={t('settings.modules.title')} className="settings-card">
      <p>{t('settings.modules.intro')}</p>
      <Checkbox.Group
        className="vertical-options"
        value={enabled.filter((m) => (availableModules as readonly string[]).includes(m))}
        disabled={change.isPending}
        onChange={(values) =>
          change.mutate([...enabled.filter((m) => !(availableModules as readonly string[]).includes(m)), ...(values as string[])])
        }
      >
        {availableModules.map((key) => (
          <Checkbox key={key} value={key}>
            <strong>{t(`modules.${key}.name`)}</strong> <span className="muted">{t(`modules.${key}.description`)}</span>
          </Checkbox>
        ))}
      </Checkbox.Group>
    </Card>
  )
}
