import type { QueryClient } from '@tanstack/react-query'
import {
  getGetCurrentCompanyQueryKey,
  getGetSessionQueryKey,
  getListApprovalsQueryKey,
  useGetPermissionCatalog,
  useGetSession,
  useListApprovals,
  useListRoles,
  useListUsers,
  getGetHostInfoQueryKey,
  getListRecentFilesQueryKey,
  getStartup,
  useGetCurrentCompany,
  useGetDashboard,
  useGetHostInfo,
  useGetPrintSettings,
  useGetBudget,
  useGetEndOfServicePosition,
  useGetLeaveBalances,
  useGetPayrollRun,
  useGetPayrollSettings,
  useGetStockLevels,
  useListBudgetYears,
  useListClaims,
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

export const refreshCompany = (queryClient: QueryClient) => {
  // Another company, or none: nobody is signed in to it yet. Not waited for, so the screens that follow the company are not held up.
  void queryClient.invalidateQueries({ queryKey: getGetSessionQueryKey() })
  return queryClient.invalidateQueries({ queryKey: getGetCurrentCompanyQueryKey() })
}

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

export const useClaims = () => useListClaims({ query: { select: (r) => r.data } })

export const useBudget = (fiscalYear: number, costCenterId: string | undefined) =>
  useGetBudget({ fiscalYear, ...(costCenterId ? { costCenterId } : {}) }, { query: { select: (r) => r.data } })

export const useBudgetYears = () => useListBudgetYears({ query: { select: (r) => r.data } })

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

export const useDashboard = (enabled = true) => useGetDashboard({ query: { select: (r) => r.data, enabled } })

// ---- Users, roles and approval ----

/** Who is signed in and what they may do. Every permission when the company has no user accounts. */
export const useSession = () => useGetSession({ query: { select: (r) => r.data, staleTime: Infinity } })

export const useUsers = () => useListUsers({ query: { select: (r) => r.data } })

export const useRoles = () => useListRoles({ query: { select: (r) => r.data } })

export const usePermissionCatalog = () => useGetPermissionCatalog({ query: { select: (r) => r.data, staleTime: Infinity } })

/** What waits for approval and what the signed-in person sent. Read again every half minute so a decision shows up. */
export const useApprovals = (enabled = true) => useListApprovals({ query: { select: (r) => r.data, enabled, refetchInterval: 30_000 } })

export const refreshApprovals = (queryClient: QueryClient) => queryClient.invalidateQueries({ queryKey: getListApprovalsQueryKey() })

export const usePrintSettings = () => useGetPrintSettings({ query: { select: (r) => r.data } })

/**
 * Anything that changes the books changes balances, reports and lists. Pages that are not on screen are read again too ("all"), so
 * going to the Summary after posting never shows the balances from before for a moment. A list that is still being read for the first time
 * (a slow first moment after opening a company) is read again from the start, because that first reading may have happened before the change.
 */
export const refreshBooks = async (queryClient: QueryClient) => {
  await queryClient.cancelQueries()
  await queryClient.invalidateQueries({ refetchType: 'all' })
}
