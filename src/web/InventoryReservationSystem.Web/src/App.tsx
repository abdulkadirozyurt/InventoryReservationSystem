import { Route, Routes } from 'react-router-dom';

import AppShell from './components/AppShell';
import HomePage from './pages/HomePage';
import OrdersPage from './pages/OrdersPage';
import OrderDetailPage from './pages/OrderDetailPage';
import CreateOrderPage from './pages/CreateOrderPage';
import BulkCancelPage from './pages/BulkCancelPage';
import HealthPage from './pages/HealthPage';
import NotFoundPage from './pages/NotFoundPage';

export default function App() {
  return (
    <Routes>
      <Route element={<AppShell />}>
        <Route index element={<HomePage />} />
        <Route path="/orders" element={<OrdersPage />} />
        <Route path="/orders/new" element={<CreateOrderPage />} />
        <Route path="/orders/bulk-cancel" element={<BulkCancelPage />} />
        <Route path="/orders/:orderNumber" element={<OrderDetailPage />} />
        <Route path="/health" element={<HealthPage />} />
        <Route path="*" element={<NotFoundPage />} />
      </Route>
    </Routes>
  );
}
