import Card from '../components/Card';
import LoadingState from '../components/LoadingState';
import ErrorBanner from '../components/ErrorBanner';
import StatTile from '../components/StatTile';
import { useHealth } from '../hooks/useHealth';
import { describeError } from '../hooks/useOrders';
import { errorCodeToUserMessage } from '../utils/errorMessages';
import { Link } from 'react-router-dom';

export default function HealthPage() {
  const { live, ready, overall, isLoading, error, refetch } = useHealth();

  if (error) {
    const { code, message, status } = describeError(error);
    return <ErrorBanner message={errorCodeToUserMessage(code, status, message)} code={code} />;
  }
  if (isLoading) return <LoadingState label="Probing backend…" />;

  const liveEntries = live?.entries ? Object.entries(live.entries) : [];
  const readyEntries = ready?.entries ? Object.entries(ready.entries) : [];

  return (
    <div className="page-stack">
      <Card
        title="Backend health"
        subtitle="OrderService liveness + readiness (MongoDB, Redis cache)."
        actions={
          <>
            <button className="btn" onClick={refetch}>Refresh</button>
            <Link className="btn" to="/">← Overview</Link>
          </>
        }
      >
        <div className="kpi-grid">
          <StatTile
            label="Overall"
            value={
              <span
                className={
                  overall === 'Healthy' ? 'banner banner--success'
                  : overall === 'Degraded' ? 'banner banner--warning'
                  : 'banner'
                }
              >
                {overall}
              </span>
            }
          />
          <StatTile label="Live" value={live?.status ?? '—'} hint={live?.totalDuration} />
          <StatTile label="Ready" value={ready?.status ?? '—'} hint={ready?.totalDuration} />
        </div>
      </Card>

      <Card title="Liveness probes">
        {liveEntries.length === 0 ? <p className="empty">No probes reported.</p> : (
          <ul className="probe-list">
            {liveEntries.map(([name, e]) => (
              <li key={name}>
                <strong>{name}</strong>: {e.status} {e.description ? `— ${e.description}` : ''}
                {e.duration ? <span className="text-muted"> ({e.duration})</span> : null}
              </li>
            ))}
          </ul>
        )}
      </Card>

      <Card title="Readiness probes">
        {readyEntries.length === 0 ? <p className="empty">No probes reported.</p> : (
          <ul className="probe-list">
            {readyEntries.map(([name, e]) => (
              <li key={name}>
                <strong>{name}</strong>: {e.status} {e.description ? `— ${e.description}` : ''}
                {e.duration ? <span className="text-muted"> ({e.duration})</span> : null}
              </li>
            ))}
          </ul>
        )}
      </Card>
    </div>
  );
}
