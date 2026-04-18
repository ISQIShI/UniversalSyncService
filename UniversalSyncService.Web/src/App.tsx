import { Routes, Route } from 'react-router-dom';
import { AppLayout } from './layout/AppLayout.tsx';
import { DashboardPage } from './pages/DashboardPage.tsx';
import { PlansPage } from './pages/PlansPage.tsx';
import { NodesPage } from './pages/NodesPage.tsx';
import { SystemPage } from './pages/SystemPage.tsx';

function App() {
  return (
    <Routes>
      <Route path="/" element={<AppLayout />}>
        <Route index element={<DashboardPage />} />
        <Route path="plans" element={<PlansPage />} />
        <Route path="plans/:planId" element={<PlansPage />} />
        <Route path="nodes" element={<NodesPage />} />
        <Route path="system" element={<SystemPage />} />
      </Route>
    </Routes>
  );
}

export default App;



