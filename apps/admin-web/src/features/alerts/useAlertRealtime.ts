import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { useQueryClient } from '@tanstack/react-query'
import { useEffect } from 'react'
import { getAccessToken, getCoreApiUrl } from '../../shared/api/httpClient'

export function useAlertRealtime() {
  const queryClient = useQueryClient()
  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl(`${getCoreApiUrl()}/hubs/operations`, { accessTokenFactory: () => getAccessToken() ?? '' })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()
    connection.on('operationalAlertChanged', () => {
      void queryClient.invalidateQueries({ queryKey: ['operational-alerts'] })
    })
    void connection.start()
    return () => { void connection.stop() }
  }, [queryClient])
}
