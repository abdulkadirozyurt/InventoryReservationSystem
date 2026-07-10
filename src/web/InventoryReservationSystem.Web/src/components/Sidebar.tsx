import { NavLink } from 'react-router-dom';

interface NavItem {
  to: string;
  label: string;
  end?: boolean;
}

const NAV: NavItem[] = [
  { to: '/', label: 'Overview', end: true },
  { to: '/orders', label: 'Orders' },
  { to: '/inventory', label: 'Inventory', end: true },
  { to: '/inventory/transfers', label: 'Stock Transfers', end: true },
  { to: '/inventory/snapshots', label: 'Snapshots', end: true },
];

export default function Sidebar() {
  return (
    <aside className="app-shell__sidebar" aria-label="Primary navigation">
      <div className="app-shell__sidebar-inner">
        <NavLink to="/" className="app-shell__sidebar-brand">
          <span className="app-shell__sidebar-brand-mark" aria-hidden="true">IR</span>
          <span className="app-shell__sidebar-brand-name">Inventory Reservations</span>
        </NavLink>
        <nav className="app-shell__sidebar-nav">
          {NAV.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.end}
              className={({ isActive }) =>
                isActive
                  ? 'app-shell__sidebar-link app-shell__sidebar-link--active'
                  : 'app-shell__sidebar-link'
              }
            >
              {item.label}
            </NavLink>
          ))}
        </nav>
        <footer className="app-shell__sidebar-foot">
          <span className="hint">OrderService / InventoryService</span>
        </footer>
      </div>
    </aside>
  );
}
