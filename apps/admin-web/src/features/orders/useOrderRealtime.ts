import { useEffect } from 'react'
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { useQueryClient } from '@tanstack/react-query'
import { getAccessToken, getCoreApiUrl } from '../../shared/api/httpClient'

export function useOrderRealtime() {
  const queryClient = useQueryClient()
  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl(`${getCoreApiUrl()}/hubs/operations`, { accessTokenFactory: () => getAccessToken() ?? '' })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()
    connection.on('orderChanged', () => {
      void queryClient.invalidateQueries({ queryKey: ['orders'] })
      void queryClient.invalidateQueries({ queryKey: ['dashboard'] })
      void queryClient.invalidateQueries({ queryKey: ['dispatch-queue'] })
      void queryClient.invalidateQueries({ queryKey: ['dispatch-attempts'] })
    })
    void connection.start()
    return () => { void connection.stop() }
  }, [queryClient])
}
