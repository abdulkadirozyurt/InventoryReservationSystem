interface ErrorBannerProps {
  message: string;
  variant?: 'danger' | 'warning' | 'info';
  code?: string;
}

export default function ErrorBanner({ message, variant = 'danger', code }: ErrorBannerProps) {
  return (
    <div className={`banner banner--${variant}`} role="alert" title={code ?? undefined}>
      <span>{message}</span>
    </div>
  );
}
