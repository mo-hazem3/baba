import { Alert, App, Button, Form, Input, InputNumber, Modal, Segmented, Select, Space, Table, Tag } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useState } from 'react'
import type { TFunction } from 'i18next'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import {
  createAsset,
  deleteAsset,
  disposeAsset,
  runDepreciation,
  undoAssetDisposal,
  undoLastDepreciation,
  updateAsset,
} from '../../api/generated/baba'
import type { AssetDto, AssetInput, DepreciationMethod, DisposeInput } from '../../api/generated/model'
import { ApiError } from '../../api/http'
import { refreshBooks, useAccounts, useAssets, useCostCenters, useCurrencies, useCurrentCompany, useModules } from '../../api/hooks'
import { AmountText } from '../../layout/AmountText'
import { DateField } from '../../layout/DateField'
import { EmptyState } from '../../layout/EmptyState'
import { errorMessage } from '../../layout/errors'
import { ExportControls } from '../../layout/ExportControls'
import { ImportModal } from '../../layout/ImportModal'
import { ListPage } from '../../layout/ListPage'
import { useShortcuts } from '../../layout/useShortcuts'
import { useSettings } from '../../settings/SettingsContext'
import { formatDate, toIsoDate } from '../../utils/format'
import { AccountSelect } from '../accounting/AccountSelect'
import { CostCenterSelect, itemName } from '../accounting/LookupSelects'
import { exportAndShow } from '../reports/exportReport'

/** The last day of the month before the current one: what has ended and can be depreciated without asking. */
const lastMonthEnd = () => {
  const now = new Date()
  return toIsoDate(new Date(now.getFullYear(), now.getMonth(), 0))
}

const problemText = (error: unknown, t: TFunction) => {
  const known = error instanceof ApiError ? error.issues[0] : undefined
  return known ? t([`assets.issues.${known.code}`, `voucher.issues.${known.code}`], { defaultValue: errorMessage(error, t) }) : errorMessage(error, t)
}

/** Fixed and intangible assets (brief section 10.4): the register, monthly depreciation, and disposals. */
export function AssetsPage() {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const { message, modal } = App.useApp()
  const queryClient = useQueryClient()
  const query = useAssets()
  const assets = useMemo(() => query.data ?? [], [query.data])
  const company = useCurrentCompany()
  const currencies = useCurrencies()
  const minorUnits = currencies.data?.find((c) => c.code === company.data?.baseCurrencyCode)?.minorUnits ?? 2
  const [form, setForm] = useState<{ open: boolean; editing?: AssetDto }>({ open: false })
  const [disposing, setDisposing] = useState<AssetDto>()
  const [running, setRunning] = useState(false)
  const [importing, setImporting] = useState(false)

  const remove = useMutation({
    mutationFn: (a: AssetDto) => deleteAsset(a.id),
    onSuccess: async () => {
      await refreshBooks(queryClient)
      void message.success(t('assets.deleted'))
    },
    onError: (error) => void message.error(problemText(error, t)),
  })
  const undoDisposal = useMutation({
    mutationFn: (a: AssetDto) => undoAssetDisposal(a.id),
    onSuccess: () => refreshBooks(queryClient),
    onError: (error) => void message.error(problemText(error, t)),
  })
  const undoLast = useMutation({
    mutationFn: () => undoLastDepreciation(),
    onSuccess: async () => {
      await refreshBooks(queryClient)
      void message.success(t('assets.undone'))
    },
    onError: (error) => void message.error(problemText(error, t)),
  })

  useShortcuts({ 'ctrl+n': () => setForm({ open: true }) })

  const columns: ColumnsType<AssetDto> = [
    { title: t('parties.code'), dataIndex: 'code', width: 110, render: (code: string) => <span dir="ltr">{code}</span> },
    { title: t('parties.name'), key: 'name', render: (_: unknown, a) => <span className={a.status === 'Disposed' ? 'muted' : ''}>{itemName(a, settings.language)}</span> },
    { title: t('assets.type'), key: 'kind', width: 120, render: (_: unknown, a) => t(`assets.kinds.${a.kind}`) },
    { title: t('assets.acquired'), key: 'date', width: 120, render: (_: unknown, a) => formatDate(a.acquisitionDate, settings.digits, settings.hijri) },
    { title: t('assets.cost'), key: 'cost', width: 120, align: 'end', render: (_: unknown, a) => <AmountText value={a.cost} minorUnits={minorUnits} /> },
    { title: t('assets.depreciation'), key: 'accumulated', width: 120, align: 'end', render: (_: unknown, a) => <AmountText value={a.accumulated} minorUnits={minorUnits} /> },
    { title: t('assets.bookValue'), key: 'book', width: 120, align: 'end', render: (_: unknown, a) => <AmountText value={a.bookValue} minorUnits={minorUnits} /> },
    { title: t('parties.status'), key: 'status', width: 110, render: (_: unknown, a) => (a.status === 'Disposed' ? <Tag>{t('assets.disposed')}</Tag> : t('assets.inUse')) },
    {
      title: <span className="visually-hidden">{t('voucher.rowActions')}</span>,
      key: 'actions',
      width: 300,
      render: (_: unknown, a) => (
        <Space size={0} wrap>
          <Button type="link" onClick={() => setForm({ open: true, editing: a })} aria-label={`${t('parties.edit')} ${a.code}`}>
            {t('parties.edit')}
          </Button>
          {a.status === 'Active' ? (
            <Button type="link" onClick={() => setDisposing(a)} aria-label={`${t('assets.dispose')} ${a.code}`}>
              {t('assets.dispose')}
            </Button>
          ) : (
            <Button type="link" onClick={() => undoDisposal.mutate(a)} aria-label={`${t('assets.undoDisposal')} ${a.code}`}>
              {t('assets.undoDisposal')}
            </Button>
          )}
          {!a.isLocked && (
            <Button
              type="link"
              danger
              aria-label={`${t('parties.delete')} ${a.code}`}
              onClick={() =>
                modal.confirm({
                  title: t('assets.deleteTitle', { name: `${a.code} ${itemName(a, settings.language)}` }),
                  okText: t('parties.delete'),
                  okButtonProps: { danger: true },
                  cancelText: t('common.cancel'),
                  onOk: () => remove.mutateAsync(a).catch(() => undefined),
                })
              }
            >
              {t('parties.delete')}
            </Button>
          )}
        </Space>
      ),
    },
  ]

  return (
    <ListPage
      title={t('assets.title')}
      help="assets"
      newLabel={t('assets.new')}
      onNew={() => setForm({ open: true })}
      actions={
        <Space wrap>
          <ExportControls run={(format, layout) => exportAndShow('assets', {}, format, layout)} />
          <Button onClick={() => setImporting(true)}>{t('assets.import')}</Button>
          <Link to="/reports/asset-register">
            <Button>{t('assets.register')}</Button>
          </Link>
          <Button onClick={() => undoLast.mutate()} loading={undoLast.isPending}>
            {t('assets.undoLast')}
          </Button>
          <Button onClick={() => setRunning(true)}>{t('assets.runDepreciation')}</Button>
        </Space>
      }
    >
      {query.isSuccess && assets.length === 0 ? (
        <EmptyState title={t('assets.emptyTitle')} body={t('assets.emptyBody')} />
      ) : (
        <Table<AssetDto> columns={columns} dataSource={assets} rowKey="id" loading={query.isPending} pagination={false} size="middle" bordered />
      )}
      <AssetFormModal open={form.open} editing={form.editing} onClose={() => setForm({ open: false })} />
      <DisposeModal asset={disposing} onClose={() => setDisposing(undefined)} />
      <RunModal open={running} onClose={() => setRunning(false)} />
      <ImportModal
        open={importing}
        onClose={() => setImporting(false)}
        title={t('assets.importTitle')}
        intro={t('assets.importIntro')}
        columns="Code, Name, Name (Arabic), Type, Acquisition date, Cost, Salvage value, Useful life (months), Method, Annual rate (%), Asset account, Accumulated depreciation account, Depreciation expense account, Depreciation so far, Depreciated through"
        url="/api/import/assets"
        templateKey="assets"
      />
    </ListPage>
  )
}

function RunModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  return open ? <RunDialog onClose={onClose} /> : null
}

function RunDialog({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const { message } = App.useApp()
  const [through, setThrough] = useState<string | undefined>(lastMonthEnd())
  const [problem, setProblem] = useState<string>()

  const run = useMutation({
    mutationFn: () => runDepreciation({ through: through! }),
    onSuccess: async (response) => {
      await refreshBooks(queryClient)
      const items = response.data.items
      const failed = items.find((i) => i.problemCode)
      if (failed) {
        setProblem(t(`assets.issues.${failed.problemCode}`, { defaultValue: t(`voucher.issues.${failed.problemCode}`, { defaultValue: failed.problemCode ?? '' }) }))
        return
      }
      void message.success(items.length === 0 ? t('assets.nothingToDepreciate') : t('assets.depreciated', { count: items.length }))
      onClose()
    },
    onError: (error) => setProblem(problemText(error, t)),
  })

  return (
    <Modal
      open
      title={t('assets.runDepreciation')}
      onCancel={onClose}
      onOk={() => run.mutate()}
      okText={t('assets.run')}
      okButtonProps={{ disabled: !through }}
      cancelText={t('common.cancel')}
      confirmLoading={run.isPending}
      maskClosable={false}
    >
      <p>{t('assets.runIntro')}</p>
      {problem && <Alert type="error" showIcon message={problem} className="form-alert" />}
      <div className="field">
        <label htmlFor="run-through">{t('assets.through')}</label>
        <DateField id="run-through" value={through} onChange={setThrough} ariaLabel={t('assets.through')} />
      </div>
    </Modal>
  )
}

function DisposeModal({ asset, onClose }: { asset?: AssetDto; onClose: () => void }) {
  return asset ? <DisposeDialog asset={asset} onClose={onClose} /> : null
}

function DisposeDialog({ asset, onClose }: { asset: AssetDto; onClose: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const accounts = useAccounts().data ?? []
  const [date, setDate] = useState<string | undefined>(toIsoDate(new Date()))
  const [proceeds, setProceeds] = useState<number | null>(0)
  const [proceedsAccountId, setProceedsAccountId] = useState<string>()
  const [gainLossAccountId, setGainLossAccountId] = useState<string>()
  const [problem, setProblem] = useState<string>()

  const dispose = useMutation({
    mutationFn: () => {
      const input: DisposeInput = { date: date!, proceeds: proceeds ?? 0, proceedsAccountId: proceedsAccountId ?? null, gainLossAccountId: gainLossAccountId ?? null }
      return disposeAsset(asset.id, input)
    },
    onSuccess: async () => {
      await refreshBooks(queryClient)
      onClose()
    },
    onError: (error) => setProblem(problemText(error, t)),
  })

  return (
    <Modal
      open
      title={t('assets.disposeTitle', { name: `${asset.code} ${asset.nameEn || asset.nameAr}` })}
      onCancel={onClose}
      onOk={() => dispose.mutate()}
      okText={t('assets.dispose')}
      okButtonProps={{ disabled: !date }}
      cancelText={t('common.cancel')}
      confirmLoading={dispose.isPending}
      maskClosable={false}
    >
      <p>{t('assets.disposeIntro')}</p>
      {problem && <Alert type="error" showIcon message={problem} className="form-alert" />}
      <div className="field">
        <label htmlFor="dispose-date">{t('voucher.date')}</label>
        <DateField id="dispose-date" value={date} onChange={setDate} ariaLabel={t('voucher.date')} />
      </div>
      <div className="field">
        <label htmlFor="dispose-proceeds">{t('assets.proceeds')}</label>
        <InputNumber id="dispose-proceeds" value={proceeds} onChange={setProceeds} min={0} precision={3} controls={false} className="amount-input" />
      </div>
      {(proceeds ?? 0) > 0 && (
        <div className="field field-wide">
          <label htmlFor="dispose-account">{t('assets.proceedsAccount')}</label>
          <AccountSelect id="dispose-account" value={proceedsAccountId} onChange={setProceedsAccountId} accounts={accounts} cashOnly ariaLabel={t('assets.proceedsAccount')} />
        </div>
      )}
      <div className="field field-wide">
        <label htmlFor="dispose-gain">{t('assets.gainLossAccount')}</label>
        <AccountSelect id="dispose-gain" value={gainLossAccountId} onChange={setGainLossAccountId} accounts={accounts.filter((a) => a.type === 'Expense' || a.type === 'Revenue')} ariaLabel={t('assets.gainLossAccount')} />
        <span className="muted">{t('assets.gainLossHelp')}</span>
      </div>
    </Modal>
  )
}

interface Values {
  code: string
  nameEn: string
  nameAr: string
  kind: AssetInput['kind']
  acquisitionDate: string
  cost: number
  salvage: number
  method: DepreciationMethod
  usefulLifeMonths: number
  annualRate: number
  assetAccountId?: string
  accumulatedAccountId?: string
  expenseAccountId?: string
  costCenterId?: string
  openingAccumulated: number
  depreciatedThrough?: string
  notes?: string
}

const fieldOf: Record<string, keyof Values> = {
  code: 'code',
  name: 'nameEn',
  acquisitionDate: 'acquisitionDate',
  cost: 'cost',
  salvage: 'salvage',
  usefulLife: 'usefulLifeMonths',
  annualRate: 'annualRate',
  assetAccount: 'assetAccountId',
  accumulatedAccount: 'accumulatedAccountId',
  expenseAccount: 'expenseAccountId',
  openingAccumulated: 'openingAccumulated',
  costCenter: 'costCenterId',
}

function AssetFormModal({ open, editing, onClose }: { open: boolean; editing?: AssetDto; onClose: () => void }) {
  return open ? <AssetForm editing={editing} onClose={onClose} /> : null
}

function AssetForm({ editing, onClose }: { editing?: AssetDto; onClose: () => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [form] = Form.useForm<Values>()
  const accounts = useAccounts().data ?? []
  const costCenters = useCostCenters().data ?? []
  const assets = useAssets().data ?? []
  const modules = useModules()
  const method = Form.useWatch('method', form)
  const [problems, setProblems] = useState<string[]>([])
  const locked = editing?.isLocked ?? false

  useEffect(() => {
    const next = Math.max(0, ...assets.map((a) => Number(/^\D*(\d+)$/.exec(a.code)?.[1] ?? 0))) + 1
    form.setFieldsValue(
      editing
        ? {
            code: editing.code,
            nameEn: editing.nameEn,
            nameAr: editing.nameAr,
            kind: editing.kind,
            acquisitionDate: editing.acquisitionDate,
            cost: editing.cost,
            salvage: editing.salvage,
            method: editing.method,
            usefulLifeMonths: editing.usefulLifeMonths,
            annualRate: editing.annualRate,
            assetAccountId: editing.assetAccountId,
            accumulatedAccountId: editing.accumulatedAccountId,
            expenseAccountId: editing.expenseAccountId,
            costCenterId: editing.costCenterId ?? undefined,
            openingAccumulated: editing.openingAccumulated,
            depreciatedThrough: editing.depreciatedThrough ?? undefined,
            notes: editing.notes ?? '',
          }
        : {
            code: `A${String(next).padStart(3, '0')}`,
            nameEn: '',
            nameAr: '',
            kind: 'Tangible',
            acquisitionDate: toIsoDate(new Date()),
            cost: 0,
            salvage: 0,
            method: 'StraightLine',
            usefulLifeMonths: 60,
            annualRate: 20,
            openingAccumulated: 0,
          },
    )
    // eslint-disable-next-line react-hooks/exhaustive-deps -- the suggested code is worked out when the window opens
  }, [editing, form])

  const save = useMutation({
    mutationFn: (values: Values) => {
      const input: AssetInput = {
        code: values.code,
        nameEn: values.nameEn ?? '',
        nameAr: values.nameAr ?? '',
        kind: values.kind,
        acquisitionDate: values.acquisitionDate,
        cost: values.cost ?? 0,
        salvage: values.salvage ?? 0,
        usefulLifeMonths: values.method === 'StraightLine' ? (values.usefulLifeMonths ?? 0) : 0,
        method: values.method,
        annualRate: values.method === 'DecliningBalance' ? (values.annualRate ?? 0) : 0,
        assetAccountId: values.assetAccountId ?? null,
        accumulatedAccountId: values.accumulatedAccountId ?? null,
        expenseAccountId: values.expenseAccountId ?? null,
        costCenterId: values.costCenterId ?? null,
        openingAccumulated: values.openingAccumulated ?? 0,
        depreciatedThrough: values.depreciatedThrough ?? null,
        notes: values.notes?.trim() || null,
      }
      return editing ? updateAsset(editing.id, input) : createAsset(input)
    },
    onSuccess: async () => {
      await refreshBooks(queryClient)
      onClose()
    },
    onError: (error) => {
      if (!(error instanceof ApiError) || error.issues.length === 0) {
        setProblems([errorMessage(error, t)])
        return
      }
      form.setFields(error.issues.filter((i) => fieldOf[i.field]).map((issue) => ({ name: fieldOf[issue.field]!, errors: [t([`assets.issues.${issue.code}`], { defaultValue: issue.code })] })))
      setProblems(error.issues.filter((i) => !fieldOf[i.field]).map((i) => t([`assets.issues.${i.code}`], { defaultValue: i.code })))
    },
  })

  return (
    <Modal
      open
      title={editing ? t('assets.edit') : t('assets.new')}
      onCancel={onClose}
      onOk={() => form.submit()}
      okText={t('common.save')}
      cancelText={t('common.cancel')}
      confirmLoading={save.isPending}
      maskClosable={false}
      width={700}
    >
      <Form form={form} layout="vertical" onFinish={(values) => save.mutate(values)} requiredMark={false}>
        {problems.length > 0 && <Alert type="error" showIcon className="form-alert" message={problems.join(' ')} />}
        {locked && <Alert type="info" showIcon className="form-alert" message={t('assets.lockedNote')} />}
        <div className="form-row">
          <Form.Item name="code" label={t('parties.code')} rules={[{ required: true, message: t('assets.issues.asset.code-required') }]}>
            <Input dir="ltr" autoFocus />
          </Form.Item>
          <Form.Item name="kind" label={t('assets.type')}>
            <Segmented
              disabled={locked}
              options={[
                { value: 'Tangible', label: t('assets.kinds.Tangible') },
                { value: 'Intangible', label: t('assets.kinds.Intangible') },
              ]}
              aria-label={t('assets.type')}
            />
          </Form.Item>
        </div>
        <Form.Item name="nameEn" label={t('parties.nameEn')} extra={t('parties.nameHelp')}>
          <Input dir="ltr" />
        </Form.Item>
        <Form.Item name="nameAr" label={t('parties.nameAr')}>
          <Input dir="rtl" />
        </Form.Item>
        <div className="form-row">
          <Form.Item name="acquisitionDate" label={t('assets.acquired')}>
            <DateField value={undefined} onChange={() => undefined} disabled={locked} ariaLabel={t('assets.acquired')} />
          </Form.Item>
          <Form.Item name="cost" label={t('assets.cost')}>
            <InputNumber min={0} precision={3} controls={false} className="amount-input" disabled={locked} />
          </Form.Item>
          <Form.Item name="salvage" label={t('assets.salvage')} extra={t('assets.salvageHelp')}>
            <InputNumber min={0} precision={3} controls={false} className="amount-input" disabled={locked} />
          </Form.Item>
        </div>
        <div className="form-row">
          <Form.Item name="method" label={t('assets.method')}>
            <Select
              disabled={locked}
              options={[
                { value: 'StraightLine', label: t('assets.methods.StraightLine') },
                { value: 'DecliningBalance', label: t('assets.methods.DecliningBalance') },
              ]}
              aria-label={t('assets.method')}
            />
          </Form.Item>
          {method === 'DecliningBalance' ? (
            <Form.Item name="annualRate" label={t('assets.annualRate')} extra={t('assets.annualRateHelp')}>
              <InputNumber min={0} max={100} precision={2} controls={false} className="amount-input" disabled={locked} />
            </Form.Item>
          ) : (
            <Form.Item name="usefulLifeMonths" label={t('assets.life')} extra={t('assets.lifeHelp')}>
              <InputNumber min={1} precision={0} controls={false} className="amount-input" disabled={locked} />
            </Form.Item>
          )}
        </div>
        <Form.Item name="assetAccountId" label={t('assets.assetAccount')} extra={t('assets.assetAccountHelp')}>
          <AccountSelect value={undefined} onChange={() => undefined} accounts={accounts.filter((a) => a.type === 'Asset')} disabled={locked} ariaLabel={t('assets.assetAccount')} />
        </Form.Item>
        <Form.Item name="accumulatedAccountId" label={t('assets.accumulatedAccount')} extra={t('assets.defaultAccountHelp')}>
          <AccountSelect value={undefined} onChange={() => undefined} accounts={accounts.filter((a) => a.type === 'Asset')} disabled={locked} ariaLabel={t('assets.accumulatedAccount')} />
        </Form.Item>
        <Form.Item name="expenseAccountId" label={t('assets.expenseAccount')} extra={t('assets.defaultAccountHelp')}>
          <AccountSelect value={undefined} onChange={() => undefined} accounts={accounts.filter((a) => a.type === 'Expense')} disabled={locked} ariaLabel={t('assets.expenseAccount')} />
        </Form.Item>
        {modules.has('cost-centers') && (
          <Form.Item name="costCenterId" label={t('reports.costCenter')}>
            <CostCenterSelect value={undefined} onChange={() => undefined} costCenters={costCenters} ariaLabel={t('reports.costCenter')} />
          </Form.Item>
        )}
        <p className="muted">{t('assets.alreadyInUse')}</p>
        <div className="form-row">
          <Form.Item name="openingAccumulated" label={t('assets.depreciationSoFar')}>
            <InputNumber min={0} precision={3} controls={false} className="amount-input" disabled={locked} />
          </Form.Item>
          <Form.Item name="depreciatedThrough" label={t('assets.depreciatedThrough')} extra={t('assets.depreciatedThroughHelp')}>
            <DateField value={undefined} onChange={() => undefined} allowEmpty disabled={locked} ariaLabel={t('assets.depreciatedThrough')} />
          </Form.Item>
        </div>
        <Form.Item name="notes" label={t('inventory.notes')}>
          <Input />
        </Form.Item>
      </Form>
    </Modal>
  )
}

