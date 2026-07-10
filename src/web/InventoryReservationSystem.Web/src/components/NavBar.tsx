import { NavLink } from 'react-router-dom';

const LINKS = [
  { to: '/', label: 'Overview', end: true },
  { to: '/orders', label: 'Orders', end: false },
  { to: '/orders/new', label: 'New order', end: false },
  { to: '/orders/bulk-cancel', label: 'Bulk cancel', end: false },
  { to: '/health', label: 'Health', end: false },
] as const;

export default function NavBar() {
  return (
    <nav className="app-shell__nav">
      <div className="app-shell__nav-inner">
        <NavLink to="/" className="app-shell__brand">
          Inventory Reservation
        </NavLink>
        <div className="app-shell__links">
          {LINKS.map((link) => (
            <NavLink
              key={link.to}
              to={link.to}
              end={link.end}
              className={({ isActive }) =>
                isActive ? 'app-shell__link app-shell__link--active' : 'app-shell__link'
              }
            >
              {link.label}
            </NavLink>
          ))}
        </div>
      </div>
    </nav>
  );
}
