import { useContext } from 'react';

import { ToastContext } from '../components/ToastProvider';
import type { ToastContextValue } from '../components/ToastProvider';

export function useToast(): ToastContextValue {
  const ctx = useContext(ToastContext);
  if (!ctx) {
    throw new Error('useToast must be used within a ToastProvider');
  }
  return ctx;
}
