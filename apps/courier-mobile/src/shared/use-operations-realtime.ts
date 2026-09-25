import { env } from '@/config/env';
import { getAccessToken } from '@/features/auth/session-storage';
import { orderKeys } from '@/features/orders/order-api';
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';

export function useOperationsRealtime() {
  const queryClient = useQueryClient();

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl(`${env.coreApiUrl}/hubs/operations`, { accessTokenFactory: async () => (await getAccessToken()) ?? '' })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    const refreshOrders = () => {
      queryClient.invalidateQueries({ queryKey: orderKeys.all });
      queryClient.invalidateQueries({ queryKey: ['courier-profile'] });
    };
    connection.on('orderChanged', refreshOrders);
    connection.on('orderRemoved', refreshOrders);
    connection.start().catch(() => undefined);

    return () => {
      connection.off('orderChanged', refreshOrders);
      connection.off('orderRemoved', refreshOrders);
      void connection.stop();
    };
  }, [queryClient]);
}
