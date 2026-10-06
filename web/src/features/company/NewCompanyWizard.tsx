import { Alert, App, Button, Card, Checkbox, Form, Input, InputNumber, Radio, Select, Spin, Steps, Typography } from 'antd'
import type { FormInstance } from 'antd'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router'
import { createCompany, pickCompanyFileToSave, useListModules } from '../../api/generated/baba'
import type { CountryDto } from '../../api/generated/model'
import { ApiError } from '../../api/http'
import { refreshCompany, useCountries, useCurrencies, useHost } from '../../api/hooks'
import { errorMessage, issueMessage } from '../../layout/errors'
import { PageHeader } from '../../layout/PageHeader'
import { TermTooltip, type Term } from '../../layout/TermTooltip'
import { useSettings } from '../../settings/SettingsContext'
import { monthName } from '../../utils/format'
import { matchesSearch } from '../../utils/arabic'
import {
  blankChart,
  countryDefaults,
  formFieldOf,
  initialValues,
  minimumPasswordLength,
  stepKeys,
  stepOfField,
  suggestedFileName,
  toRequest,
  type WizardValues,
} from './wizardModel'

interface PendingErrors {
  fields: { name: string[]; errors: string[] }[]
}

const labelWithTerm = (label: string, term: Term) => (
  <span>
    {label} <TermTooltip term={term} />
  </span>
)

/** The new-company wizard (brief section 9): one question group per step, nothing is created until the last step. */
export function NewCompanyWizard() {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [form] = Form.useForm<WizardValues>()
  const [step, setStep] = useState(0)
  const pendingErrors = useRef<PendingErrors | null>(null)
  const [errorVersion, setErrorVersion] = useState(0)

  const countries = useCountries()
  const currencies = useCurrencies()
  const modules = useListModules({ query: { select: (r) => r.data, staleTime: Infinity } })
  const host = useHost()

  // `preserve`: the country field only exists on the first step, but later steps still need to know the country.
  const countryCode = Form.useWatch('countryCode', { form, preserve: true })
  const country = countries.data?.find((c) => c.code === countryCode)

  const create = useMutation({
    mutationFn: (values: WizardValues) => createCompany(toRequest(values)),
    onSuccess: async () => {
      await refreshCompany(queryClient)
      navigate('/')
    },
    onError: (error) => {
      if (!(error instanceof ApiError) || error.issues.length === 0) return
      // Show each server problem on its field, and go to the first step that has one.
      const fields = error.issues.map((issue) => ({
        name: formFieldOf(issue.field),
        errors: [issueMessage(issue, t, { min: minimumPasswordLength })],
        step: stepKeys.indexOf(stepOfField(issue.field)),
      }))
      pendingErrors.current = { fields }
      setStep(Math.min(...fields.map((f) => f.step)))
      setErrorVersion((v) => v + 1)
    },
  })

  // The fields of a step only exist while it is shown, so server messages are applied after the step appears.
  useEffect(() => {
    if (!pendingErrors.current) return
    form.setFields(pendingErrors.current.fields as Parameters<typeof form.setFields>[0])
    pendingErrors.current = null
  }, [errorVersion, step, form])

  const lastStep = step === stepKeys.length - 1

  const onCountryChange = (code: string) => {
    const chosen = countries.data?.find((c) => c.code === code)
    if (chosen) form.setFieldsValue(countryDefaults(chosen))
  }

  const finish = (values: WizardValues) => {
    if (!lastStep) {
      setStep(step + 1)
      return
    }
    create.mutate({ ...form.getFieldsValue(true), ...values })
  }

  if (countries.isPending || currencies.isPending || modules.isPending || host.isPending) {
    return <Spin size="large" className="page-spinner" />
  }

  const stepTitles = stepKeys.map((key) => ({ title: t(`wizard.steps.${key}`) }))

  return (
    <div className="wizard">
      <PageHeader
        title={t('wizard.title')}
        help="wizard"
        crumbs={[{ label: t('breadcrumb.home'), to: '/' }, { label: t('breadcrumb.newCompany') }]}
      />

      <Steps
        current={step}
        items={stepTitles}
        size="small"
        responsive={false}
        onChange={(index) => index < step && !create.isPending && setStep(index)}
        className="wizard-steps"
      />

      <Card className="wizard-card">
        <Form<WizardValues>
          form={form}
          layout="vertical"
          initialValues={initialValues()}
          onFinish={finish}
          requiredMark="optional"
          disabled={create.isPending}
        >
          {stepKeys[step] === 'company' && (
            <CompanyStep countries={countries.data ?? []} currencies={currencies.data ?? []} onCountryChange={onCountryChange} />
          )}
          {stepKeys[step] === 'taxes' && <TaxStep country={country} />}
          {stepKeys[step] === 'address' && (
            <Form.Item name="address" label={t('wizard.address')} extra={t('wizard.addressHelp')}>
              <Input.TextArea rows={4} autoFocus />
            </Form.Item>
          )}
          {stepKeys[step] === 'chart' && <ChartStep country={country} />}
          {stepKeys[step] === 'modules' && <ModulesStep keys={(modules.data ?? []).map((m) => m.key)} />}
          {stepKeys[step] === 'file' && <FileStep form={form} fileDialogs={host.data?.fileDialogs ?? false} />}

          {create.isError && !(create.error instanceof ApiError && create.error.issues.length > 0) && (
            <Alert type="error" showIcon message={errorMessage(create.error, t)} className="form-alert" />
          )}
          {create.isError && create.error instanceof ApiError && create.error.issues.length > 0 && (
            <Alert type="error" showIcon message={t('errors.Validation')} className="form-alert" />
          )}
          {create.isPending && <Alert type="info" showIcon message={t('wizard.creating')} className="form-alert" />}

          <div className="form-buttons">
            <Button type="primary" htmlType="submit" loading={create.isPending}>
              {lastStep ? t('common.create') : t('common.next')}
            </Button>
            {step > 0 && (
              <Button onClick={() => setStep(step - 1)} disabled={create.isPending}>
                {t('common.back')}
              </Button>
            )}
            <Button onClick={() => navigate('/')} disabled={create.isPending}>
              {t('common.cancel')}
            </Button>
          </div>
        </Form>
      </Card>
    </div>
  )
}

function CompanyStep({
  countries,
  currencies,
  onCountryChange,
}: {
  countries: CountryDto[]
  currencies: { code: string; nameEn: string; nameAr: string }[]
  onCountryChange: (code: string) => void
}) {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const ar = settings.language === 'ar'
  const required = [{ required: true, message: t('wizard.fieldRequired') }]

  return (
    <>
      <Form.Item name="nameEn" label={t('wizard.nameEn')} extra={t('wizard.nameHelp')}>
        <Input autoFocus dir="ltr" />
      </Form.Item>
      <Form.Item name="nameAr" label={t('wizard.nameAr')}>
        <Input dir="rtl" />
      </Form.Item>
      <Form.Item name="countryCode" label={t('wizard.country')} extra={t('wizard.countryHelp')} rules={required}>
        <Select
          showSearch
          onChange={onCountryChange}
          filterOption={(input, option) => {
            const c = countries.find((x) => x.code === option?.value)
            return c ? matchesSearch(input, c.nameEn, c.nameAr, c.code) : false
          }}
          options={countries.map((c) => ({ value: c.code, label: ar ? c.nameAr : c.nameEn }))}
        />
      </Form.Item>
      <Form.Item
        name="baseCurrencyCode"
        label={labelWithTerm(t('wizard.currency'), 'baseCurrency')}
        extra={t('wizard.currencyHelp')}
        rules={required}
      >
        <Select
          showSearch
          filterOption={(input, option) => {
            const c = currencies.find((x) => x.code === option?.value)
            return c ? matchesSearch(input, c.code, c.nameEn, c.nameAr) : false
          }}
          options={currencies.map((c) => ({ value: c.code, label: `${c.code} — ${ar ? c.nameAr : c.nameEn}` }))}
        />
      </Form.Item>
      <div className="form-row">
        <Form.Item name="fiscalYearStartMonth" label={labelWithTerm(t('wizard.fiscalYearStartMonth'), 'fiscalYear')} rules={required}>
          <Select
            options={Array.from({ length: 12 }, (_, i) => ({ value: i + 1, label: monthName(i + 1, settings.language) }))}
          />
        </Form.Item>
        <Form.Item name="firstFiscalYear" label={t('wizard.firstFiscalYear')} rules={required}>
          <InputNumber min={1900} max={2200} precision={0} controls={false} dir="ltr" />
        </Form.Item>
      </div>
    </>
  )
}

function TaxStep({ country }: { country?: CountryDto }) {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const ar = settings.language === 'ar'

  if (!country) return null

  return (
    <>
      <Typography.Paragraph>
        {country.taxRegistration.length > 0
          ? t('wizard.taxesIntro')
          : t('wizard.taxesNone', { country: ar ? country.nameAr : country.nameEn })}{' '}
        <TermTooltip term="taxRegistration" />
      </Typography.Paragraph>
      {country.taxRegistration.map((rule, index) => (
        <Form.Item
          key={rule.key}
          name={['taxNumbers', rule.key]}
          label={ar ? rule.nameAr : rule.nameEn}
          rules={[
            { required: rule.requiredForCompany, message: t('validation.tax.required') },
            {
              validator: async (_, value: string | undefined) => {
                if (value && rule.pattern && !new RegExp(rule.pattern).test(value.trim())) {
                  throw new Error(t('wizard.taxInvalid'))
                }
              },
            },
          ]}
        >
          <Input dir="ltr" autoFocus={index === 0} />
        </Form.Item>
      ))}
    </>
  )
}

function ChartStep({ country }: { country?: CountryDto }) {
  const { t } = useTranslation()
  const { settings } = useSettings()
  const ar = settings.language === 'ar'

  return (
    <>
      <Typography.Paragraph>
        {t('wizard.chartIntro')} <TermTooltip term="chartOfAccounts" />
      </Typography.Paragraph>
      <Form.Item name="chartTemplateKey" label={t('wizard.steps.chart')}>
        <Radio.Group className="vertical-options">
          {(country?.chartsOfAccounts ?? []).map((chart) => (
            <Radio key={chart.key} value={chart.key}>
              <strong>{ar ? chart.nameAr : chart.nameEn}</strong>{' '}
              <span className="muted">{t('wizard.chartAccounts', { count: chart.accountCount })}</span>
            </Radio>
          ))}
          <Radio value={blankChart}>
            <strong>{t('wizard.chartBlank')}</strong> <span className="muted">{t('wizard.chartBlankHelp')}</span>
          </Radio>
        </Radio.Group>
      </Form.Item>
    </>
  )
}

function ModulesStep({ keys }: { keys: string[] }) {
  const { t } = useTranslation()

  return (
    <>
      <Typography.Paragraph>
        {t('wizard.modulesIntro')} <TermTooltip term="modules" />
      </Typography.Paragraph>
      <Form.Item name="enabledModules" label={t('wizard.steps.modules')}>
        <Checkbox.Group className="vertical-options">
          {keys.map((key) => (
            <Checkbox key={key} value={key}>
              <strong>{t(`modules.${key}.name`)}</strong>{' '}
              <span className="muted">{t(`modules.${key}.description`)}</span>
            </Checkbox>
          ))}
        </Checkbox.Group>
      </Form.Item>
    </>
  )
}

function FileStep({ form, fileDialogs }: { form: FormInstance<WizardValues>; fileDialogs: boolean }) {
  const { t } = useTranslation()
  const { message } = App.useApp()

  const choose = useMutation({
    mutationFn: () => {
      const { nameEn, nameAr } = form.getFieldsValue(true) as WizardValues
      return pickCompanyFileToSave({ suggestedFileName: suggestedFileName(nameEn, nameAr) })
    },
    onSuccess: (response) => {
      const path = response.status === 200 ? response.data.path : null
      if (path) form.setFieldValue('filePath', path)
    },
    onError: (error) => void message.error(errorMessage(error, t)),
  })

  return (
    <>
      <Typography.Paragraph>{t('wizard.passwordIntro')}</Typography.Paragraph>
      <Alert
        type="warning"
        showIcon
        message={t('wizard.passwordWarningTitle')}
        description={t('wizard.passwordWarning')}
        className="form-alert"
      />
      <Form.Item
        name="password"
        label={labelWithTerm(t('wizard.password'), 'filePassword')}
        rules={[
          { required: true, message: t('wizard.fieldRequired') },
          { min: minimumPasswordLength, message: t('validation.password.too-short', { min: minimumPasswordLength }) },
        ]}
      >
        <Input.Password autoComplete="new-password" autoFocus />
      </Form.Item>
      <Form.Item
        name="passwordConfirm"
        label={t('wizard.passwordConfirm')}
        dependencies={['password']}
        rules={[
          { required: true, message: t('wizard.fieldRequired') },
          ({ getFieldValue }) => ({
            validator: async (_, value: string | undefined) => {
              if (value && value !== getFieldValue('password')) throw new Error(t('wizard.passwordMismatch'))
            },
          }),
        ]}
      >
        <Input.Password autoComplete="new-password" />
      </Form.Item>
      <Form.Item label={t('wizard.filePath')} extra={t('wizard.filePathHelp')} required>
        <div className="file-row">
          <Form.Item name="filePath" noStyle rules={[{ required: true, message: t('validation.path.required') }]}>
            <Input dir="ltr" readOnly={fileDialogs} placeholder={t('start.pathPlaceholder')} />
          </Form.Item>
          {fileDialogs && (
            <Button onClick={() => choose.mutate()} loading={choose.isPending}>
              {t('wizard.chooseLocation')}
            </Button>
          )}
        </div>
      </Form.Item>
    </>
  )
}
