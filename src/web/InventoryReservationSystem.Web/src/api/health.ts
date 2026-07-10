import { requestAbsolute } from './http';
import type { HealthSnapshot } from '../types/health';

export const healthApi = {
  live(signal?: AbortSignal): Promise<HealthSnapshot> {
    return requestAbsolute<HealthSnapshot>('/health', { method: 'GET', signal });
  },
  ready(signal?: AbortSignal): Promise<HealthSnapshot> {
    return requestAbsolute<HealthSnapshot>('/health/ready', { method: 'GET', signal });
  },
};
