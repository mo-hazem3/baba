import type { QueryClient } from '@tanstack/react-query'
import {
  getGetCurrentCompanyQueryKey,
  getGetHostInfoQueryKey,
  getListRecentFilesQueryKey,
  getStartup,
  useGetCurrentCompany,
  useGetHostInfo,
  useListCountries,
  useListCurrencies,
  useListRecentFiles,
} from './generated/baba'

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
