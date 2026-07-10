import { useQuery } from '@tanstack/react-query';

import { healthApi } from '../api/health';
import type { HealthSnapshot } from '../types/health';
import { isApiError } from '../types/api';

export interface HealthState {
  live: HealthSnapshot | undefined;
  ready: HealthSnapshot | undefined;
  overall: 'Healthy' | 'Degraded' | 'Unknown';
  isLoading: boolean;
  error: string | null;
  refetch: () => void;
}

const POLL_INTERVAL_MS = 30_000;

function deriveOverall(live?: HealthSnapshot, ready?: HealthSnapshot): HealthState['overall'] {
  if (!live || !ready) return 'Unknown';
  return live.status === 'Healthy' && ready.status === 'Healthy' ? 'Healthy' : 'Degraded';
}

export function useHealth(): HealthState {
  const liveQ = useQuery({
    queryKey: ['health', 'live'],
    queryFn: ({ signal }) => healthApi.live(signal),
    refetchInterval: POLL_INTERVAL_MS,
  });
  const readyQ = useQuery({
    queryKey: ['health', 'ready'],
    queryFn: ({ signal }) => healthApi.ready(signal),
    refetchInterval: POLL_INTERVAL_MS,
  });

  const errorMessage =
    isApiError(liveQ.error) ? liveQ.error.message :
    isApiError(readyQ.error) ? readyQ.error.message :
    liveQ.error instanceof Error ? liveQ.error.message :
    readyQ.error instanceof Error ? readyQ.error.message :
    null;

  return {
    live: liveQ.data,
    ready: readyQ.data,
    overall: deriveOverall(liveQ.data, readyQ.data),
    isLoading: liveQ.isLoading || readyQ.isLoading,
    error: errorMessage,
    refetch: () => {
      void liveQ.refetch();
      void readyQ.refetch();
    },
  };
}
