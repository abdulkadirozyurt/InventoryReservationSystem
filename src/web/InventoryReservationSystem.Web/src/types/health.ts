export interface HealthSnapshot {
  status: string;
  totalDuration: string;
  entries: Record<string, HealthEntry>;
}

export interface HealthEntry {
  status: string;
  description?: string;
  duration?: string;
  data?: Record<string, unknown>;
}
