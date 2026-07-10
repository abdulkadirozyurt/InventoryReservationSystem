import { Link } from 'react-router-dom';

import Card from '../components/Card';

export default function NotFoundPage() {
  return (
    <Card title="404" subtitle="No page here yet.">
      <p>
        <Link to="/">← back to overview</Link>
      </p>
    </Card>
  );
}
