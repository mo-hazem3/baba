import { Alert, Checkbox, Input, Modal, Select } from 'antd'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { createRecurringFromDocument, createRecurringFromVoucher, updateRecurring } from '../../api/generated/baba'
import type { RecurrenceFrequency, RecurringDto } from '../../api/generated/model'
import { ApiError } from '../../api/http'
import { refreshBooks } from '../../api/hooks'
import { DateField } from '../../layout/DateField'
import { errorMessage } from '../../layout/errors'
import { toIsoDate } from '../../utils/format'

export const frequencies: RecurrenceFrequency[] = ['Weekly', 'Monthly', 'Quarterly', 'Yearly']

/** What a new schedule is made from: a saved document or voucher, copied as it is now. */
export type RecurringSource = { type: 'document' | 'voucher'; id: string; name: string }

/**
 * Set up (or change) a schedule (brief section 10.3): how often, from which date, until when, and whether what is made is issued
 * straight away or left as a draft to check first.
 */
export function RecurringModal({
  open,
  source,
  editing,
  onClose,
  onSaved,
}: {
  open: boolean
  source?: RecurringSource
  editing?: RecurringDto
  onClose: () => void
  onSaved?: () => void
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [name, setName] = useState(editing?.name ?? source?.name ?? '')
  const [frequency, setFrequency] = useState<RecurrenceFrequency>(editing?.frequency ?? 'Monthly')
  const [nextRunDate, setNextRunDate] = useState(editing?.nextRunDate ?? toIsoDate(new Date()))
  const [endDate, setEndDate] = useState<string | undefined>(editing?.endDate ?? undefined)
  const [autoIssue, setAutoIssue] = useState(editing?.autoIssue ?? false)
  const [problems, setProblems] = useState<string[]>([])

  const save = useMutation({
    mutationFn: () => {
      const input = { name, frequency, nextRunDate, endDate: endDate ?? null, autoIssue }
      if (editing) return updateRecurring(editing.id, input)
      return source?.type === 'voucher' ? createRecurringFromVoucher({ sourceId: source.id, input }) : createRecurringFromDocument({ sourceId: source!.id, input })
    },
    onSuccess: async () => {
      await refreshBooks(queryClient)
      onSaved?.()
      onClose()
    },
    onError: (error) => {
      if (error instanceof ApiError && error.issues.length > 0) setProblems(error.issues.map((i) => t(`recurring.issues.${i.code}`, { defaultValue: i.code })))
      else setProblems([errorMessage(error, t)])
    },
  })

  return (
    <Modal
      open={open}
      title={editing ? t('recurring.edit') : t('recurring.new')}
      onCancel={onClose}
      onOk={() => save.mutate()}
      okText={t('common.save')}
      cancelText={t('common.cancel')}
      confirmLoading={save.isPending}
      maskClosable={false}
      destroyOnHidden
      width={560}
    >
      {problems.length > 0 && (
        <Alert type="error" showIcon className="form-alert" message={<ul className="issue-list">{problems.map((p, i) => <li key={i}>{p}</li>)}</ul>} />
      )}
      {!editing && <p>{t('recurring.intro')}</p>}
      <div className="field field-full">
        <label htmlFor="recurring-name">{t('recurring.name')}</label>
        <Input id="recurring-name" value={name} onChange={(e) => setName(e.target.value)} maxLength={100} autoFocus />
      </div>
      <div className="voucher-header modal-fields">
        <div className="field">
          <label htmlFor="recurring-frequency">{t('recurring.frequency')}</label>
          <Select
            id="recurring-frequency"
            value={frequency}
            onChange={setFrequency}
            options={frequencies.map((f) => ({ value: f, label: t(`recurring.frequencies.${f}`) }))}
            aria-label={t('recurring.frequency')}
          />
        </div>
        <div className="field">
          <label htmlFor="recurring-next">{t('recurring.nextRun')}</label>
          <DateField id="recurring-next" value={nextRunDate} onChange={(v) => v && setNextRunDate(v)} ariaLabel={t('recurring.nextRun')} />
        </div>
        <div className="field">
          <label htmlFor="recurring-end">{t('recurring.endDate')}</label>
          <DateField id="recurring-end" value={endDate} onChange={setEndDate} allowEmpty ariaLabel={t('recurring.endDate')} />
        </div>
      </div>
      <Checkbox checked={autoIssue} onChange={(e) => setAutoIssue(e.target.checked)}>
        {t('recurring.autoIssue')}
      </Checkbox>
      <p className="muted">{t('recurring.autoIssueHelp')}</p>
    </Modal>
  )
}
