import type { ReactNode } from 'react';

interface StatTileProps {
  label: ReactNode;
  value: ReactNode;
  hint?: ReactNode;
  tone?: 'neutral' | 'success' | 'warning' | 'danger';
}

const TONE_BG: Record<NonNullable<StatTileProps['tone']>, string> = {
  neutral: 'stat-tile',
  success: 'stat-tile stat-tile--success',
  warning: 'stat-tile stat-tile--warning',
  danger: 'stat-tile stat-tile--danger',
};

export default function StatTile({ label, value, hint, tone = 'neutral' }: StatTileProps) {
  return (
    <div className={TONE_BG[tone]}>
      <div className="stat-tile__label">{label}</div>
      <div className="stat-tile__value">{value}</div>
      {hint && <div className="stat-tile__hint">{hint}</div>}
    </div>
  );
}
