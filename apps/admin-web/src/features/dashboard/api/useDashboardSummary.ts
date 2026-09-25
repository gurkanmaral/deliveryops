import { useQuery } from '@tanstack/react-query'
import { getJson } from '../../../shared/api/httpClient'

export interface DashboardSummary {
  activeBusinesses: number
  activeCouriers: number
  openOrders: number
  deliveringCouriers: number
}

export function useDashboardSummary() {
  return useQuery({
    queryKey: ['dashboard', 'summary'],
    queryFn: ({ signal }) => getJson<DashboardSummary>('/api/v1/dashboard/summary', signal),
  })
}
