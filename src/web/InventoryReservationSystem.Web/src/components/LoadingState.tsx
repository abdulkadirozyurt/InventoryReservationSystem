interface LoadingStateProps {
  label?: string;
}

export default function LoadingState({ label = 'Loading…' }: LoadingStateProps) {
  return (
    <div aria-live="polite" className="loading">
      <span className="spinner" aria-hidden="true" />
      <span style={{ marginLeft: '0.5rem' }}>{label}</span>
    </div>
  );
}
