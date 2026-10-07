import type { QueryClient } from '@tanstack/react-query'
import {
  getGetCurrentCompanyQueryKey,
  getGetHostInfoQueryKey,
  getListRecentFilesQueryKey,
  getStartup,
  useGetCurrentCompany,
  useGetDashboard,
  useGetHostInfo,
  useGetPrintSettings,
  useGetEndOfServicePosition,
  useGetLeaveBalances,
  useGetPayrollRun,
  useGetPayrollSettings,
  useGetStockLevels,
  useListEmployees,
  useListLeave,
  useListPayrollRuns,
  useListSalaryComponents,
  useListAssets,
  useListAccounts,
  useListBankAccounts,
  useListFiscalYears,
  useListCostCenters,
  useListExchangeRates,
  useListCountries,
  useListCurrencies,
  useListParties,
  useListTaxCodes,
  useListPriceLists,
  useListProducts,
  useListRecentFiles,
  useListStockDocuments,
  useListWarehouses,
} from './generated/baba'
import type { ListPartiesParams } from './generated/model'

// Thin wrappers over the generated client that hand back plain data instead of { data, status, headers }.

/** The open company, or null when none is open. */
export const useCurrentCompany = () =>
  useGetCurrentCompany({ query: { select: (r) => (r.status === 200 ? r.data : null), staleTime: Infinity } })

/** What this host can do, for example whether native file dialogs exist. */
export const useHost = () => useGetHostInfo({ query: { select: (r) => r.data, staleTime: Infinity } })

export const useCountries = () => useListCountries({ query: { select: (r) => r.data, staleTime: Infinity } })

export const useCurrencies = () => useListCurrencies({ query: { select: (r) => r.data, staleTime: Infinity } })

export const useRecentFiles = () => useListRecentFiles({ query: { select: (r) => r.data } })

export const refreshCompany = (queryClient: QueryClient) =>
  queryClient.invalidateQueries({ queryKey: getGetCurrentCompanyQueryKey() })

export const refreshRecentFiles = (queryClient: QueryClient) =>
  queryClient.invalidateQueries({ queryKey: getListRecentFilesQueryKey() })

export const hostQueryKey = getGetHostInfoQueryKey

/** Asks the app which company file it was started with. The server hands it over only once. */
export const fetchStartupFile = async (): Promise<string | null> => {
  const response = await getStartup()
  return response.data.openPath ?? null
}

let firstStartupCheck: Promise<string | null> | undefined

/** The first check at start-up, shared so React's double render in development cannot lose the file. */
export const startupFileOnce = (): Promise<string | null> => (firstStartupCheck ??= fetchStartupFile())

// ---- Accounting ----

export const useAccounts = () => useListAccounts({ query: { select: (r) => r.data } })

export const useParties = (params?: ListPartiesParams) => useListParties(params, { query: { select: (r) => r.data } })

export const useTaxCodes = () => useListTaxCodes({ query: { select: (r) => r.data } })

/** What the open company's country supports (tax codes, tax return ...). Screens follow these, never the country itself. */
export const useCapabilities = () => {
  const company = useCurrentCompany()
  const countries = useCountries()
  return countries.data?.find((c) => c.code === company.data?.countryCode)?.capabilities
}

export const useProducts = () => useListProducts({ query: { select: (r) => r.data } })

export const usePriceLists = () => useListPriceLists({ query: { select: (r) => r.data } })

export const useExchangeRates = () => useListExchangeRates(undefined, { query: { select: (r) => r.data } })

export const useBankAccounts = () => useListBankAccounts({ query: { select: (r) => r.data } })

export const useFiscalYears = () => useListFiscalYears({ query: { select: (r) => r.data } })

export const useEmployees = () => useListEmployees({ query: { select: (r) => r.data } })

export const useSalaryComponents = () => useListSalaryComponents({ query: { select: (r) => r.data } })

export const useLeave = () => useListLeave({ query: { select: (r) => r.data } })

export const useLeaveBalances = (asOf: string) => useGetLeaveBalances({ asOf }, { query: { select: (r) => r.data } })

export const usePayrollSettings = () => useGetPayrollSettings({ query: { select: (r) => r.data } })

export const usePayrollRuns = () => useListPayrollRuns({ query: { select: (r) => r.data } })

export const usePayrollRun = (id: string | undefined) => useGetPayrollRun(id ?? '', { query: { enabled: id !== undefined, select: (r) => (r.status === 200 ? r.data : null) } })

export const useEndOfService = (asOf: string) => useGetEndOfServicePosition({ asOf }, { query: { select: (r) => r.data } })

export const useAssets = () => useListAssets({ query: { select: (r) => r.data } })

export const useWarehouses = () => useListWarehouses({ query: { select: (r) => r.data } })

/** The stock on hand (and its value) per product and warehouse, optionally as of a date. */
export const useStockLevels = (asOf?: string) => useGetStockLevels(asOf ? { asOf } : undefined, { query: { select: (r) => r.data } })

export const useStockDocuments = () => useListStockDocuments(undefined, { query: { select: (r) => r.data } })

export const useCostCenters = () => useListCostCenters({ query: { select: (r) => r.data } })

/** The optional modules this company has switched on. Screens of a module that is off are not offered. */
export const useModules = (): ReadonlySet<string> => {
  const company = useCurrentCompany()
  return new Set(company.data?.enabledModules ?? [])
}

export const useDashboard = () => useGetDashboard({ query: { select: (r) => r.data } })

export const usePrintSettings = () => useGetPrintSettings({ query: { select: (r) => r.data } })

/**
 * Anything that changes the books changes balances, reports and lists. Pages that are not on screen are read again too ("all"), so
 * going to the Summary after posting never shows the balances from before for a moment.
 */
export const refreshBooks = (queryClient: QueryClient) => queryClient.invalidateQueries({ refetchType: 'all' })
