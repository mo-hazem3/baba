import { Spin } from 'antd'
import { useQuery } from '@tanstack/react-query'
import { Navigate, useParams } from 'react-router'
import { getVoucher } from '../../api/generated/baba'
import { voucherPath } from '../reports/reportModel'

/** /vouchers/open/:id. A report row only knows a voucher's id, so this finds its kind and goes to the right form. */
export function VoucherOpenPage() {
  const { id } = useParams()
  const query = useQuery({
    queryKey: ['/api/vouchers', 'open', id],
    enabled: id !== undefined,
    queryFn: async () => {
      const response = await getVoucher(id!)
      return response.status === 200 ? response.data : null
    },
  })

  if (query.isPending) return <Spin size="large" className="page-spinner" />
  if (query.data?.documentId) return <Navigate to={`/documents/open/${query.data.documentId}`} replace />
  return query.data ? <Navigate to={voucherPath(query.data.kind, query.data.id)} replace /> : <Navigate to="/" replace />
}
