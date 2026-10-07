import { Spin } from 'antd'
import { useQuery } from '@tanstack/react-query'
import { Navigate } from 'react-router'
import { listVouchers } from '../../api/generated/baba'

/** /vouchers/opening. There is only ever one opening-balances voucher, so this opens it, or a new one when there is none yet. */
export function OpeningPage() {
  const query = useQuery({
    queryKey: ['/api/vouchers', 'opening-redirect'],
    queryFn: async () => (await listVouchers({ kind: 'Opening', limit: 1 })).data,
  })

  if (query.isPending) return <Spin size="large" className="page-spinner" />
  const existing = query.data?.[0]
  return <Navigate to={existing ? `/vouchers/opening/${existing.id}` : '/vouchers/opening/new'} replace />
}
