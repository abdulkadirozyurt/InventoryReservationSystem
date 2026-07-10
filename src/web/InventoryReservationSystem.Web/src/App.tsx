import { Route, Routes } from 'react-router-dom';

import AppShell from './components/AppShell';
import HomePage from './pages/HomePage';
import OrdersPage from './pages/OrdersPage';
import OrderDetailPage from './pages/OrderDetailPage';
import CreateOrderPage from './pages/CreateOrderPage';
import BulkCancelPage from './pages/BulkCancelPage';
import InventoryPage from './pages/InventoryPage';
import StockTransfersPage from './pages/StockTransfersPage';
import SnapshotsPage from './pages/SnapshotsPage';
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
        <Route path="/inventory" element={<InventoryPage />} />
        <Route path="/inventory/transfers" element={<StockTransfersPage />} />
        <Route path="/inventory/snapshots" element={<SnapshotsPage />} />
        <Route path="*" element={<NotFoundPage />} />
      </Route>
    </Routes>
  );
}
