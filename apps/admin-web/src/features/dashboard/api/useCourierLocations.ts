import { useEffect } from 'react'
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { getAccessToken, getCoreApiUrl, getJson } from '../../../shared/api/httpClient'
import type { PagedResponse } from '../../../shared/api/types'

export interface CourierLocation {
  courierId: string
  businessId: string
  courierName: string
  latitude: number
  longitude: number
  accuracyMeters?: number
  speedMetersPerSecond?: number
  headingDegrees?: number
  recordedAtUtc: string
  isStale: boolean
  availability: 'Offline' | 'Available' | 'OnBreak' | 'OffShift' | number
  deliveryStatus: 'WaitingForAssignment' | 'GoingToPickup' | 'Delivering' | number
}

const key = ['courier-locations'] as const

async function getAllCourierLocations(signal: AbortSignal) {
  const firstPage = await getJson<PagedResponse<CourierLocation>>('/api/v1/locations/latest?page=1&pageSize=100', signal)
  if (firstPage.totalPages <= 1) return firstPage
  const remainingPages = await Promise.all(Array.from({ length: firstPage.totalPages - 1 }, (_, index) =>
    getJson<PagedResponse<CourierLocation>>(`/api/v1/locations/latest?page=${index + 2}&pageSize=100`, signal)))
  return { ...firstPage, items: [firstPage, ...remainingPages].flatMap(page => page.items), pageSize: firstPage.totalCount }
}

export function useCourierLocations() {
  const queryClient = useQueryClient()
  const query = useQuery({
    queryKey: key,
    queryFn: ({ signal }) => getAllCourierLocations(signal),
    refetchInterval: 60_000,
  })

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl(`${getCoreApiUrl()}/hubs/operations`, { accessTokenFactory: () => getAccessToken() ?? '' })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()
    connection.on('courierLocationUpdated', (location: CourierLocation) => {
      queryClient.setQueryData<PagedResponse<CourierLocation>>(key, current => {
        if (!current) return current
        const existed = current.items.some(item => item.courierId === location.courierId)
        return { ...current, totalCount: current.totalCount + (existed ? 0 : 1), items: [...current.items.filter(item => item.courierId !== location.courierId), location] }
      })
    })
    connection.on('courierLocationStale', ({ courierId }: { courierId: string }) => {
      queryClient.setQueryData<PagedResponse<CourierLocation>>(key, current => current && ({ ...current, items: current.items.map(item =>
        item.courierId === courierId ? { ...item, isStale: true } : item) }))
    })
    void connection.start()
    return () => { void connection.stop() }
  }, [queryClient])

  return query
}
